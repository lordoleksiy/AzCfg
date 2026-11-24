using System.Text.Json;
using AzCfg.Cli.Commands;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests.Commands;

/// <summary>The guards of <c>azcfg use</c> (ADR 0013): blocked keys, first activation with unmanaged keys, protected environments.</summary>
public sealed class UseCommandTests
{
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/r/Orders.csproj", LocalFileFormat.NestedJson);

    private sealed class Stubs
    {
        public string? Active { get; init; }
        public IReadOnlyList<string> Blocked { get; init; } = [];
        public IReadOnlyList<string> Unmanaged { get; init; } = [];
        public Exception? StatusError { get; init; }
        public List<(string Env, bool Force)> Pulled { get; } = [];
    }

    private sealed class StubState : IUserStateStore
    {
        public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) =>
            Task.FromResult<UserProfile?>(new([Env("dev"), Env("stage"), Env("prod", true)]));

        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSnapshotsAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubProjects(Stubs s) : IProjectService
    {
        public Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult(new ProjectResolution([], true, [], new ProjectBinding("/r", "orders", Target, Target, new Dictionary<string, Uri>(), s.Active)));

        public Task<ProjectBinding> BindAsync(string repoPath, string service, LocalTargetBinding valuesTarget, LocalTargetBinding secretsTarget, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding> AddIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding> RemoveIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubSync(Stubs s) : ISyncService
    {
        public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct)
        {
            s.Pulled.Add((env.Name, force));
            return Task.FromResult(new PullResult(env.Name, 2, 1, false, []));
        }

        public Task<SyncSummary> StatusAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => s.StatusError is { } error
            ? Task.FromException<SyncSummary>(error)
            : Task.FromResult(new SyncSummary(env.Name, true, s.Blocked.Count, 0, [], s.Blocked, [], 0));

        public Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct) =>
            Task.FromResult(binding.ActiveEnvironment is null ? s.Unmanaged : []);

        public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct) => throw new NotSupportedException();
        public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteConfig> ReadRemoteConfigAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) => throw new NotSupportedException();
        public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictResolution resolution, CancellationToken ct) => throw new NotSupportedException();
    }

    private static ConfigEnvironment Env(string name, bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);

    private static async Task<(int Exit, string Output)> Run(Stubs s, string input, params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.NoColors, Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var services = new ServiceCollection()
            .AddSingleton<IUserStateStore>(new StubState())
            .AddSingleton<IProjectService>(new StubProjects(s))
            .AddSingleton<IProjectSettingsService>(new StubProjectSettings())
            .AddSingleton<ISyncService>(new StubSync(s))
            .AddSingleton<IEnvironmentResolver, SeparateStoresResolver>();
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.ConfigureConsole(new ScriptedConsole(console, new ScriptedInput(input)));
            c.AddCommand<UseCommand>("use");
        });
        var exit = await app.RunAsync(["use", .. args]);
        return (exit, writer.ToString());
    }

    [Fact]
    public async Task Blocked_lists_keys_with_hints_and_pulls_nothing()
    {
        var s = new Stubs { Active = "dev", Blocked = ["Db:Host", "Api:Key"] };

        var (exit, output) = await Run(s, "", "stage");

        Assert.Equal(1, exit);
        Assert.Empty(s.Pulled);
        Assert.Contains("Db:Host", output);
        Assert.Contains("Api:Key", output);
        Assert.Contains("push", output);
        Assert.Contains("pull --key", output);
        Assert.Contains("--force", output);
    }

    [Fact]
    public async Task Force_overrides_blocked()
    {
        var s = new Stubs { Active = "dev", Blocked = ["Db:Host"] };

        var (exit, _) = await Run(s, "", "stage", "--force");

        Assert.Equal(0, exit);
        Assert.Equal([("stage", true)], s.Pulled);
    }

    [Fact]
    public async Task Blocked_json_returns_the_keys_without_prompting_and_exits_non_zero()
    {
        var s = new Stubs { Active = "dev", Blocked = ["Db:Host"] };

        var (exit, output) = await Run(s, "", "stage", "--json");

        var doc = JsonDocument.Parse(output).RootElement;
        Assert.Equal(1, exit);
        Assert.Equal("blocked", doc.GetProperty("reason").GetString());
        Assert.Equal("Db:Host", doc.GetProperty("keys")[0].GetString());
        Assert.Empty(s.Pulled);
    }

    [Theory]
    [InlineData("y\n", 0, 1)]
    [InlineData("n\n", 1, 0)]
    public async Task Unreadable_active_environment_warns_and_asks(string answer, int exitCode, int pulls)
    {
        var s = new Stubs { Active = "prod", StatusError = new HttpRequestException("403 Forbidden") };

        var (exit, output) = await Run(s, answer, "dev");

        Assert.Equal(exitCode, exit);
        Assert.Equal(pulls, s.Pulled.Count);
        Assert.Contains("403 Forbidden", output);
    }

    [Fact]
    public async Task Unreadable_active_environment_json_is_refused_as_unverified()
    {
        var s = new Stubs { Active = "prod", StatusError = new HttpRequestException("403 Forbidden") };

        var (exit, output) = await Run(s, "", "dev", "--json");

        var doc = JsonDocument.Parse(output).RootElement;
        Assert.Equal(1, exit);
        Assert.Equal("unverified", doc.GetProperty("reason").GetString());
        Assert.Contains("--force", doc.GetProperty("hint").GetString());
        Assert.Empty(s.Pulled);
    }

    [Theory]
    [InlineData("y\n", 0, 1)]
    [InlineData("n\n", 1, 0)]
    public async Task First_activation_with_unmanaged_keys_asks(string answer, int exitCode, int pulls)
    {
        var s = new Stubs { Unmanaged = ["Legacy:Key"] };

        var (exit, output) = await Run(s, answer, "stage");

        Assert.Equal(exitCode, exit);
        Assert.Equal(pulls, s.Pulled.Count);
        Assert.Contains("Legacy:Key", output);
    }

    [Fact]
    public async Task Yes_skips_the_unmanaged_confirmation()
    {
        var s = new Stubs { Unmanaged = ["Legacy:Key"] };

        var (exit, _) = await Run(s, "", "stage", "--yes");

        Assert.Equal(0, exit);
        Assert.Single(s.Pulled);
    }

    [Fact]
    public async Task Unmanaged_keys_are_ignored_when_an_environment_is_already_active()
    {
        var s = new Stubs { Active = "dev", Unmanaged = ["Legacy:Key"] };

        var (exit, _) = await Run(s, "", "stage");

        Assert.Equal(0, exit);
        Assert.Single(s.Pulled);
    }

    [Fact]
    public async Task Unmanaged_json_returns_the_keys_without_prompting_and_exits_non_zero()
    {
        var s = new Stubs { Unmanaged = ["Legacy:Key"] };

        var (exit, output) = await Run(s, "", "stage", "--json");

        var doc = JsonDocument.Parse(output).RootElement;
        Assert.Equal(1, exit);
        Assert.Equal("unmanaged", doc.GetProperty("reason").GetString());
        Assert.Equal("Legacy:Key", doc.GetProperty("keys")[0].GetString());
        Assert.Empty(s.Pulled);
    }

    [Fact]
    public async Task Json_with_yes_pulls_despite_unmanaged_keys()
    {
        var s = new Stubs { Unmanaged = ["Legacy:Key"] };

        var (exit, output) = await Run(s, "", "stage", "--json", "--yes");

        Assert.Equal(0, exit);
        Assert.Equal("stage", JsonDocument.Parse(output).RootElement.GetProperty("environment").GetString());
    }

    [Theory]
    [InlineData("prod\n", 0, 1)]
    [InlineData("PROD\n", 0, 1)]
    [InlineData("stage\n", 1, 0)]
    public async Task Protected_environment_needs_its_name_typed_even_with_yes(string input, int exitCode, int pulls)
    {
        var s = new Stubs { Active = "dev" };

        var (exit, output) = await Run(s, input, "prod", "--yes");

        Assert.Equal(exitCode, exit);
        Assert.Equal(pulls, s.Pulled.Count);
        Assert.Contains("secrets of prod will be written to local files", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirm_option_skips_the_protected_prompt()
    {
        var s = new Stubs { Active = "dev" };

        var (exit, _) = await Run(s, "", "prod", "--confirm", "prod");

        Assert.Equal(0, exit);
        Assert.Equal([("prod", false)], s.Pulled);
    }

    [Fact]
    public async Task Wrong_confirm_value_refuses()
    {
        var s = new Stubs { Active = "dev" };

        var (exit, _) = await Run(s, "", "prod", "--confirm", "stage");

        Assert.Equal(1, exit);
        Assert.Empty(s.Pulled);
    }

    [Fact]
    public async Task Protected_json_without_confirm_returns_the_refusal_and_never_prompts()
    {
        var s = new Stubs { Active = "dev" };

        var (exit, output) = await Run(s, "", "prod", "--json", "--yes");

        var doc = JsonDocument.Parse(output).RootElement;
        Assert.Equal(1, exit);
        Assert.Equal("protected", doc.GetProperty("reason").GetString());
        Assert.Contains("--confirm prod", doc.GetProperty("hint").GetString());
        Assert.Empty(s.Pulled);
    }

    [Fact]
    public async Task Protected_json_with_confirm_pulls()
    {
        var s = new Stubs { Active = "dev" };

        var (exit, _) = await Run(s, "", "prod", "--json", "--confirm", "prod");

        Assert.Equal(0, exit);
        Assert.Single(s.Pulled);
    }

    [Fact]
    public async Task Unprotected_active_environment_pulls_without_any_question()
    {
        var s = new Stubs { Active = "dev" };

        var (exit, _) = await Run(s, "", "stage");

        Assert.Equal(0, exit);
        Assert.Equal([("stage", false)], s.Pulled);
    }
}
