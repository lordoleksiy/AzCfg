using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceEditedJsonBaseTests
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";

    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment Env = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Target, Target, new Dictionary<string, Uri>());

    /// <summary>Export the edit started from: A=1, B=2, the secret reference and global G=g.</summary>
    private const string BaseJson = """{"A":"1","B":"2","Db":{"Password":{"@kv":"Db--Password"}},"@global":{"G":"g"}}""";

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value), "e");

    /// <summary>Azure after the export: B changed by someone else.</summary>
    private static readonly ConfigEntry[] Remote =
    [
        Plain("A", "1"),
        Plain("B", "remote"),
        new("Db:Password", ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri), "e"),
        Plain("G", "g", ConfigScope.Global),
    ];

    private static Task<SyncPlan> Plan(string json, string? baseJson = BaseJson) =>
        new SyncService(
            new FakeConfigStore([], new() { ["dev"] = Remote }),
            new FakeSecretStore(new() { [SecretUri] = "old-secret" }),
            new FakeUserStateStore(),
            new HmacSecretFingerprinter(new byte[32]),
            []).PlanPushAsync(Binding, Env, new PushSource.EditedJson(json, baseJson), CancellationToken.None);

    [Fact]
    public async Task PlanPushAsync_UntouchedKeyChangedInAzure_IsNotReverted()
    {
        var plan = await Plan(BaseJson);

        Assert.Empty(plan.Changes);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task PlanPushAsync_WithoutBaseJson_RevertsTheAzureChange()
    {
        var plan = await Plan(BaseJson, baseJson: null);

        var change = Assert.Single(plan.Changes);
        Assert.Equal((ChangeKind.Update, "B"), (change.Kind, change.After!.Key));
    }

    [Fact]
    public async Task PlanPushAsync_KeyEditedLocallyAndInAzure_IsConflict()
    {
        var plan = await Plan("""{"A":"1","B":"mine","Db":{"Password":{"@kv":"Db--Password"}},"@global":{"G":"g"}}""");

        Assert.Empty(plan.Changes);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("B", conflict.Key);
    }

    [Fact]
    public async Task PlanPushAsync_KeyEditedOnlyLocally_IsUpdate()
    {
        var plan = await Plan("""{"A":"x","B":"2","Db":{"Password":{"@kv":"Db--Password"}},"@global":{"G":"g"}}""");

        Assert.Empty(plan.Conflicts);
        var change = Assert.Single(plan.Changes);
        Assert.Equal((ChangeKind.Update, "A", (ConfigValue)new ConfigValue.Plain("x")), (change.Kind, change.After!.Key, change.After.Value));
    }

    [Fact]
    public async Task PlanPushAsync_KeyRemovedInJson_IsDelete()
    {
        var plan = await Plan("""{"B":"2","Db":{"Password":{"@kv":"Db--Password"}},"@global":{"G":"g"}}""");

        Assert.Empty(plan.Conflicts);
        var change = Assert.Single(plan.Changes);
        Assert.Equal((ChangeKind.Delete, "A"), (change.Kind, change.Before!.Key));
    }

    [Fact]
    public async Task PlanPushAsync_UnchangedSecretReference_IsNoChange()
    {
        var plan = await Plan("""{"A":"1","B":"2","Db":{"Password":{"@kv":"Db--Password"}},"@global":{"G":"g"}}""");

        Assert.DoesNotContain(plan.Changes, c => (c.After ?? c.Before)!.Key == "Db:Password");
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task PlanPushAsync_NewSecretWithValue_IsCreateSecret()
    {
        var plan = await Plan("""{"A":"1","B":"2","Db":{"Password":{"@kv":"Db--Password"}},"Api":{"Key":{"@kv":"Api--Key","value":""" + $"\"{Sentinel}\"" + """}},"@global":{"G":"g"}}""");

        Assert.Empty(plan.Conflicts);
        var change = Assert.Single(plan.Changes);
        Assert.Equal((ChangeKind.CreateSecret, "Api--Key", "Api:Key"), (change.Kind, change.SecretName, change.After!.Key));
        Assert.Equal(Sentinel, change.SecretValue!.Reveal());
        Assert.DoesNotContain(Sentinel, plan.ToString());
    }
}
