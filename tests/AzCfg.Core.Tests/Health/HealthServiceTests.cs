using System.Text.Json;
using System.Text.Json.Serialization;
using AzCfg.Core.Environments;
using AzCfg.Core.Health;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
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
            Config = new(entries.Keys.ToDictionary(k => k, _ => new[] { "orders" }), entries);
            FakeLocalTarget[] all = targets.Length > 0 ? targets : [new(LocalTargetKind.UserSecrets), new(LocalTargetKind.AppSettingsLocal)];
            Service = new HealthService(
                State,
                new SeparateStoresResolver(),
                Config,
                new SyncService(Config, KeyVault, State, new HmacSecretFingerprinter(new byte[32]), all),
                KeyVault,
                Probe,
                all);
        }

        public FakeUserStateStore State { get; }
        public FakeConfigStore Config { get; }
        public FakeSecretStore KeyVault { get; } = new([]);
        public FakeAzureAccountProbe Probe { get; } = new();
        public HealthService Service { get; }

        public Task<HealthReport> CheckAsync() => Service.CheckAsync("/repo", Ct);
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
}
