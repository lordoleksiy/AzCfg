using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceDiffPlanTests
{
    private const string SecretText = "old-secret";
    private const string NewSecretText = "n3w-secret";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly HmacSecretFingerprinter Fingerprinter = new(new byte[32]);
    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment Env = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly string NewSecretJson = """{"Db":{"Password":{"@kv":"Db--Password","value":"n3w-secret"}}}""";

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value), "etag");
    private static ConfigEntry Ref(string key, Uri uri, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.KeyVaultRef(uri), "etag");
    private static StateItem SecretItem(string key) => new(key, ConfigScope.Service, new StateValue.Secret(SecretUri, Fingerprinter.Fingerprint(new SecretString(SecretText))));

    private static ProjectBinding Binding(LocalTargetBinding? secrets = null, Uri? vault = null) =>
        new("/repo", "orders", Values, secrets ?? Secrets, vault is null ? new Dictionary<string, Uri>() : new Dictionary<string, Uri> { ["dev"] = vault });

    private sealed record Setup(SyncService Service, FakeUserStateStore State, FakeLocalTarget ValuesTarget, FakeLocalTarget SecretsTarget);

    private static Setup Create(ConfigEntry[] entries, LocalValue[]? values = null, LocalValue[]? secrets = null, ConfigState? snapshot = null)
    {
        var state = new FakeUserStateStore();
        if (snapshot is not null)
        {
            state.Snapshots["dev"] = snapshot;
        }

        var valuesTarget = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal) { Contents = values ?? [] };
        var secretsTarget = new FakeLocalTarget(LocalTargetKind.UserSecrets) { Contents = secrets ?? [] };
        var secretValues = entries.Select(e => e.Value).OfType<ConfigValue.KeyVaultRef>().DistinctBy(r => r.SecretUri).ToDictionary(r => r.SecretUri, _ => SecretText);
        return new(new(new FakeConfigStore([], new() { ["dev"] = entries }), new FakeSecretStore(secretValues), state, Fingerprinter, [valuesTarget, secretsTarget]), state, valuesTarget, secretsTarget);
    }

    private static ConfigState State(params StateItem[] items) => new(items.ToDictionary(i => i.Key, StringComparer.Ordinal));

    [Fact]
    public async Task DiffAsync_ComparesLocalFilesWithSnapshotAndRemote()
    {
        var s = Create(
            [Plain("A", "1"), Ref("Db:Password", SecretUri)],
            values: [new("A", "2")],
            secrets: [new("Db:Password", SecretText)],
            snapshot: State(new StateItem("A", ConfigScope.Service, new StateValue.Plain("1")), SecretItem("Db:Password")));

        var diff = await s.Service.DiffAsync(Binding(), Env, Ct);

        Assert.Equal([("A", DiffKind.LocalModified), ("Db:Password", DiffKind.Unchanged)], diff.Items.Select(i => (i.Key, i.Kind)));
    }

    [Fact]
    public async Task DiffAsync_NoSnapshot_UsesEmptyBase()
    {
        var s = Create([Plain("A", "1")], values: [new("B", "x")]);

        var diff = await s.Service.DiffAsync(Binding(), Env, Ct);

        Assert.Equal([("A", DiffKind.RemoteAdded), ("B", DiffKind.LocalAdded)], diff.Items.Select(i => (i.Key, i.Kind)));
    }

    [Fact]
    public async Task PlanPushAsync_LocalFiles_PlansPlainChangesAndReadsSharedTargetOnce()
    {
        var s = Create([Plain("A", "1")], values: [new("A", "2"), new("B", "x")], snapshot: State(new StateItem("A", ConfigScope.Service, new StateValue.Plain("1"))));

        var plan = await s.Service.PlanPushAsync(Binding(Values), Env, new PushSource.LocalFiles(), Ct);

        Assert.Equal([ChangeKind.Update, ChangeKind.Add], plan.Changes.Select(c => c.Kind));
        Assert.Equal("etag", plan.Changes[0].Before!.ETag);
        Assert.Equal(1, s.ValuesTarget.Reads);
        Assert.Equal(0, s.SecretsTarget.Reads);
    }

    [Fact]
    public async Task PlanPushAsync_LocalFiles_ChangedSecret_CarriesValueAndVaultFromItsUri()
    {
        var s = Create(
            [Ref("Db:Password", SecretUri)],
            secrets: [new("Db:Password", NewSecretText)],
            snapshot: State(SecretItem("Db:Password")));

        var plan = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.LocalFiles(), Ct);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.CreateSecret, change.Kind);
        Assert.Equal("Db--Password", change.SecretName);
        Assert.Equal(NewSecretText, change.SecretValue!.Reveal());
        Assert.Equal(new ConfigValue.KeyVaultRef(new("https://vault.vault.azure.net/secrets/Db--Password")), change.After!.Value);
        Assert.False(plan.RequiresVaultChoice);
    }

    [Fact]
    public async Task PlanPushAsync_EditedJson_NewSecret_CarriesValueNameAndBoundVault()
    {
        var s = Create([]);

        var plan = await s.Service.PlanPushAsync(Binding(vault: new("https://bound.vault.azure.net/")), Env, new PushSource.EditedJson(NewSecretJson), Ct);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.CreateSecret, change.Kind);
        Assert.Equal("Db--Password", change.SecretName);
        Assert.Equal(NewSecretText, change.SecretValue!.Reveal());
        Assert.Equal(new ConfigEntry("Db:Password", ConfigScope.Service, new ConfigValue.KeyVaultRef(new("https://bound.vault.azure.net/secrets/Db--Password"))), change.After);
    }

    [Fact]
    public async Task PlanPushAsync_EditedJson_NewSecretKeepsExplicitName()
    {
        var s = Create([]);

        var plan = await s.Service.PlanPushAsync(Binding(vault: new("https://bound.vault.azure.net/")), Env, new PushSource.EditedJson("""{"Db":{"Password":{"@kv":"my-name","value":"x"}}}"""), Ct);

        Assert.Equal("my-name", Assert.Single(plan.Changes).SecretName);
    }

    [Fact]
    public async Task PlanPushAsync_EditedJson_KnownSecretWithoutValue_IsUnchanged()
    {
        var s = Create([Ref("Db:Password", SecretUri)], snapshot: State(SecretItem("Db:Password")));

        var plan = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.EditedJson("""{"Db":{"Password":{"@kv":"Db--Password"}}}"""), Ct);

        Assert.Empty(plan.Changes);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task PlanPushAsync_EditedJson_UnknownSecretWithoutValue_IsNotPlanned()
    {
        var s = Create([]);

        var plan = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.EditedJson("""{"Db":{"Password":{"@kv":"new-name"}},"A":"1"}"""), Ct);

        Assert.Equal([ChangeKind.Add], plan.Changes.Select(c => c.Kind));
        Assert.False(plan.RequiresVaultChoice);
    }

    [Theory]
    [InlineData("bound", "s1,s1,s2", "g1", "bound")]
    [InlineData(null, "s1,s2,s2", "g1,g1", "s2")]
    [InlineData(null, "", "g1,g2,g2", "g2")]
    [InlineData(null, "", "", null)]
    public async Task PlanPushAsync_ChoosesVaultForNewSecrets(string? bound, string serviceVaults, string globalVaults, string? expected)
    {
        Uri Secret(string vault, int i) => new($"https://{vault}.vault.azure.net/secrets/n{i}/v1");
        var entries = serviceVaults.Split(',', StringSplitOptions.RemoveEmptyEntries).Select((v, i) => Ref($"S{i}", Secret(v, i)))
            .Concat(globalVaults.Split(',', StringSplitOptions.RemoveEmptyEntries).Select((v, i) => Ref($"G{i}", Secret(v, 10 + i), ConfigScope.Global)));
        var s = Create([.. entries]);

        var plan = await s.Service.PlanPushAsync(Binding(vault: bound is null ? null : new($"https://{bound}.vault.azure.net/")), Env, new PushSource.EditedJson(NewSecretJson), Ct);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(expected is null, plan.RequiresVaultChoice);
        Assert.Equal(expected is null ? null : new ConfigValue.KeyVaultRef(new($"https://{expected}.vault.azure.net/secrets/Db--Password")), change.After?.Value);
        Assert.Equal(NewSecretText, change.SecretValue!.Reveal());
    }

    private static PushSource.Promote Promote(string json) => new(json, "stage");

    [Fact]
    public async Task PlanPushAsync_Promote_OnlyAddsNewKeys_NeverUpdatesOrDeletes()
    {
        var s = Create([Plain("A", "1"), Plain("B", "2")], snapshot: State(
            new StateItem("A", ConfigScope.Service, new StateValue.Plain("1")),
            new StateItem("B", ConfigScope.Service, new StateValue.Plain("2")),
            new StateItem("C", ConfigScope.Service, new StateValue.Plain("3"))));

        var plan = await s.Service.PlanPushAsync(Binding(), Env, Promote("""{"N":"x","@global":{"G":"1"}}"""), Ct);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Add, change.Kind);
        Assert.Equal("N", change.After!.Key);
        Assert.Empty(plan.Conflicts);
        Assert.False(plan.RequiresVaultChoice);
    }

    [Fact]
    public async Task PlanPushAsync_Promote_KeyAlreadyInTarget_IsConflict()
    {
        var s = Create([Plain("A", "1")]);

        var plan = await s.Service.PlanPushAsync(Binding(), Env, Promote("""{"A":"mine","N":"x"}"""), Ct);

        Assert.Equal("A", Assert.Single(plan.Conflicts).Key);
        Assert.Equal(["N"], plan.Changes.Select(c => c.After!.Key));
    }

    [Theory]
    [InlineData("bound", "other", "bound")]
    [InlineData(null, "other", "other")]
    public async Task PlanPushAsync_Promote_NewSecret_IsCreatedInTargetVault(string? bound, string existing, string expected)
    {
        var s = Create([Ref("S", new($"https://{existing}.vault.azure.net/secrets/s/v1"))]);

        var plan = await s.Service.PlanPushAsync(Binding(vault: bound is null ? null : new($"https://{bound}.vault.azure.net/")), Env, Promote(NewSecretJson), Ct);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.CreateSecret, change.Kind);
        Assert.Equal("Db--Password", change.SecretName);
        Assert.Equal(NewSecretText, change.SecretValue!.Reveal());
        Assert.Equal(new ConfigValue.KeyVaultRef(new($"https://{expected}.vault.azure.net/secrets/Db--Password")), change.After!.Value);
    }

    [Fact]
    public async Task PlanPushAsync_Promote_NewSecretWithoutVault_RequiresVaultChoice()
    {
        var plan = await Create([]).Service.PlanPushAsync(Binding(), Env, Promote(NewSecretJson), Ct);

        Assert.True(plan.RequiresVaultChoice);
        Assert.Null(Assert.Single(plan.Changes).After);
    }

    [Fact]
    public async Task PlanPushAsync_Promote_NullSecretValue_ThrowsNamingKeyWithoutValues()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Create([]).Service.PlanPushAsync(Binding(), Env, Promote("""{"Db":{"Password":{"@kv":"Db--Password","value":null}}}"""), Ct));

        Assert.Contains("Db:Password", ex.Message);
    }

    [Fact]
    public async Task PlanPushAsync_SerializedPlan_DoesNotContainSecretValues()
    {
        var s = Create([]);

        var plan = await s.Service.PlanPushAsync(Binding(vault: new("https://bound.vault.azure.net/")), Env, new PushSource.EditedJson(NewSecretJson), Ct);

        var json = JsonSerializer.Serialize(plan);
        Assert.DoesNotContain(NewSecretText, json);
        Assert.DoesNotContain(NewSecretText, plan.ToString());
        Assert.Contains("***", json);
    }
}
