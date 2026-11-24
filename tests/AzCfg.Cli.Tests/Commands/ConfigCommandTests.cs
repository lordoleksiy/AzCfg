using AzCfg.Cli.Commands;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure.InMemory;
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

    private sealed class InMemoryState(UserProfile? profile, ProjectBinding? binding = null) : IUserStateStore
    {
        public UserProfile? Profile { get; private set; } = profile;

        public ProjectBinding? Binding { get; private set; } = binding;

        public int SnapshotDeletes { get; private set; }

        public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult(Profile);

        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct)
        {
            Profile = profile;
            return Task.CompletedTask;
        }

        public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => Task.FromResult(Binding);

        public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct)
        {
            Binding = binding;
            return Task.CompletedTask;
        }

        public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSnapshotsAsync(string repoPath, CancellationToken ct)
        {
            SnapshotDeletes++;
            return Task.CompletedTask;
        }

        public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static readonly string Repo = Path.GetFullPath("/repo");
    private static readonly string Csproj = Path.Combine(Repo, "Orders", "Orders.csproj");

    private static readonly ProjectBinding Bound = new(
        Repo,
        "orders",
        new(LocalTargetKind.AppSettingsLocal, Csproj, LocalFileFormat.NestedJson),
        new(LocalTargetKind.UserSecrets, Csproj, LocalFileFormat.NestedJson),
        new Dictionary<string, Uri>(),
        "dev");

    private sealed class OneProject : IProjectDetector
    {
        public Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProjectInfo>>([new(Repo, Csproj, "Orders", false, null)]);
    }

    private sealed class SafeTarget(LocalTargetKind kind) : ILocalTarget
    {
        public LocalTargetKind Kind => kind;
        public bool CanHandle(ProjectInfo project) => true;
        public Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct) => Task.FromResult(new LocalTargetOption(target, true, true));
        public Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct) => throw new NotSupportedException();
        public Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct) => Task.CompletedTask;
    }

    private static Task<(int Exit, string Output, InMemoryState State)> Run(params string[] args) => RunBound(null, args);

    private static Task<(int Exit, string Output, InMemoryState State)> RunBound(ProjectBinding? binding, params string[] args) =>
        RunShared(new InMemoryProjectManifestStore(), binding, args);

    private static async Task<(int Exit, string Output, InMemoryState State)> RunShared(
        InMemoryProjectManifestStore manifests, ProjectBinding? binding, params string[] args)
    {
        var state = new InMemoryState(Profile, binding);
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), ColorSystem = ColorSystemSupport.NoColors, Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var services = new ServiceCollection()
            .AddSingleton<IUserStateStore>(state)
            .AddSingleton<IProjectDetector>(new OneProject())
            .AddSingleton<ILocalTarget>(new SafeTarget(LocalTargetKind.UserSecrets))
            .AddSingleton<ILocalTarget>(new SafeTarget(LocalTargetKind.AppSettingsLocal))
            .AddSingleton<IUserSettingsService, UserSettingsService>()
            .AddSingleton<IProjectManifestStore>(manifests)
            .AddSingleton<IProjectSettingsService, ProjectSettingsService>();
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
        var exit = await app.RunAsync(["config", .. args, "--project", Repo]);
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
            """[{"key":"mode","value":"labels"},{"key":"defaultLabel","value":null},{"key":"gitignoreLocalFiles","value":"false"},{"key":"env.dev.endpoint","value":"https://shared.azconfig.io"},{"key":"env.dev.label","value":"Development"},{"key":"env.dev.protected","value":"false"},{"key":"env.prod.endpoint","value":"https://shared.azconfig.io"},{"key":"env.prod.label","value":"Production"},{"key":"env.prod.protected","value":"true"}]""",
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

    [Fact]
    public async Task List_InsideBoundProject_PrintsUserKeysThenProjectKeys()
    {
        var (exit, output, _) = await RunBound(Bound, "list");

        Assert.Equal(0, exit);
        Assert.True(output.IndexOf("env.prod.protected", StringComparison.Ordinal) < output.IndexOf("project.service", StringComparison.Ordinal));
        Assert.Matches(@"project\.service\s.*orders", output);
        Assert.Matches(@"project\.active\s.*dev", output);
        Assert.Matches(@"project\.vault\.prod\s.*\(none\)", output);
    }

    [Fact]
    public async Task List_Json_InsideBoundProject_KeepsTheSettingShape()
    {
        var (_, output, _) = await RunBound(Bound, "list", "--json");

        using var json = System.Text.Json.JsonDocument.Parse(output);
        var items = json.RootElement.EnumerateArray().ToList();
        Assert.All(items, i => Assert.Equal(["key", "value"], i.EnumerateObject().Select(p => p.Name)));
        Assert.Equal("orders", items.Single(i => i.GetProperty("key").GetString() == "project.service").GetProperty("value").GetString());
        Assert.Equal("mode", items[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task Get_ProjectKey_ShowsIt()
    {
        var (_, output, _) = await RunBound(Bound, "get", "project.service", "--json");

        Assert.Equal("""{"key":"project.service","value":"orders"}""", output.Trim());
    }

    [Fact]
    public async Task Set_Service_SavesAndPrintsTheFollowUpHint()
    {
        var (exit, output, state) = await RunBound(Bound, "set", "project.service", "billing");

        Assert.Equal(0, exit);
        Assert.Contains("project.service = billing", output);
        Assert.Contains("azcfg use", output);
        Assert.Equal(("billing", null), (state.Binding!.Service, state.Binding.ActiveEnvironment));
        Assert.Equal(1, state.SnapshotDeletes);
    }

    [Fact]
    public async Task Set_Secrets_PrintsPullForceHint_ButJsonIsTheSettingAlone()
    {
        var (_, human, _) = await RunBound(Bound, "set", "project.secrets", "appsettings-local");
        var (_, json, state) = await RunBound(Bound, "set", "project.secrets", "appsettings-local", "--json");

        Assert.Contains("azcfg pull --force", human);
        Assert.Equal($$"""{"key":"project.secrets","value":{{System.Text.Json.JsonSerializer.Serialize("appsettings-local:" + Csproj)}}}""", json.Trim());
        Assert.Equal(LocalTargetKind.AppSettingsLocal, state.Binding!.SecretsTarget.Kind);
    }

    [Fact]
    public async Task Set_ProjectKey_WithoutBinding_AsksForPull()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run("set", "project.service", "x"));

        Assert.Contains("azcfg pull", ex.Message);
    }

    [Fact]
    public async Task Set_ReadOnlyProjectKey_IsAnError()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => RunBound(Bound, "set", "project.active", "prod"));

        Assert.Contains("azcfg use", ex.Message);
    }

    private static readonly string ManifestPath = Path.Combine(Repo, IProjectManifestStore.FileName);

    [Fact]
    public async Task List_ShowsProjectSharedFalseThenTrueWithPath()
    {
        var manifests = new InMemoryProjectManifestStore();
        var (_, before, _) = await RunShared(manifests, Bound, "list");
        Assert.Matches(@"project\.shared\s.*false", before);

        await RunShared(manifests, Bound, "set", "project.shared", "true");
        var (_, after, _) = await RunShared(manifests, Bound, "list");
        var (_, json, _) = await RunShared(manifests, Bound, "get", "project.shared", "--json");

        Assert.Contains($"true ({ManifestPath})", after);
        Assert.Equal($$"""{"key":"project.shared","value":"true","source":{{System.Text.Json.JsonSerializer.Serialize(ManifestPath)}}}""", json.Trim());
    }

    [Fact]
    public async Task SetShared_WritesManifestAndPrintsHintWithPath_JsonCarriesSourceAndHint()
    {
        var manifests = new InMemoryProjectManifestStore();

        var (exit, human, _) = await RunShared(manifests, Bound, "set", "project.shared", "true");
        var (_, json, _) = await RunShared(manifests, Bound, "set", "project.shared", "true", "--json");

        Assert.Equal(0, exit);
        Assert.Contains("project.shared = true", human);
        Assert.Contains($"{ProjectSettingKeys.SharedHint} ({ManifestPath})", human);
        Assert.Contains("\"service\": \"orders\"", manifests.Json(ManifestPath));
        var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;
        Assert.Equal((ManifestPath, ProjectSettingKeys.SharedHint), (parsed.GetProperty("source").GetString(), parsed.GetProperty("hint").GetString()));
    }

    [Fact]
    public async Task SetService_InSharedProject_UpdatesManifestAndSaysCommitIt()
    {
        var manifests = new InMemoryProjectManifestStore();
        await RunShared(manifests, Bound, "set", "project.shared", "true");

        var (_, output, state) = await RunShared(manifests, Bound, "set", "project.service", "billing");

        Assert.Contains("azcfg use", output);
        Assert.Contains(ProjectSettingKeys.SharedHint, output);
        Assert.Contains("\"service\": \"billing\"", manifests.Json(ManifestPath));
        Assert.Equal("billing", state.Binding!.Service);
    }
}
