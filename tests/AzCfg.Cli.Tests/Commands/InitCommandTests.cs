using AzCfg.Cli.Commands;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AzCfg.Cli.Tests.Commands;

public sealed class InitCommandTests
{
    private const char Down = '↓';
    private static readonly Uri Shared = new("https://shared.azconfig.io");

    private sealed class FakeSetup(params EnvironmentCandidate[] candidates) : ISetupService
    {
        public UserProfile? Saved;
        public bool Discovered;
        public Dictionary<Uri, string[]> Labels { get; } = [];

        public Task<IReadOnlyList<EnvironmentCandidate>> DiscoverEnvironmentsAsync(CancellationToken ct)
        {
            Discovered = true;
            return Task.FromResult<IReadOnlyList<EnvironmentCandidate>>(candidates);
        }

        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct)
        {
            Saved = profile;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListLabelsAsync(Uri store, CancellationToken ct) => Labels.TryGetValue(store, out var labels)
            ? Task.FromResult<IReadOnlyList<string>>(labels)
            : Task.FromException<IReadOnlyList<string>>(new InvalidOperationException($"Access denied to {store}"));
    }

    private sealed class ScriptedInput(string text) : IAnsiConsoleInput
    {
        private readonly Queue<char> _keys = new(text);

        public bool IsKeyAvailable() => _keys.Count > 0;

        public ConsoleKeyInfo? ReadKey(bool intercept) => _keys.TryDequeue(out var c)
            ? c switch
            {
                '\n' => new('\r', ConsoleKey.Enter, false, false, false),
                ' ' => new(' ', ConsoleKey.Spacebar, false, false, false),
                Down => new('\0', ConsoleKey.DownArrow, false, false, false),
                _ => new(c, default, false, false, false),
            }
            : throw new InvalidOperationException("Unexpected prompt.");

        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
    }

    private sealed class ScriptedConsole(IAnsiConsole inner, IAnsiConsoleInput input) : IAnsiConsole
    {
        public Profile Profile => inner.Profile;
        public IAnsiConsoleCursor Cursor => inner.Cursor;
        public IAnsiConsoleInput Input => input;
        public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;
        public RenderPipeline Pipeline => inner.Pipeline;
        public void Clear(bool home) => inner.Clear(home);
        public void Write(IRenderable renderable) => inner.Write(renderable);
        public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
    }

    private static EnvironmentCandidate Candidate(string store, string? name, bool isProtected = false) => new(
        name, new($"/s/{store}", store, Endpoint(store), "s", "rg", new Dictionary<string, string>()), isProtected);

    private static Uri Endpoint(string store) => new($"https://{store}.azconfig.io");

    private static Task<(int Code, string Output, FakeSetup Setup)> Run(InitSettings settings, params EnvironmentCandidate[] candidates) =>
        Run(settings, new FakeSetup(candidates));

    /// <summary>Runs <c>init</c> on an interactive console that answers prompts from <paramref name="input"/> and fails on any other prompt.</summary>
    private static async Task<(int Code, string Output, FakeSetup Setup)> Run(InitSettings settings, FakeSetup setup, string input = "")
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.NoColors, Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var code = await new InitCommand(setup, new ScriptedConsole(console, new ScriptedInput(input))).ExecuteAsync(null!, settings, CancellationToken.None);
        return (code, writer.ToString(), setup);
    }

    [Fact]
    public void ToEnvironments_MapsNamedCandidatesAndSkipsUndetermined()
    {
        var result = InitCommand.ToEnvironments([Candidate("a-dev", "dev"), Candidate("a-qa", null), Candidate("a-prod", "prod", true)]);

        Assert.Equal(
            [new ConfigEnvironment("dev", new("https://a-dev.azconfig.io"), null, false), new ConfigEnvironment("prod", new("https://a-prod.azconfig.io"), null, true)],
            result);
    }

    [Fact]
    public async Task Json_PrintsCandidatesAndDoesNotSave()
    {
        var (code, output, setup) = await Run(new InitSettings { Json = true }, Candidate("a-prod", "prod", true), Candidate("a-qa", null));

        Assert.Equal(0, code);
        Assert.Null(setup.Saved);
        Assert.Equal(
            """[{"name":"prod","store":{"id":"/s/a-prod","name":"a-prod","endpoint":"https://a-prod.azconfig.io","subscriptionId":"s","resourceGroup":"rg","tags":{}},"isProtected":true},{"name":null,"store":{"id":"/s/a-qa","name":"a-qa","endpoint":"https://a-qa.azconfig.io","subscriptionId":"s","resourceGroup":"rg","tags":{}},"isProtected":false}]""",
            output.Trim());
    }

    [Fact]
    public async Task Yes_SavesDetectedEnvironmentsWithoutPrompting()
    {
        var (code, output, setup) = await Run(new InitSettings { Yes = true }, Candidate("a-dev", "dev"), Candidate("a-qa", null), Candidate("a-prod", "prod", true));

        Assert.Equal(0, code);
        Assert.Equal([("dev", false), ("prod", true)], setup.Saved!.Environments.Select(e => (e.Name, e.IsProtected)));
        Assert.Equal((EnvironmentMode.StorePerEnvironment, null), (setup.Saved.Mode, setup.Saved.DefaultLabel));
        Assert.Contains("Mode: stores", output);
    }

    [Fact]
    public async Task Yes_StoresModeWithLabel_SavesItAsDefaultLabel()
    {
        var (_, output, setup) = await Run(new InitSettings { Yes = true, Mode = "stores", Label = "Development" }, Candidate("a-dev", "dev"));

        Assert.Equal((EnvironmentMode.StorePerEnvironment, "Development"), (setup.Saved!.Mode, setup.Saved.DefaultLabel));
        Assert.Null(Assert.Single(setup.Saved.Environments).Label);
        Assert.Matches(@"dev\s.*a-dev\.azconfig\.io\s.*Development\s.*no", output);
    }

    [Fact]
    public async Task Yes_LabelsMode_CreatesEnvironmentPerLabelInSharedStore()
    {
        var setup = new FakeSetup(Candidate("other", "dev")) { Labels = { [Shared] = ["Development", "Production", "QA"] } };

        var (code, output, _) = await Run(new InitSettings { Yes = true, Mode = "labels", Store = Shared.OriginalString }, setup);

        Assert.Equal(0, code);
        Assert.False(setup.Discovered);
        Assert.Equal(EnvironmentMode.LabelPerEnvironment, setup.Saved!.Mode);
        Assert.Equal(
            [new ConfigEnvironment("dev", Shared, "Development", false), new ConfigEnvironment("prod", Shared, "Production", true), new ConfigEnvironment("qa", Shared, "QA", false)],
            setup.Saved.Environments);
        Assert.Contains("Mode: labels", output);
        Assert.Matches(@"prod\s.*shared\.azconfig\.io\s.*Production\s.*yes", output);
    }

    [Fact]
    public async Task Yes_LabelsModeWithoutStore_IsAnError()
    {
        var setup = new FakeSetup(Candidate("shared", "dev"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new InitSettings { Yes = true, Mode = "labels" }, setup));

        Assert.Equal("--mode labels with --yes requires --store <endpoint>.", ex.Message);
        Assert.Null(setup.Saved);
    }

    [Fact]
    public async Task Interactive_AsksForMode_LabelsMode_NamesSelectedLabels()
    {
        var setup = new FakeSetup(Candidate("shared", null)) { Labels = { [Shared] = ["Development", "Production"] } };
        var input = $"{Down}\n" // mode: one store, a label per environment
            + "\n" // shared store: the discovered one
            + $" {Down} \n" // labels: Development + Production
            + "\n\n" // Development -> dev, not protected
            + "\n\n" // Production -> prod, protected
            + "\n"; // no manual labels

        var (code, output, _) = await Run(new InitSettings(), setup, input);

        Assert.Equal(0, code);
        Assert.Contains("How are environments separated in App Configuration?", output);
        Assert.Equal(EnvironmentMode.LabelPerEnvironment, setup.Saved!.Mode);
        Assert.Equal([new ConfigEnvironment("dev", Shared, "Development", false), new ConfigEnvironment("prod", Shared, "Production", true)], setup.Saved.Environments);
    }

    [Fact]
    public async Task Interactive_StoresMode_OffersLabelsOfChosenStoresIgnoringFailures()
    {
        var setup = new FakeSetup(Candidate("a-dev", "dev"), Candidate("a-prod", "prod", true)) { Labels = { [Endpoint("a-dev")] = ["Shared", "Development"] } };
        var input = "\n" // mode: one store per environment
            + $" {Down} \n" // both stores
            + "\n\n\n\n" // names and protection as detected
            + "\n" // no manual endpoint
            + $"{Down}\n"; // label for new keys: (no label), Development, Shared

        await Run(new InitSettings(), setup, input);

        Assert.Equal((EnvironmentMode.StorePerEnvironment, "Development"), (setup.Saved!.Mode, setup.Saved.DefaultLabel));
        Assert.Equal(
            [new ConfigEnvironment("dev", Endpoint("a-dev"), null, false), new ConfigEnvironment("prod", Endpoint("a-prod"), null, true)],
            setup.Saved.Environments);
    }

    [Fact]
    public async Task Interactive_StoresMode_NoLabelChoice_SavesNoDefaultLabel()
    {
        var setup = new FakeSetup(Candidate("a-dev", "dev")) { Labels = { [Endpoint("a-dev")] = ["Development"] } };

        await Run(new InitSettings { Mode = "stores" }, setup, " \n\n\n\n\n");

        Assert.Null(setup.Saved!.DefaultLabel);
        Assert.Single(setup.Saved.Environments);
    }

    [Fact]
    public async Task Interactive_StoresModeWithLabelOption_DoesNotAskForModeOrLabel()
    {
        var setup = new FakeSetup(Candidate("a-dev", "dev")) { Labels = { [Endpoint("a-dev")] = ["Development"] } };

        await Run(new InitSettings { Mode = "stores", Label = "Custom" }, setup, " \n\n\n\n");

        Assert.Equal("Custom", setup.Saved!.DefaultLabel);
    }

    [Fact]
    public async Task Interactive_LabelsModeWithStore_NoLabelsFound_EntersLabelsManually()
    {
        var setup = new FakeSetup { Labels = { [Shared] = [] } };
        var input = "\n" // add a label manually (default yes)
            + "Staging\n" // label
            + "\n\n" // name: stage (suggested), not protected
            + "\n"; // no more labels

        await Run(new InitSettings { Mode = "labels", Store = Shared.OriginalString }, setup, input);

        Assert.False(setup.Discovered);
        Assert.Equal([new ConfigEnvironment("stage", Shared, "Staging", false)], setup.Saved!.Environments);
    }

    [Theory]
    [InlineData("Development", "dev")]
    [InlineData("Staging", "stage")]
    [InlineData("Stage", "stage")]
    [InlineData("Production", "prod")]
    [InlineData("Prod", "prod")]
    [InlineData("QA-Europe", "qa-europe")]
    public void SuggestName_MapsWellKnownLabels(string label, string expected) =>
        Assert.Equal(expected, InitCommand.SuggestName(label));

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("labels", "https://shared.azconfig.io", true)]
    [InlineData("STORES", null, true)]
    [InlineData("both", null, false)]
    [InlineData("labels", "shared", false)]
    [InlineData("labels", "ftp://shared.azconfig.io", false)]
    public void Settings_ValidateModeAndStore(string? mode, string? store, bool valid) =>
        Assert.Equal(valid, new InitSettings { Mode = mode, Store = store }.Validate().Successful);
}
