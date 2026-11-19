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

public sealed class DiffCommandTests
{
    private const string Canary = "CANARY-s3cret-value";

    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/r/Orders.csproj", LocalFileFormat.NestedJson);

    private sealed class Stubs
    {
        public UserProfile? Profile { get; init; } = new([Env("dev"), Env("test"), Env("prod", true)]);
        public ProjectBinding? Existing { get; init; } = Binding("dev");
        public ConfigComparison Comparison { get; init; } = new([], 0);
        public Func<ConfigEnvironment, RemoteConfig> Remote { get; init; } = e => new(e, [], []);
        public List<(CompareSide Left, CompareSide Right)> Compared { get; } = [];
        public List<string> Read { get; } = [];
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

    private sealed class StubComparison(Stubs s) : IConfigComparisonService
    {
        public Task<ConfigComparison> CompareAsync(ProjectBinding binding, CompareSide left, CompareSide right, CancellationToken ct)
        {
            s.Compared.Add((left, right));
            return Task.FromResult(s.Comparison);
        }
    }

    private sealed class StubSync(Stubs s) : ISyncService
    {
        public Task<RemoteConfig> ReadRemoteConfigAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
        {
            s.Read.Add(env.Name);
            return Task.FromResult(s.Remote(env));
        }

        public Task<SyncSummary> StatusAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct) => throw new NotSupportedException();
        public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) => throw new NotSupportedException();
        public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictResolution resolution, CancellationToken ct) => throw new NotSupportedException();
    }

    private static ConfigEnvironment Env(string name, bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);

    private static ProjectBinding Binding(string? active, IReadOnlyList<string>? ignore = null) =>
        new("/r", "orders", Target, Target, new Dictionary<string, Uri>(), active, ignore);

    private static ConfigComparisonItem Item(string key, ComparisonKind kind, string? left, string? right, bool secret = false) =>
        new(key, kind, left is null && !secret ? null : new(secret, left), right is null && !secret ? null : new(secret, right));

    private static ConfigEntry Plain(string key) => new(key, ConfigScope.Service, new ConfigValue.Plain("v"));

    private static ConfigEntry Secret(string key) =>
        new(key, ConfigScope.Service, new ConfigValue.ResolvedSecret(new Uri("https://kv.vault.azure.net/secrets/x"), new SecretString(Canary)));

    private static async Task<(int Exit, string Output)> Run(Stubs s, params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.No, Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var services = new ServiceCollection()
            .AddSingleton<IUserStateStore>(new StubState(s))
            .AddSingleton<IProjectService>(new StubProjects(s))
            .AddSingleton<ISyncService>(new StubSync(s))
            .AddSingleton<IConfigComparisonService>(new StubComparison(s))
            .AddSingleton<IEnvironmentResolver, SeparateStoresResolver>();
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.ConfigureConsole(console);
            c.AddCommand<DiffCommand>("diff");
        });
        var exit = await app.RunAsync(["diff", .. args]);
        return (exit, writer.ToString());
    }

    private static string EnvName(CompareSide side) => side is CompareSide.Environment e ? e.Env.Name : "local";

    [Theory]
    [InlineData("", "dev", "local", "dev")]
    [InlineData("", null, "local", "dev")]
    [InlineData("prod", "dev", "local", "prod")]
    [InlineData("test prod", "dev", "test", "prod")]
    [InlineData("local prod", "dev", "local", "prod")]
    [InlineData("prod local", "dev", "prod", "local")]
    [InlineData("LOCAL Prod", "dev", "local", "prod")]
    public async Task Argument_forms_pick_the_sides(string args, string? active, string left, string right)
    {
        var s = new Stubs { Existing = Binding(active) };

        var (exit, _) = await Run(s, args.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(0, exit);
        var (l, r) = Assert.Single(s.Compared);
        Assert.Equal((left, right), (EnvName(l), EnvName(r)));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("local local")]
    public async Task Local_against_local_is_a_clear_error(string args)
    {
        var s = new Stubs();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(s, args.Split(' ')));

        Assert.Contains("Cannot compare local with local", ex.Message);
        Assert.Empty(s.Compared);
    }

    [Fact]
    public async Task Unbound_project_missing_profile_and_unknown_env_are_errors()
    {
        Assert.Contains("azcfg pull", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs { Existing = null }))).Message);
        Assert.Contains("azcfg init", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs { Profile = null }))).Message);
        Assert.Contains("'nope'", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs(), "nope"))).Message);
        Assert.Contains("'nope'", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs(), "dev", "nope"))).Message);
    }

    [Fact]
    public async Task Differences_only_by_default_and_same_shows_equal_keys()
    {
        var s = new Stubs
        {
            Comparison = new(
                [
                    Item("Db:Host", ComparisonKind.Different, "localhost", "prod-db"),
                    Item("Timeout", ComparisonKind.Same, "30", "30"),
                    Item("Only:Local", ComparisonKind.OnlyLeft, "x", null),
                    Item("Only:Remote", ComparisonKind.OnlyRight, null, "y"),
                ],
                2),
        };

        var (exit, output) = await Run(s, "prod");

        Assert.Equal(0, exit);
        Assert.Contains("Comparing local ↔ prod", output);
        Assert.Contains("Db:Host", output);
        Assert.Contains("localhost", output);
        Assert.Contains("prod-db", output);
        Assert.Contains("only in local", output);
        Assert.Contains("only in prod", output);
        Assert.DoesNotContain("Timeout", output);
        Assert.Contains("1 equal keys hidden (use --same)", output);
        Assert.Contains("Ignored keys: 2", output);

        (_, output) = await Run(s, "prod", "--same");

        Assert.Contains("Timeout", output);
        Assert.Contains("same", output);
        Assert.DoesNotContain("hidden", output);
    }

    [Fact]
    public async Task No_differences_says_so()
    {
        var (exit, output) = await Run(new Stubs { Comparison = new([Item("A", ComparisonKind.Same, "1", "1")], 0) });

        Assert.Equal(0, exit);
        Assert.Contains("No differences.", output);
    }

    [Fact]
    public async Task Secrets_are_shown_as_names_and_same_or_different_never_values()
    {
        var s = new Stubs
        {
            Comparison = new(
                [
                    Item("Db:Password", ComparisonKind.Different, null, null, secret: true),
                    Item("Api:Key", ComparisonKind.Same, null, null, secret: true),
                    Item("Leaky", ComparisonKind.Different, Canary, Canary, secret: true),
                ],
                0),
        };

        var (_, human) = await Run(s, "prod", "--same");
        var (_, json) = await Run(new Stubs { Comparison = new([Item("Db:Password", ComparisonKind.Different, null, null, secret: true), Item("Api:Key", ComparisonKind.Same, null, null, secret: true)], 0) }, "prod", "--same", "--json");

        Assert.Contains("Db:Password", human);
        Assert.Contains("different", human);
        Assert.Contains("(secret)", human);
        Assert.DoesNotContain(Canary, human);
        Assert.DoesNotContain(Canary, json);
        var items = JsonDocument.Parse(json).RootElement.GetProperty("items");
        Assert.True(items[0].GetProperty("left").GetProperty("isSecret").GetBoolean());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("left").GetProperty("plain").ValueKind);
    }

    [Fact]
    public async Task Json_is_the_comparison_record_without_equal_keys_unless_same()
    {
        var s = new Stubs
        {
            Comparison = new([Item("Db:Host", ComparisonKind.Different, "a", "b"), Item("Timeout", ComparisonKind.Same, "30", "30")], 4),
        };

        var (exit, output) = await Run(s, "prod", "--json");

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.Equal(4, root.GetProperty("ignoredCount").GetInt32());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal("Db:Host", item.GetProperty("key").GetString());
        Assert.Equal("different", item.GetProperty("kind").GetString());
        Assert.Equal("a", item.GetProperty("left").GetProperty("plain").GetString());

        (_, output) = await Run(s, "prod", "--json", "--same");

        Assert.Equal(2, JsonDocument.Parse(output).RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Missing_all_reads_every_environment_and_exits_1_when_a_key_is_missing()
    {
        var s = new Stubs
        {
            Remote = e => new(e, e.Name == "prod" ? [Plain("A")] : [Plain("A"), Plain("B"), Secret("S")], []),
        };

        var (exit, output) = await Run(s, "--missing", "--all");

        Assert.Equal(1, exit);
        Assert.Equal(["dev", "test", "prod"], s.Read);
        Assert.Empty(s.Compared);
        Assert.Contains("Checking dev, test, prod", output);
        Assert.Contains("B", output);
        Assert.Contains("dev, test", output);
        Assert.Contains("2 keys are missing", output);
        Assert.DoesNotContain(Canary, output);
    }

    [Fact]
    public async Task Missing_all_exits_0_when_nothing_is_missing()
    {
        var s = new Stubs { Remote = e => new(e, [Plain("A")], []) };

        var (exit, output) = await Run(s, "--missing", "--all");

        Assert.Equal(0, exit);
        Assert.Contains("No missing keys.", output);
    }

    [Fact]
    public async Task Missing_all_skips_ignored_keys()
    {
        var s = new Stubs
        {
            Existing = Binding("dev", ["Local:*"]),
            Remote = e => new(e, e.Name == "dev" ? [Plain("A"), Plain("Local:Only")] : [Plain("A")], []),
        };

        var (exit, _) = await Run(s, "--missing", "--all");

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Missing_all_json_is_the_report_and_has_no_secret_values()
    {
        var s = new Stubs { Remote = e => new(e, e.Name == "prod" ? [] : [Secret("Db:Password")], []) };

        var (exit, output) = await Run(s, "--missing", "--all", "--json");

        Assert.Equal(1, exit);
        Assert.DoesNotContain(Canary, output);
        var first = JsonDocument.Parse(output).RootElement[0];
        Assert.Equal("Db:Password", first.GetProperty("key").GetString());
        Assert.Equal("dev", first.GetProperty("presentIn")[0].GetString());
        Assert.Equal("prod", first.GetProperty("missingIn")[0].GetString());
    }

    [Theory]
    [InlineData("--missing")]
    [InlineData("--all")]
    [InlineData("--missing --all prod")]
    public async Task Missing_and_all_go_together_and_take_no_sides(string args)
    {
        await Assert.ThrowsAsync<CommandRuntimeException>(() => Run(new Stubs(), args.Split(' ')));
    }
}
