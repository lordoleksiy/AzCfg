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

public sealed class PullCommandTests
{
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/r/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Unsafe = new(LocalTargetKind.AppSettingsLocal, "/r/Orders.csproj", LocalFileFormat.NestedJson);

    private sealed class Stubs
    {
        public UserProfile? Profile { get; init; } = new([Env("dev"), Env("stage")]);
        public ProjectResolution Resolution { get; init; } = new(["orders"], true, [new(Unsafe, true, false), new(Secrets, true, true)], null);
        public PullResult Result { get; init; } = new("dev", 3, 1, false, []);
        public ProjectBinding? Bound { get; set; }
        public (ProjectBinding Binding, ConfigEnvironment Env, bool Force)? Pulled { get; set; }
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
        public Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct) => Task.FromResult(s.Resolution);

        public Task<ProjectBinding> BindAsync(string repoPath, string service, LocalTargetBinding valuesTarget, LocalTargetBinding secretsTarget, CancellationToken ct) =>
            Task.FromResult(s.Bound = new(repoPath, service, valuesTarget, secretsTarget, new Dictionary<string, Uri>()));

        public Task<ProjectBinding> AddIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();

        public Task<ProjectBinding> RemoveIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubSync(Stubs s) : ISyncService
    {
        public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct)
        {
            s.Pulled = (binding, env, force);
            return Task.FromResult(s.Result);
        }

        public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct) => throw new NotSupportedException();
        public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct) => throw new NotSupportedException();
    }

    private static ConfigEnvironment Env(string name) => new(name, new Uri($"https://{name}.azconfig.io"), null, false);

    private static ProjectBinding Binding(string? active = null) =>
        new("/r", "orders", Unsafe, Secrets, new Dictionary<string, Uri>(), active);

    private static Stubs Bound(PullResult? result = null) =>
        new() { Resolution = new([], true, [], Binding()), Result = result ?? new("dev", 3, 1, false, []) };

    private static async Task<(int Exit, string Output)> Run(Stubs s, params string[] args) => await Run("pull", s, args);

    private static async Task<(int Exit, string Output)> Run(string command, Stubs s, params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Out = new AnsiConsoleOutput(writer) });
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
            c.AddCommand<PullCommand>("pull");
            c.AddCommand<UseCommand>("use");
        });
        var exit = await app.RunAsync([command, .. args]);
        return (exit, writer.ToString());
    }

    public static TheoryData<string[], string?, string> EnvSelection => new()
    {
        { ["--env", "stage"], "dev", "stage" },
        { ["--env", "STAGE"], null, "stage" },
        { [], "stage", "stage" },
        { [], null, "dev" },
    };

    [Theory]
    [MemberData(nameof(EnvSelection))]
    public async Task Env_is_option_then_active_then_dev(string[] args, string? active, string expected)
    {
        var s = new Stubs { Resolution = new([], true, [], Binding(active)) };

        await Run(s, args);

        Assert.Equal(expected, s.Pulled!.Value.Env.Name);
    }

    [Theory]
    [InlineData(true, "azcfg init")]
    [InlineData(false, "nope")]
    public async Task Missing_profile_or_unknown_env_is_an_error(bool noProfile, string expected)
    {
        var s = new Stubs { Profile = noProfile ? null : new([Env("dev")]), Resolution = new([], true, [], Binding()) };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(s, "--env", "nope"));

        Assert.Contains(expected, ex.Message);
        Assert.Null(s.Pulled);
    }

    [Fact]
    public async Task Existing_binding_is_used_without_binding_again()
    {
        var s = Bound();

        await Run(s);

        Assert.Null(s.Bound);
        Assert.Equal("orders", s.Pulled!.Value.Binding.Service);
    }

    [Fact]
    public async Task Confident_match_binds_first_safe_target_without_prompting()
    {
        var s = new Stubs();

        await Run(s, "--json");

        Assert.Equal(("orders", Secrets, Secrets), (s.Bound!.Service, s.Bound.ValuesTarget, s.Bound.SecretsTarget));
        Assert.Same(s.Bound, s.Pulled!.Value.Binding);
    }

    public static TheoryData<ProjectResolution> Unresolvable => new()
    {
        new(["orders", "payments"], false, [new(Secrets, true, true)], null),
        new([], false, [], null),
        new(["orders"], true, [new(Unsafe, true, false)], null),
    };

    [Theory]
    [MemberData(nameof(Unresolvable))]
    public async Task Unresolvable_project_without_prompting_is_an_error(ProjectResolution resolution)
    {
        var s = new Stubs { Resolution = resolution };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(s, "--json"));

        Assert.Null(s.Pulled);
    }

    public static TheoryData<ProjectResolution, string> ServiceOption => new()
    {
        { new([], false, [new(Unsafe, true, false), new(Secrets, true, true)], null), "billing" },
        { new(["orders", "payments"], false, [new(Secrets, true, true)], null), "payments" },
        { new(["orders"], true, [new(Secrets, true, true)], null), "billing" },
    };

    [Theory]
    [MemberData(nameof(ServiceOption))]
    public async Task Service_option_binds_an_unbound_project_without_detection(ProjectResolution resolution, string service)
    {
        var s = new Stubs { Resolution = resolution };

        await Run(s, "--service", service, "--json");

        Assert.Equal((service, Secrets, Secrets), (s.Bound!.Service, s.Bound.ValuesTarget, s.Bound.SecretsTarget));
        Assert.Same(s.Bound, s.Pulled!.Value.Binding);
    }

    [Fact]
    public async Task Service_option_does_not_rebind_a_bound_project()
    {
        var s = Bound();

        await Run(s, "--service", "billing");

        Assert.Null(s.Bound);
        Assert.Equal("orders", s.Pulled!.Value.Binding.Service);
    }

    [Fact]
    public async Task Use_accepts_the_service_option()
    {
        var s = new Stubs { Resolution = new([], false, [new(Secrets, true, true)], null) };

        await Run("use", s, "stage", "--service", "billing", "--yes");

        Assert.Equal("billing", s.Bound!.Service);
        Assert.Equal("stage", s.Pulled!.Value.Env.Name);
    }

    public static TheoryData<ProjectResolution, string[], string> UnresolvableService => new()
    {
        { new([], false, [new(Secrets, true, true)], null), ["--json"], "No service keys were found in the App Configuration store for this project; pass --service <name>." },
        { new([], false, [new(Secrets, true, true)], null), [], "No service keys were found in the App Configuration store for this project; pass --service <name>." },
        { new(["orders", "payments"], false, [new(Secrets, true, true)], null), ["--yes"], "Several services match this project (orders, payments); pass --service <name> or run interactively." },
        { new(["billing"], false, [new(Secrets, true, true)], null), ["--json"], "The only service in the App Configuration store (billing) does not match this project; pass --service <name> or run interactively." },
    };

    [Theory]
    [MemberData(nameof(UnresolvableService))]
    public async Task Unresolvable_service_names_the_service_option(ProjectResolution resolution, string[] args, string expected)
    {
        var s = new Stubs { Resolution = resolution };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(s, args));

        Assert.Equal(expected, ex.Message);
        Assert.Null(s.Pulled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Force_is_passed_through(bool force)
    {
        var s = Bound();

        await Run(s, force ? ["--force"] : []);

        Assert.Equal(force, s.Pulled!.Value.Force);
    }

    [Fact]
    public async Task Human_output_shows_environment_and_counts()
    {
        var (exit, output) = await Run(Bound());

        Assert.Equal(0, exit);
        Assert.Contains("dev", output);
        Assert.Matches(@"3\s.*1", output);
    }

    [Fact]
    public async Task Blocked_pull_lists_keys_hints_force_and_fails()
    {
        var (exit, output) = await Run(Bound(new("dev", 0, 0, true, ["Db:Host", "Api:Key"])));

        Assert.Equal(1, exit);
        Assert.Contains("Db:Host", output);
        Assert.Contains("Api:Key", output);
        Assert.Contains("--force", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Human_output_shows_warnings_and_ignored_keys(bool blocked)
    {
        var result = new PullResult("dev", 3, 1, blocked, blocked ? ["Db:Host"] : [])
        {
            Warnings = [new(SyncWarningKind.DuplicateLabels, "Api:Url", "'Api:Url' has [a] and [b]")],
            Ignored = ["Logging:Level", "Serilog:Url"],
        };

        var (exit, output) = await Run(Bound(result));

        Assert.Equal(blocked ? 1 : 0, exit);
        Assert.Contains("Warning: 'Api:Url' has [a] and [b]", output);
        Assert.Contains("2 ignored key(s) kept local: Logging:Level, Serilog:Url", output);
        Assert.Equal(blocked, output.Contains("--force"));
    }

    [Fact]
    public async Task Human_output_without_warnings_or_ignored_keys_shows_neither()
    {
        var (_, output) = await Run(Bound());

        Assert.DoesNotContain("Warning:", output);
        Assert.DoesNotContain("ignored", output);
    }

    [Fact]
    public async Task Json_output_carries_warnings_and_ignored_keys()
    {
        var result = new PullResult("dev", 3, 1, false, [])
        {
            Warnings = [new(SyncWarningKind.DuplicateLabels, "Api:Url", "dup")],
            Ignored = ["Logging:Level"],
        };

        var (_, output) = await Run(Bound(result), "--json");

        Assert.Equal(CliJson.Serialize(result), output.Trim());
        Assert.Contains("\"warnings\"", output);
        Assert.Contains("\"ignored\":[\"Logging:Level\"]", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Json_output_is_the_serialized_PullResult(bool blocked)
    {
        var result = new PullResult("dev", blocked ? 0 : 3, 0, blocked, blocked ? ["Db:Host"] : []);

        var (exit, output) = await Run(Bound(result), "--json");

        Assert.Equal(blocked ? 1 : 0, exit);
        Assert.Equal(CliJson.Serialize(result), output.Trim());
    }

    public static TheoryData<string[], string?, string> UseSelection => new()
    {
        { ["stage"], "dev", "stage" },
        { ["STAGE"], null, "stage" },
        { ["stage", "--env", "dev"], null, "stage" },
    };

    [Theory]
    [MemberData(nameof(UseSelection))]
    public async Task Use_pulls_the_given_environment(string[] args, string? active, string expected)
    {
        var s = new Stubs { Resolution = new([], true, [], Binding(active)) };

        await Run("use", s, args);

        Assert.Equal(expected, s.Pulled!.Value.Env.Name);
    }

    [Fact]
    public async Task Use_unknown_env_is_an_error_and_pulls_nothing()
    {
        var s = Bound();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run("use", s, "nope"));

        Assert.Null(s.Pulled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Use_passes_force_and_fails_when_blocked(bool force)
    {
        var s = Bound(new("stage", 0, 0, !force, ["Db:Host"]));

        var (exit, output) = await Run("use", s, force ? ["stage", "--force"] : ["stage"]);

        Assert.Equal(force, s.Pulled!.Value.Force);
        Assert.Equal(force ? 0 : 1, exit);
        Assert.Equal(!force, output.Contains("Db:Host"));
    }

    [Fact]
    public async Task Use_json_output_is_the_serialized_PullResult()
    {
        var result = new PullResult("stage", 2, 1, false, []);

        var (_, output) = await Run("use", Bound(result), "stage", "--json");

        Assert.Equal(CliJson.Serialize(result), output.Trim());
    }
}
