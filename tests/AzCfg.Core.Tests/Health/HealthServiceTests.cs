using System.Text.Json;
using System.Text.Json.Serialization;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Health;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.Health;

public sealed class HealthServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri DevSecret = new("https://kv-dev.vault.azure.net/secrets/Db--Password");
    private static readonly Uri ProdSecret = new("https://kv-prod.vault.azure.net/secrets/Db--Password");
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Values, Secrets, new Dictionary<string, Uri>());

    private sealed class Rig
    {
        public Rig(ProjectBinding? binding, Dictionary<string, ConfigEntry[]>? entries = null, params FakeLocalTarget[] targets)
        {
            State = new() { Profile = new([Env("dev"), Env("prod", true)]), Binding = binding };
            entries ??= new() { ["dev"] = [], ["prod"] = [] };
            Prefixes = entries.Keys.ToDictionary(k => k, _ => new[] { "orders" });
            Config = new(Prefixes, entries);
            FakeLocalTarget[] all = targets.Length > 0 ? targets : [new(LocalTargetKind.UserSecrets), new(LocalTargetKind.AppSettingsLocal)];
            Sync = new SyncService(Config, KeyVault, State, new HmacSecretFingerprinter(new byte[32]), all);
            Service = new HealthService(
                State,
                new SeparateStoresResolver(),
                Config,
                Sync,
                KeyVault,
                Probe,
                all,
                Detector,
                new FakeAccessErrorClassifier());
        }

        public FakeUserStateStore State { get; }
        public SyncService Sync { get; }
        public Dictionary<string, string[]> Prefixes { get; }
        public FakeConfigStore Config { get; }
        public ListDetector Detector { get; } = new([new("/repo", "/repo/Orders/Orders.csproj", "Orders", false, null)]);
        public FakeSecretStore KeyVault { get; } = new([]);
        public FakeAzureAccountProbe Probe { get; } = new();
        public HealthService Service { get; }

        public Task<HealthReport> CheckAsync() => Service.CheckAsync("/repo", Ct);

        public HealthService With(IConfigStore store) =>
            new(State, new SeparateStoresResolver(), store, Sync, KeyVault, Probe, [], Detector, new FakeAccessErrorClassifier());
    }

    private sealed class ListDetector(List<ProjectInfo> projects) : IProjectDetector
    {
        public List<ProjectInfo> Projects => projects;

        public bool Throws { get; set; }

        public Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct) =>
            Throws ? throw new IOException("boom, value=CANARY") : Task.FromResult<IReadOnlyList<ProjectInfo>>(projects);
    }

    private static ConfigEnvironment Env(string name, bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);

    private static ConfigEntry Plain(string key) => new(key, ConfigScope.Service, new ConfigValue.Plain("v"));

    private static ConfigEntry Ref(string key, Uri uri) => new(key, ConfigScope.Service, new ConfigValue.KeyVaultRef(uri));

    private static string Json(HealthReport report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } });

    [Fact]
    public async Task CheckAsync_WithoutProfile_Throws()
    {
        var rig = new Rig(Binding);
        rig.State.Profile = null;

        await Assert.ThrowsAsync<InvalidOperationException>(rig.CheckAsync);
    }

    [Fact]
    public async Task CheckAsync_Healthy_HasNoFindings()
    {
        var rig = new Rig(Binding, new() { ["dev"] = [Plain("A"), Ref("Db:Password", DevSecret)], ["prod"] = [Plain("A"), Ref("Db:Password", ProdSecret)] });

        var report = await rig.CheckAsync();

        Assert.True(report.Account.SignedIn);
        Assert.Equal("dev@contoso.com", report.Account.Account);
        Assert.Equal(["dev", "prod"], report.Environments.Select(e => e.Name));
        Assert.All(report.Environments, e => Assert.True(e.Reachable));
        Assert.Empty(report.Findings);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public async Task CheckAsync_NotSignedIn_ReportsSignInErrorAndSkipsRemoteChecks()
    {
        var rig = new Rig(Binding);
        rig.Probe.Result = new(false, null, "No credential");

        var report = await rig.CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(HealthCheckKind.AzureSignIn, finding.Kind);
        Assert.Equal(HealthSeverity.Error, finding.Severity);
        Assert.Equal("az login", finding.Fix);
        Assert.All(report.Environments, e => Assert.False(e.Reachable));
        Assert.Empty(rig.KeyVault.ExistsCalls);
        Assert.True(report.HasErrors);
    }

    [Fact]
    public async Task CheckAsync_ProbeThrows_IsNotSignedInWithoutMessage()
    {
        var rig = new Rig(Binding);
        rig.Probe.Throws = true;

        var report = await rig.CheckAsync();

        Assert.False(report.Account.SignedIn);
        Assert.DoesNotContain("CANARY", Json(report));
    }

    [Fact]
    public async Task CheckAsync_NoBinding_RunsOnlySignInAndEnvironmentAccess()
    {
        var rig = new Rig(null);
        rig.Config.Unreachable.Add("prod");

        var report = await rig.CheckAsync();

        Assert.True(report.Environments.Single(e => e.Name == "dev").Reachable);
        Assert.False(report.Environments.Single(e => e.Name == "prod").Reachable);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(HealthCheckKind.EnvironmentAccess, finding.Kind);
        Assert.Equal(HealthSeverity.Error, finding.Severity);
        Assert.Equal("prod", finding.Environment);
    }

    [Fact]
    public async Task CheckAsync_UnreachableEnvironment_IsExcludedFromSecretAndMissingKeyChecks()
    {
        var rig = new Rig(Binding, new() { ["dev"] = [Plain("A"), Ref("Db:Password", DevSecret)], ["prod"] = [] });
        rig.Config.Unreachable.Add("prod");

        var report = await rig.CheckAsync();

        Assert.Equal([HealthCheckKind.EnvironmentAccess], report.Findings.Select(f => f.Kind));
        Assert.Equal([DevSecret], rig.KeyVault.ExistsCalls);
    }

    [Fact]
    public async Task CheckAsync_UnsafeSecretsTarget_IsAnErrorWithFix()
    {
        var rig = new Rig(Binding with { SecretsTarget = Values }, null, new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, isSafeForSecrets: false), new FakeLocalTarget(LocalTargetKind.UserSecrets));

        var report = await rig.CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(HealthCheckKind.SecretsTargetNotIgnored, finding.Kind);
        Assert.Equal(HealthSeverity.Error, finding.Severity);
        Assert.Equal("azcfg config set gitignoreLocalFiles true", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_UnsafeValuesTarget_IsAWarningWithFix()
    {
        var rig = new Rig(Binding, null, new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, isSafeForSecrets: false), new FakeLocalTarget(LocalTargetKind.UserSecrets));

        var report = await rig.CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(HealthCheckKind.ValuesTargetNotIgnored, finding.Kind);
        Assert.Equal(HealthSeverity.Warning, finding.Severity);
        Assert.Equal("azcfg config set gitignoreLocalFiles true", finding.Fix);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public async Task CheckAsync_DeadSecretReference_IsAnError()
    {
        var rig = new Rig(Binding, new() { ["dev"] = [Ref("Db:Password", DevSecret), Plain("A")], ["prod"] = [Ref("Db:Password", ProdSecret), Plain("A")] });
        rig.KeyVault.Missing.Add(DevSecret);

        var report = await rig.CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(HealthCheckKind.DeadSecretReference, finding.Kind);
        Assert.Equal(HealthSeverity.Error, finding.Severity);
        Assert.Equal("dev", finding.Environment);
        Assert.Equal("Db:Password", finding.Key);
        Assert.Equal("azcfg edit --env dev", finding.Fix);
        Assert.True(report.HasErrors);
    }

    [Fact]
    public async Task CheckAsync_ExistsThrows_IsASecretNotVerifiedWarningWithoutMessage()
    {
        var rig = new Rig(Binding, new() { ["dev"] = [Ref("Db:Password", DevSecret)], ["prod"] = [Ref("Db:Password", ProdSecret)] });
        rig.KeyVault.ExistsThrows.Add(DevSecret);

        var report = await rig.CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(HealthCheckKind.SecretNotVerified, finding.Kind);
        Assert.Equal(HealthSeverity.Warning, finding.Severity);
        Assert.Equal("Db:Password", finding.Key);
        Assert.False(report.HasErrors);
        Assert.DoesNotContain("CANARY", Json(report));
    }

    [Fact]
    public async Task CheckAsync_DistinctSecretUris_AreCheckedOnce()
    {
        var rig = new Rig(Binding, new() { ["dev"] = [Ref("A", DevSecret), Ref("B", DevSecret)], ["prod"] = [Ref("A", DevSecret), Ref("B", DevSecret)] });

        await rig.CheckAsync();

        Assert.Equal([DevSecret], rig.KeyVault.ExistsCalls);
    }

    [Fact]
    public async Task CheckAsync_NotSignedInWithUnsafeSecretsTarget_StillReportsTargetFinding()
    {
        var rig = new Rig(Binding, null, new FakeLocalTarget(LocalTargetKind.UserSecrets, isSafeForSecrets: false), new FakeLocalTarget(LocalTargetKind.AppSettingsLocal));
        rig.Probe.Result = new(false, null, "No credential");

        var report = await rig.CheckAsync();

        Assert.Contains(report.Findings, f => f.Kind == HealthCheckKind.AzureSignIn);
        var finding = Assert.Single(report.Findings, f => f.Kind == HealthCheckKind.SecretsTargetNotIgnored);
        Assert.Equal(HealthSeverity.Error, finding.Severity);
        Assert.Contains("git rm --cached", finding.Message);
        Assert.Contains("azcfg pull", finding.Message);
    }

    [Fact]
    public async Task CheckAsync_NotSignedInWithMissingTargetProject_StillReportsBindingMismatch()
    {
        var rig = new Rig(Binding);
        rig.Probe.Result = new(false, null, "No credential");
        rig.Detector.Projects.Clear();

        var report = await rig.CheckAsync();

        Assert.Contains(report.Findings, f => f.Kind == HealthCheckKind.BindingMismatch && f.Severity == HealthSeverity.Error);
    }

    [Fact]
    public async Task CheckAsync_GlobalKeyMissingInOneEnvironment_FixIsGlobalEdit()
    {
        var rig = new Rig(Binding, new()
        {
            ["dev"] = [new ConfigEntry("G", ConfigScope.Global, new ConfigValue.Plain("v"))],
            ["prod"] = [],
        });

        var report = await rig.CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal("azcfg edit --env prod --global --set G=<VALUE>", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_KeyMissingInOneEnvironment_IsAWarningPerKey()
    {
        var rig = new Rig(Binding, new() { ["dev"] = [Plain("A"), Plain("OnlyDev")], ["prod"] = [Plain("A")] });

        var report = await rig.CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(HealthCheckKind.KeyMissingInEnvironments, finding.Kind);
        Assert.Equal(HealthSeverity.Warning, finding.Severity);
        Assert.Equal("OnlyDev", finding.Key);
        Assert.Equal("prod", finding.Environment);
        Assert.Equal("azcfg promote dev prod", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_IgnoredKeys_AreNotReportedMissing()
    {
        var rig = new Rig(Binding with { Ignore = ["OnlyDev"] }, new() { ["dev"] = [Plain("OnlyDev")], ["prod"] = [] });

        var report = await rig.CheckAsync();

        Assert.Empty(report.Findings);
    }

    private static HealthFinding[] Mismatches(HealthReport report) => [.. report.Findings.Where(f => f.Kind == HealthCheckKind.BindingMismatch)];

    [Fact]
    public async Task CheckAsync_ServiceHasNoKeys_WithOneOtherPrefix_WarnsAndSuggestsIt()
    {
        var rig = new Rig(Binding with { Service = "billing" });

        var finding = Assert.Single(Mismatches(await rig.CheckAsync()));

        Assert.Equal(HealthSeverity.Warning, finding.Severity);
        Assert.Contains("billing", finding.Message);
        Assert.Equal("azcfg config set project.service orders", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_ServiceHasNoKeys_WithSeveralOtherPrefixes_UsesPlaceholder()
    {
        var rig = new Rig(Binding with { Service = "billing" });
        rig.Prefixes["dev"] = ["orders", "users"];

        var finding = Assert.Single(Mismatches(await rig.CheckAsync()));

        Assert.Equal("azcfg config set project.service <SERVICE>", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_ServiceHasNoKeys_WithNoPrefixes_UsesPlaceholder()
    {
        var rig = new Rig(Binding with { Service = "billing" });
        rig.Prefixes["dev"] = [];
        rig.Prefixes["prod"] = [];

        var finding = Assert.Single(Mismatches(await rig.CheckAsync()));

        Assert.Equal("azcfg config set project.service <SERVICE>", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_ServiceExistsInOneEnvironmentOnly_DoesNotWarn()
    {
        var rig = new Rig(Binding);
        rig.Prefixes["prod"] = [];

        Assert.Empty(Mismatches(await rig.CheckAsync()));
    }

    [Fact]
    public async Task CheckAsync_ServicePrefixCaseDiffers_IsAMismatch()
    {
        var rig = new Rig(Binding with { Service = "Orders" });

        Assert.Single(Mismatches(await rig.CheckAsync()));
    }

    [Fact]
    public async Task CheckAsync_NoEnvironmentReachable_DoesNotReportServiceMismatch()
    {
        var rig = new Rig(Binding with { Service = "billing" });
        rig.Config.Unreachable.UnionWith(["dev", "prod"]);

        Assert.Empty(Mismatches(await rig.CheckAsync()));
    }

    [Fact]
    public async Task CheckAsync_ServiceMismatch_OnlyReachableEnvironmentsCount()
    {
        var rig = new Rig(Binding);
        rig.Config.Unreachable.Add("prod");
        rig.Prefixes["prod"] = [];

        Assert.Empty(Mismatches(await rig.CheckAsync()));
    }

    [Fact]
    public async Task CheckAsync_MissingValuesProject_IsAnErrorWithFix()
    {
        var rig = new Rig(Binding with { ValuesTarget = new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, "/repo/Gone/Gone.csproj", LocalFileFormat.NestedJson) });

        var finding = Assert.Single(Mismatches(await rig.CheckAsync()));

        Assert.Equal(HealthSeverity.Error, finding.Severity);
        Assert.Equal("azcfg config set project.values appsettings-local:<PATH>", finding.Fix);
        Assert.Contains("/repo/Gone/Gone.csproj", finding.Message);
    }

    [Fact]
    public async Task CheckAsync_MissingSecretsProject_IsAnErrorWithFix()
    {
        var rig = new Rig(Binding with { SecretsTarget = new LocalTargetBinding(LocalTargetKind.UserSecrets, "/repo/Gone/Gone.csproj", LocalFileFormat.NestedJson) });

        var finding = Assert.Single(Mismatches(await rig.CheckAsync()));

        Assert.Equal(HealthSeverity.Error, finding.Severity);
        Assert.Equal("azcfg config set project.secrets user-secrets:<PATH>", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_SameMissingProjectForBothTargets_ReportsOnce()
    {
        var gone = new LocalTargetBinding(LocalTargetKind.UserSecrets, "/repo/Gone/Gone.csproj", LocalFileFormat.NestedJson);
        var rig = new Rig(Binding with { ValuesTarget = gone, SecretsTarget = gone });

        var finding = Assert.Single(Mismatches(await rig.CheckAsync()));

        Assert.Equal("azcfg config set project.secrets user-secrets:<PATH>", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_ProjectDirectoryOfTarget_IsNotMissing()
    {
        var rig = new Rig(Binding with { ValuesTarget = new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, "/repo/Orders", LocalFileFormat.NestedJson) });

        Assert.Empty(Mismatches(await rig.CheckAsync()));
    }

    [Fact]
    public async Task CheckAsync_DetectorThrows_SkipsProjectCheckWithoutLeaking()
    {
        var rig = new Rig(Binding);
        rig.Detector.Throws = true;

        var report = await rig.CheckAsync();

        Assert.Empty(Mismatches(report));
        Assert.DoesNotContain("CANARY", Json(report));
    }

    [Fact]
    public async Task CheckAsync_VaultForUnknownEnvironment_WarnsWithFix()
    {
        var rig = new Rig(Binding with { VaultByEnvironment = new Dictionary<string, Uri> { ["staging"] = new("https://kv-staging.vault.azure.net"), ["DEV"] = new("https://kv-dev.vault.azure.net") } });

        var finding = Assert.Single(Mismatches(await rig.CheckAsync()));

        Assert.Equal(HealthSeverity.Warning, finding.Severity);
        Assert.Equal("staging", finding.Environment);
        Assert.Equal("azcfg config set project.vault.staging \"\"", finding.Fix);
    }

    [Fact]
    public async Task CheckAsync_NoBinding_HasNoBindingMismatch()
    {
        var rig = new Rig(null);

        Assert.Empty(Mismatches(await rig.CheckAsync()));
    }

    [Fact]
    public async Task CheckAsync_NotSignedIn_SkipsServiceCheckButChecksTargetProjects()
    {
        var rig = new Rig(Binding with { Service = "billing", ValuesTarget = new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, "/repo/Gone/Gone.csproj", LocalFileFormat.NestedJson) });
        rig.Probe.Result = new(false, null, "No credential");

        var mismatch = Assert.Single(Mismatches(await rig.CheckAsync()));
        Assert.Contains("no longer exists", mismatch.Message);
    }

    [Fact]
    public async Task CheckAsync_HealthyBinding_HasNoBindingMismatch()
    {
        var rig = new Rig(Binding with { VaultByEnvironment = new Dictionary<string, Uri> { ["prod"] = new("https://kv-prod.vault.azure.net") } });

        Assert.Empty(Mismatches(await rig.CheckAsync()));
    }

    [Fact]
    public async Task CheckAsync_Cancelled_Propagates()
    {
        var rig = new Rig(Binding);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.CheckAsync("/repo", cts.Token));
    }

    [Fact]
    public async Task CheckAsync_Report_NeverContainsCanaryValues()
    {
        const string canary = "AZCFG-CANARY-s3cret";
        var rig = new Rig(Binding, new() { ["dev"] = [new("Plain", ConfigScope.Service, new ConfigValue.Plain(canary)), Ref("Db:Password", DevSecret)], ["prod"] = [] });
        rig.KeyVault.Missing.Add(DevSecret);

        var report = await rig.CheckAsync();

        Assert.NotEmpty(report.Findings);
        Assert.DoesNotContain(canary, Json(report));
    }
    [Fact]
    public async Task CheckAsync_EnvironmentDenied_ReportsForbiddenAccess()
    {
        var rig = new Rig(Binding);
        rig.Config.Unreachable.Add("prod");

        var report = await rig.CheckAsync();

        Assert.Equal(EnvironmentAccess.Ok, report.Environments.Single(e => e.Name == "dev").Access);
        var prod = report.Environments.Single(e => e.Name == "prod");
        Assert.Equal(EnvironmentAccess.Forbidden, prod.Access);
        Assert.False(prod.Reachable);
        Assert.DoesNotContain("CANARY", Json(report));
    }

    [Fact]
    public async Task CheckAsync_NotSignedIn_EnvironmentsNeedSignIn()
    {
        var rig = new Rig(Binding);
        rig.Probe.Result = new(false, null, "No credential");

        var report = await rig.CheckAsync();

        Assert.All(report.Environments, e => Assert.Equal(EnvironmentAccess.AuthRequired, e.Access));
    }

    [Fact]
    public async Task CheckEnvironmentAsync_WithService_ReadsServiceAndIsOk()
    {
        // No prefixes for 'dev': listing them would fail, so Ok proves the service was read.
        var store = new FakeConfigStore([], new() { ["dev"] = [Plain("A")] });
        var service = new Rig(Binding).With(store);

        var check = await service.CheckEnvironmentAsync(Env("dev"), "orders", Ct);

        Assert.Equal(new EnvironmentCheck("dev", EnvironmentAccess.Ok, null), check);
        Assert.True(check.Reachable);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_WithoutService_ListsPrefixes()
    {
        // No entries: reading the service would fail, so Ok proves the prefixes were listed.
        var store = new FakeConfigStore(new() { ["dev"] = ["orders"] });
        var service = new Rig(Binding).With(store);

        var check = await service.CheckEnvironmentAsync(Env("dev"), null, Ct);

        Assert.Equal(EnvironmentAccess.Ok, check.Access);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_Denied_ClassifiedWithoutMessage()
    {
        var store = new FakeConfigStore(new() { ["dev"] = ["orders"] });
        store.Unreachable.Add("dev");
        var service = new Rig(Binding).With(store);

        var check = await service.CheckEnvironmentAsync(Env("dev"), "orders", Ct);

        Assert.Equal(new EnvironmentCheck("dev", EnvironmentAccess.Forbidden, nameof(UnauthorizedAccessException)), check);
        Assert.False(check.Reachable);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var service = new Rig(Binding).With(new CancellingStore());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckEnvironmentAsync(Env("dev"), "orders", cts.Token));
    }

    private sealed class CancellingStore : IConfigStore
    {
        public IAsyncEnumerable<ConfigEntry> ReadAsync(ConfigEnvironment env, string service, CancellationToken ct) => Throw<ConfigEntry>(ct);

        public IAsyncEnumerable<string> ListServicePrefixesAsync(ConfigEnvironment env, CancellationToken ct) => Throw<string>(ct);

        public Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListLabelsAsync(Uri storeEndpoint, CancellationToken ct) => throw new NotSupportedException();

        private static async IAsyncEnumerable<T> Throw<T>([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }
    }
}
