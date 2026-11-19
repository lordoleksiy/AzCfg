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
using Spectre.Console.Rendering;

namespace AzCfg.Cli.Tests;

public sealed class PlanPushCommandTests
{
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/r/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly Uri VaultUri = new("https://kv-orders.vault.azure.net");

    private sealed class Stubs
    {
        public UserProfile? Profile { get; init; } = new([Env("dev"), Env("prod", true)]);
        public ProjectBinding? Existing { get; init; } = Binding();
        public Func<ProjectBinding, SyncPlan> PlanFor { get; init; } = b => Plan(b);
        public ApplyResult Result { get; init; } = new(2, []);
        public List<(ProjectBinding Binding, ConfigEnvironment Env, PushSource Source)> Planned { get; } = [];
        public List<ProjectBinding> SavedBindings { get; } = [];
        public List<(SyncPlan Plan, ApplyOptions Options)> Applied { get; } = [];
    }

    private sealed class StubState(Stubs s) : IUserStateStore
    {
        public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult(s.Profile);
        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct) { s.SavedBindings.Add(binding); return Task.CompletedTask; }
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
        public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct)
        {
            s.Planned.Add((binding, env, source));
            return Task.FromResult(s.PlanFor(binding) with { Environment = env });
        }

        public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct)
        {
            s.Applied.Add((plan, options));
            return Task.FromResult(s.Result);
        }

        public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<SyncSummary> StatusAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteConfig> ReadRemoteConfigAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) => throw new NotSupportedException();
        public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictResolution resolution, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubDiscovery : IResourceDiscovery
    {
        public Task<IReadOnlyList<AzureResource>> FindAppConfigStoresAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<AzureResource>> FindKeyVaultsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AzureResource>>([new("/kv", "kv-orders", VaultUri, "s", "rg", new Dictionary<string, string>())]);
    }

    private sealed class ScriptedInput(string text) : IAnsiConsoleInput
    {
        private readonly Queue<char> _keys = new(text);

        public bool IsKeyAvailable() => _keys.Count > 0;

        public ConsoleKeyInfo? ReadKey(bool intercept) => _keys.TryDequeue(out var c)
            ? c == '\n' ? new('\r', ConsoleKey.Enter, false, false, false) : new(c, default, false, false, false)
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

    private static ConfigEnvironment Env(string name, bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);

    private static ProjectBinding Binding(string? active = null) => new("/r", "orders", Target, Target, new Dictionary<string, Uri>(), active);

    private static ConfigEntry Entry(string key, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain("v"));

    private static SyncPlan Plan(ProjectBinding binding, IReadOnlyList<Change>? changes = null, IReadOnlyList<Conflict>? conflicts = null, bool vault = false, bool global = false) =>
        new(binding, Env("dev"), changes ?? [new(ChangeKind.Add, null, Entry("Db:Host"))], conflicts ?? [], vault, global);

    private static async Task<(int Exit, string Output)> Run(string command, Stubs s, string input, params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.NoColors, Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var services = new ServiceCollection()
            .AddSingleton<IUserStateStore>(new StubState(s))
            .AddSingleton<IProjectService>(new StubProjects(s))
            .AddSingleton<ISyncService>(new StubSync(s))
            .AddSingleton<IResourceDiscovery>(new StubDiscovery())
            .AddSingleton<IEnvironmentResolver, SeparateStoresResolver>();
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.ConfigureConsole(new ScriptedConsole(console, new ScriptedInput(input)));
            c.AddCommand<PlanCommand>("plan");
            c.AddCommand<PushCommand>("push");
        });
        var exit = await app.RunAsync([command, .. args]);
        return (exit, writer.ToString());
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("push")]
    public async Task Unbound_project_or_missing_profile_or_unknown_env_is_an_error(string command)
    {
        var unbound = new Stubs { Existing = null };
        var noProfile = new Stubs { Profile = null };
        var unknown = new Stubs();

        Assert.Contains("azcfg pull", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(command, unbound, ""))).Message);
        Assert.Contains("azcfg init", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(command, noProfile, ""))).Message);
        Assert.Contains("nope", (await Assert.ThrowsAsync<InvalidOperationException>(() => Run(command, unknown, "", "--env", "nope"))).Message);
        Assert.All([unbound, noProfile, unknown], s => Assert.Empty(s.Planned));
    }

    public static TheoryData<string, string[], string?, string> EnvSelection => new()
    {
        { "plan", ["--env", "prod"], "dev", "prod" },
        { "plan", [], "prod", "prod" },
        { "plan", [], null, "dev" },
        { "push", ["--env", "PROD", "--yes"], "dev", "prod" },
        { "push", ["--yes"], null, "dev" },
    };

    [Theory]
    [MemberData(nameof(EnvSelection))]
    public async Task Env_is_option_then_active_then_dev_and_source_is_local_files(string command, string[] args, string? active, string expected)
    {
        var s = new Stubs { Existing = Binding(active), PlanFor = b => Plan(b, []) };

        await Run(command, s, "", args);

        var planned = Assert.Single(s.Planned);
        Assert.Equal(expected, planned.Env.Name);
        Assert.IsType<PushSource.LocalFiles>(planned.Source);
    }

    [Fact]
    public async Task Plan_lists_kind_scope_and_key_and_conflicts_but_no_secret_value()
    {
        var secret = new Change(ChangeKind.CreateSecret, null, Entry("Api:Key"), "orders--Api--Key", new SecretString("s3cret"));
        var s = new Stubs
        {
            PlanFor = b => Plan(b, [new(ChangeKind.Add, null, Entry("Db:Host")), new(ChangeKind.Delete, Entry("Old:Key", ConfigScope.Global), null), secret],
                [new("Conflict:Key", null, new("Conflict:Key", ConfigScope.Service, new StateValue.Plain("a")), null)]),
        };

        var (exit, output) = await Run("plan", s, "");

        Assert.Equal(0, exit);
        Assert.Matches(@"Add[\s│]+Service[\s│]+Db:Host", output);
        Assert.Matches(@"Delete[\s│]+Global[\s│]+Old:Key", output);
        Assert.Matches(@"CreateSecret[\s│]+Service[\s│]+Api:Key[\s│]+\(none\)[\s│]+orders--Api--Key", output);
        Assert.Contains("Conflict:Key", output);
        Assert.DoesNotContain("s3cret", output);
    }

    public static TheoryData<string, string[], bool> DeleteOption => new()
    {
        { "plan", [], false },
        { "plan", ["--delete"], true },
        { "push", ["--yes"], false },
        { "push", ["--yes", "--delete"], true },
    };

    [Theory]
    [MemberData(nameof(DeleteOption))]
    public async Task Delete_option_is_passed_to_the_local_files_source(string command, string[] args, bool delete)
    {
        var s = new Stubs();

        await Run(command, s, "", args);

        Assert.Equal(new PushSource.LocalFiles(delete), Assert.Single(s.Planned).Source);
    }

    [Fact]
    public async Task Plan_shows_the_label_of_each_change()
    {
        var labeled = Entry("Api:Url") with { Label = "Development" };
        var s = new Stubs
        {
            PlanFor = b => Plan(b, [new(ChangeKind.Add, null, Entry("Db:Host")), new(ChangeKind.Update, labeled, labeled), new(ChangeKind.Delete, Entry("Old:Key") with { Label = "[x]" }, null)]),
        };

        var (_, output) = await Run("plan", s, "");

        Assert.Matches(@"Label", output);
        Assert.Matches(@"Add[\s│]+Service[\s│]+Db:Host[\s│]+\(none\)", output);
        Assert.Matches(@"Update[\s│]+Service[\s│]+Api:Url[\s│]+Development", output);
        Assert.Matches(@"Delete[\s│]+Service[\s│]+Old:Key[\s│]+\[x\]", output);
    }

    private static readonly SyncWarning Skipped = new(SyncWarningKind.DeleteSkipped, "Old:Key", "'Old:Key' [kept] (use --delete).");

    [Theory]
    [InlineData("plan")]
    [InlineData("push")]
    public async Task Warnings_follow_the_tables(string command)
    {
        var s = new Stubs { PlanFor = b => Plan(b) with { Warnings = [Skipped] } };

        var (_, output) = await Run(command, s, "", "--yes");

        Assert.Contains("Warning: 'Old:Key' [kept] (use --delete).", output);
        Assert.True(output.IndexOf("Db:Host", StringComparison.Ordinal) < output.IndexOf("Warning:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("push")]
    public async Task Warnings_are_shown_when_there_are_no_changes(string command)
    {
        var s = new Stubs { PlanFor = b => Plan(b, []) with { Warnings = [Skipped] } };

        var (exit, output) = await Run(command, s, "", "--yes");

        Assert.Equal(0, exit);
        var none = output.IndexOf("No changes.", StringComparison.Ordinal);
        Assert.True(none >= 0 && none < output.IndexOf("Warning: 'Old:Key' [kept] (use --delete).", StringComparison.Ordinal), output);
        Assert.Empty(s.Applied);
    }

    [Fact]
    public async Task Refused_push_shows_warnings()
    {
        var s = new Stubs
        {
            PlanFor = b => Plan(b, conflicts: [new("Db:Host", null, null, new("Db:Host", ConfigScope.Service, new StateValue.Plain("r")))]) with { Warnings = [Skipped] },
        };

        var (exit, output) = await Run("push", s, "", "--yes");

        Assert.Equal(1, exit);
        Assert.Contains("Warning: 'Old:Key' [kept] (use --delete).", output);
    }

    [Fact]
    public async Task Plan_json_carries_the_warnings_as_is()
    {
        var plan = Plan(Binding(), []) with { Warnings = [Skipped] };
        var s = new Stubs { PlanFor = _ => plan };

        var (_, output) = await Run("plan", s, "", "--json");

        Assert.Equal(CliJson.Serialize(plan), output.Trim());
        Assert.Contains("deleteSkipped", output);
        Assert.DoesNotContain("No changes", output);
    }

    [Fact]
    public async Task Plan_json_is_the_serialized_plan_with_redacted_secrets()
    {
        var plan = Plan(Binding(), [new(ChangeKind.CreateSecret, null, Entry("Api:Key"), "orders--Api--Key", new SecretString("s3cret"))]);
        var s = new Stubs { PlanFor = _ => plan };

        var (exit, output) = await Run("plan", s, "", "--json");

        Assert.Equal(0, exit);
        Assert.Equal(CliJson.Serialize(plan), output.Trim());
        Assert.Contains("***", output);
        Assert.DoesNotContain("s3cret", output);
    }

    [Fact]
    public async Task Empty_plan_reports_no_changes_and_push_applies_nothing()
    {
        var s = new Stubs { PlanFor = b => Plan(b, []) };

        var plan = await Run("plan", s, "");
        var push = await Run("push", s, "", "--yes");

        Assert.All([plan, push], r => Assert.Equal(0, r.Exit));
        Assert.All([plan, push], r => Assert.Contains("No changes", r.Output));
        Assert.Empty(s.Applied);
    }

    [Fact]
    public async Task Push_with_conflicts_refuses_without_applying()
    {
        var s = new Stubs { PlanFor = b => Plan(b, conflicts: [new("Db:Host", null, null, new("Db:Host", ConfigScope.Service, new StateValue.Plain("r")))]) };

        var (exit, output) = await Run("push", s, "", "--yes");

        Assert.Equal(1, exit);
        Assert.Contains("Db:Host", output);
        Assert.Empty(s.Applied);
    }

    [Fact]
    public async Task Push_with_yes_applies_without_confirmation_and_prints_the_count()
    {
        var s = new Stubs();

        var (exit, output) = await Run("push", s, "", "--yes");

        Assert.Equal(0, exit);
        Assert.Equal(new ApplyOptions(), Assert.Single(s.Applied).Options);
        Assert.Matches(@"Applied\s+2", output);
    }

    [Theory]
    [InlineData("y\n", true)]
    [InlineData("n\n", false)]
    public async Task Push_asks_for_confirmation_unless_yes(string input, bool applied)
    {
        var s = new Stubs();

        var (exit, _) = await Run("push", s, input);

        Assert.Equal(applied ? 0 : 1, exit);
        Assert.Equal(applied ? 1 : 0, s.Applied.Count);
    }

    [Fact]
    public async Task Push_json_without_yes_is_an_error()
    {
        var s = new Stubs();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run("push", s, "", "--json"));

        Assert.Empty(s.Applied);
    }

    [Fact]
    public async Task Push_json_prints_the_ApplyResult()
    {
        var s = new Stubs { Result = new(1, ["Db:Host"]) };

        var (exit, output) = await Run("push", s, "", "--json", "--yes");

        Assert.Equal(1, exit);
        Assert.Equal(CliJson.Serialize(s.Result), output.Trim());
    }

    [Fact]
    public async Task Push_with_failed_keys_lists_them_and_fails()
    {
        var s = new Stubs { Result = new(1, ["Db:Host"]) };

        var (exit, output) = await Run("push", s, "", "--yes");

        Assert.Equal(1, exit);
        Assert.Contains("Db:Host", output);
    }

    public static TheoryData<string, string[], string?> ProtectedConfirmation => new()
    {
        { "prod\n", ["--yes"], "prod" },
        { "", ["--confirm", "prod"], "prod" },
        { "", ["--confirm", "prod", "--json"], "prod" },
    };

    [Theory]
    [MemberData(nameof(ProtectedConfirmation))]
    public async Task Protected_environment_needs_the_name_even_with_yes(string input, string[] args, string? confirmed)
    {
        var s = new Stubs();

        await Run("push", s, input, ["--env", "prod", .. args]);

        Assert.Equal(confirmed, Assert.Single(s.Applied).Options.ConfirmedEnvironment);
    }

    [Fact]
    public async Task Protected_environment_in_json_without_confirm_is_an_error()
    {
        var s = new Stubs();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run("push", s, "", "--env", "prod", "--json", "--yes"));

        Assert.Contains("--confirm", ex.Message);
        Assert.Empty(s.Applied);
    }

    public static TheoryData<string, string[], bool, bool> GlobalConfirmation => new()
    {
        { "", ["--yes"], true, true },
        { "y\ny\n", [], true, true },
        { "y\nn\n", [], false, false },
    };

    [Theory]
    [MemberData(nameof(GlobalConfirmation))]
    public async Task Global_changes_need_their_own_confirmation(string input, string[] args, bool applied, bool confirmed)
    {
        var s = new Stubs { PlanFor = b => Plan(b, global: true) };

        var (exit, _) = await Run("push", s, input, args);

        Assert.Equal(applied ? 0 : 1, exit);
        Assert.Equal(applied, s.Applied.Count == 1);
        Assert.All(s.Applied, a => Assert.Equal(confirmed, a.Options.ConfirmGlobal));
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--yes")]
    public async Task Vault_choice_without_prompting_is_an_error(string flag)
    {
        var s = new Stubs { PlanFor = b => Plan(b, vault: true) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run("push", s, "", flag, "--yes"));

        Assert.Empty(s.Applied);
        Assert.Empty(s.SavedBindings);
    }

    [Fact]
    public async Task Vault_choice_is_saved_in_the_binding_and_the_plan_is_recomputed()
    {
        var s = new Stubs { PlanFor = b => Plan(b, vault: b.VaultByEnvironment.Count == 0) };

        var (exit, _) = await Run("push", s, "\ny\n");

        Assert.Equal(0, exit);
        Assert.Equal(VaultUri, Assert.Single(s.SavedBindings).VaultByEnvironment["dev"]);
        Assert.Equal(2, s.Planned.Count);
        Assert.Equal(VaultUri, s.Planned[1].Binding.VaultByEnvironment["dev"]);
        Assert.False(Assert.Single(s.Applied).Plan.RequiresVaultChoice);
    }
}
