using System.Text.Json;
using AzCfg.Cli;
using AzCfg.Cli.Commands;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests;

public sealed class StatusCommandTests
{
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/r/Orders.csproj", LocalFileFormat.NestedJson);

    private sealed class Stubs
    {
        public UserProfile? Profile { get; init; } = new([Env("dev"), Env("prod", true)]);
        public ProjectBinding? Existing { get; init; } = Binding("dev");
        public Func<ConfigEnvironment, SyncSummary> SummaryFor { get; init; } = e => new(e.Name, true, 0, 0, [], [], [], 0);
        public List<(ProjectBinding Binding, ConfigEnvironment Env)> Asked { get; } = [];
    }

    private sealed class StubState(Stubs s) : IUserStateStore
    {
        public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult(s.Profile);
        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubProjects(Stubs s) : IProjectService
    {
        public Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct) => Task.FromResult(new ProjectResolution([], true, [], s.Existing));
        public Task<ProjectBinding> BindAsync(string repoPath, string service, LocalTargetBinding valuesTarget, LocalTargetBinding secretsTarget, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding> AddIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding> RemoveIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubSync(Stubs s) : ISyncService
    {
        public Task<SyncSummary> StatusAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
        {
            s.Asked.Add((binding, env));
            return Task.FromResult(s.SummaryFor(env));
        }

        public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct) => throw new NotSupportedException();
        public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteConfig> ReadRemoteConfigAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) => throw new NotSupportedException();
        public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictResolution resolution, CancellationToken ct) => throw new NotSupportedException();
    }

    private static ConfigEnvironment Env(string name, bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);

    private static ProjectBinding Binding(string? active) => new("/r", "orders", Target, Target, new Dictionary<string, Uri>(), active);

    private static async Task<(int Exit, string Output)> Run(Stubs s, params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.No, Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var services = new ServiceCollection()
            .AddSingleton<IUserStateStore>(new StubState(s))
            .AddSingleton<IProjectService>(new StubProjects(s))
            .AddSingleton<ISyncService>(new StubSync(s))
            .AddSingleton<IEnvironmentResolver, SeparateStoresResolver>();
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.ConfigureConsole(console);
            c.AddCommand<StatusCommand>("status");
        });
        var exit = await app.RunAsync(["status", .. args]);
        return (exit, writer.ToString());
    }

    [Fact]
    public async Task Unbound_project_or_missing_profile_or_unknown_env_is_an_error()
    {
        Assert.Contains("azcfg pull", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs { Existing = null }))).Message);
        Assert.Contains("azcfg init", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs { Profile = null }))).Message);
        Assert.Contains("nope", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs(), "--env", "nope"))).Message);
    }

    [Theory]
    [InlineData("--env prod", "dev", "prod")]
    [InlineData("", "prod", "prod")]
    [InlineData("", null, "dev")]
    public async Task Env_is_option_then_active_then_dev(string args, string? active, string expected)
    {
        var s = new Stubs { Existing = Binding(active) };

        var (exit, _) = await Run(s, args.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(0, exit);
        Assert.Equal(expected, Assert.Single(s.Asked).Env.Name);
    }

    [Fact]
    public async Task Human_output_shows_binding_counters_blocked_keys_and_warnings()
    {
        var s = new Stubs
        {
            SummaryFor = e => new(e.Name, true, 2, 1, ["Db:Host"], ["Db:Host", "Feature:X"],
                [new(SyncWarningKind.DeleteSkipped, "Old:Key", "'Old:Key' was deleted locally; it is not deleted remotely (use --delete).")], 3),
        };

        var (exit, output) = await Run(s);

        Assert.Equal(0, exit);
        Assert.Contains("Project: Orders", output);
        Assert.Contains("Service: orders", output);
        Assert.Contains("UserSecrets", output);
        Assert.Contains("Active environment: dev", output);
        Assert.Contains("⇡ 2 local changes, ⇣ 1 remote changes, 1 conflicts", output);
        Assert.Contains("Ignored keys: 3", output);
        Assert.Contains("Db:Host", output);
        Assert.Contains("Feature:X", output);
        Assert.Contains("Warning: 'Old:Key' was deleted", output);
        Assert.DoesNotContain("Up to date.", output);
    }

    [Fact]
    public async Task Clean_environment_says_up_to_date()
    {
        var (_, output) = await Run(new Stubs());

        Assert.Contains("Up to date.", output);
    }

    [Fact]
    public async Task Other_environment_is_reported_as_not_active()
    {
        var s = new Stubs { SummaryFor = e => new(e.Name, false, 0, 0, [], [], [], 0) };

        var (exit, output) = await Run(s, "--env", "prod");

        Assert.Equal(0, exit);
        Assert.Contains("'prod' is not the active one", output);
        Assert.DoesNotContain("Up to date.", output);
    }

    [Fact]
    public async Task Json_prints_binding_info_and_the_summary()
    {
        var s = new Stubs { SummaryFor = e => new(e.Name, true, 2, 1, ["Db:Host"], ["Db:Host"], [new(SyncWarningKind.DeleteSkipped, "Old:Key", "m")], 3) };

        var (exit, output) = await Run(s, "--json");

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.Equal("Orders", root.GetProperty("project").GetString());
        Assert.Equal("orders", root.GetProperty("service").GetString());
        Assert.Equal("userSecrets", root.GetProperty("valuesTarget").GetProperty("kind").GetString());
        Assert.Equal("dev", root.GetProperty("activeEnvironment").GetString());
        var summary = root.GetProperty("summary");
        Assert.True(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, summary.GetProperty("localChanges").GetInt32());
        Assert.Equal(1, summary.GetProperty("remoteChanges").GetInt32());
        Assert.Equal("Db:Host", summary.GetProperty("conflicts")[0].GetString());
        Assert.Equal(3, summary.GetProperty("ignoredCount").GetInt32());
        Assert.Equal("deleteSkipped", summary.GetProperty("warnings")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Json_for_other_environment_reports_is_active_false()
    {
        var s = new Stubs { SummaryFor = e => new(e.Name, false, 0, 0, [], [], [], 0) };

        var (_, output) = await Run(s, "--json", "--env", "prod");

        var summary = JsonDocument.Parse(output).RootElement.GetProperty("summary");
        Assert.False(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal("prod", summary.GetProperty("environment").GetString());
    }
}
