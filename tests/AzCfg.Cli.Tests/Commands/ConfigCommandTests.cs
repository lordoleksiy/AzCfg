using AzCfg.Cli.Commands;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests.Commands;

public sealed class ConfigCommandTests
{
    private static readonly Uri Shared = new("https://shared.azconfig.io");

    private static readonly UserProfile Profile = new(
        [new("dev", Shared, "Development", false), new("prod", Shared, "Production", true)],
        EnvironmentMode.LabelPerEnvironment);

    private sealed class InMemoryState(UserProfile? profile) : IUserStateStore
    {
        public UserProfile? Profile { get; private set; } = profile;

        public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult(Profile);

        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct)
        {
            Profile = profile;
            return Task.CompletedTask;
        }

        public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static async Task<(int Exit, string Output, InMemoryState State)> Run(params string[] args)
    {
        var state = new InMemoryState(Profile);
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), ColorSystem = ColorSystemSupport.NoColors, Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var services = new ServiceCollection()
            .AddSingleton<IUserStateStore>(state)
            .AddSingleton<IUserSettingsService, UserSettingsService>();
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.ConfigureConsole(console);
            c.AddBranch("config", b =>
            {
                b.AddCommand<ConfigListCommand>("list");
                b.AddCommand<ConfigGetCommand>("get");
                b.AddCommand<ConfigSetCommand>("set");
            });
        });
        var exit = await app.RunAsync(["config", .. args]);
        return (exit, writer.ToString(), state);
    }

    [Fact]
    public async Task List_ShowsEverySettingAsKeyValueTable()
    {
        var (exit, output, _) = await Run("list");

        Assert.Equal(0, exit);
        Assert.Matches(@"Key\s.*Value", output);
        Assert.Matches(@"mode\s.*labels", output);
        Assert.Matches(@"env\.prod\.label\s.*Production", output);
        Assert.Matches(@"env\.prod\.protected\s.*true", output);
        Assert.Matches(@"defaultLabel\s.*\(none\)", output);
    }

    [Fact]
    public async Task List_Json_SerializesSettings()
    {
        var (_, output, _) = await Run("list", "--json");

        Assert.Equal(
            """[{"key":"mode","value":"labels"},{"key":"defaultLabel","value":null},{"key":"env.dev.endpoint","value":"https://shared.azconfig.io"},{"key":"env.dev.label","value":"Development"},{"key":"env.dev.protected","value":"false"},{"key":"env.prod.endpoint","value":"https://shared.azconfig.io"},{"key":"env.prod.label","value":"Production"},{"key":"env.prod.protected","value":"true"}]""",
            output.Trim());
    }

    [Fact]
    public async Task Get_ShowsOneSetting()
    {
        var (exit, output, _) = await Run("get", "env.DEV.label");

        Assert.Equal(0, exit);
        Assert.Matches(@"env\.dev\.label\s.*Development", output);
        Assert.DoesNotContain("env.prod", output);
    }

    [Fact]
    public async Task Get_Json_SerializesSetting()
    {
        var (_, output, _) = await Run("get", "mode", "--json");

        Assert.Equal("""{"key":"mode","value":"labels"}""", output.Trim());
    }

    [Fact]
    public async Task Get_UnknownKey_IsAnError()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Run("get", "color"));

        Assert.Contains("env.dev.label", ex.Message);
    }

    [Fact]
    public async Task Set_SavesProfileAndPrintsKeyEqualsValue()
    {
        var (exit, output, state) = await Run("set", "env.dev.protected", "TRUE");

        Assert.Equal(0, exit);
        Assert.Equal("env.dev.protected = true", output.Trim());
        Assert.True(state.Profile!.Environments[0].IsProtected);
    }

    [Fact]
    public async Task Set_Json_SerializesNewSetting()
    {
        var (_, output, state) = await Run("set", "mode", "stores", "--json");

        Assert.Equal("""{"key":"mode","value":"stores"}""", output.Trim());
        Assert.Equal(EnvironmentMode.StorePerEnvironment, state.Profile!.Mode);
    }

    [Fact]
    public async Task Set_EmptyLabel_ClearsIt()
    {
        var (_, output, state) = await Run("set", "env.prod.label", "");

        Assert.Equal("env.prod.label = (none)", output.Trim());
        Assert.Null(state.Profile!.Environments[1].Label);
    }

    [Theory]
    [InlineData("mode", "both")]
    [InlineData("env.dev.endpoint", "not-a-url")]
    [InlineData("env.qa.label", "QA")]
    public async Task Set_InvalidValueOrEnvironment_IsAnError(string key, string value)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Run("set", key, value));

        Assert.Contains(key.Split('.')[^1] == "label" ? "Unknown environment" : "Invalid value", ex.Message);
    }
}
