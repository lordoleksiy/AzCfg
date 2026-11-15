using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class ThreeWayDiffPlanTests
{
    private static readonly Uri VaultUri = new("https://vault1.vault.azure.net/secrets/crm/0123abcd");
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "src/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("repo", "orders", Target, Target, new Dictionary<string, Uri>());
    private static readonly ConfigEnvironment Env = new("dev", new("https://dev.azconfig.io"), null, false);

    [Theory]
    [InlineData(DiffKind.Unchanged)]
    [InlineData(DiffKind.RemoteAdded)]
    [InlineData(DiffKind.RemoteModified)]
    [InlineData(DiffKind.RemoteDeleted)]
    public void Plan_IgnoresNonLocalChanges(DiffKind kind)
    {
        var plan = Plan([Item(kind, Plain("K", "a"), Plain("K", "a"), Plain("K", "b"))]);

        Assert.Empty(plan.Changes);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Plan_CopiesBindingAndEnvironment_WithoutVaultChoice()
    {
        var plan = Plan([]);

        Assert.Same(Binding, plan.Binding);
        Assert.Same(Env, plan.Environment);
        Assert.False(plan.RequiresVaultChoice);
        Assert.False(plan.TouchesGlobal);
    }

    [Fact]
    public void Plan_Conflict_GoesToConflicts()
    {
        var (b, l, r) = (Plain("K", "a"), Plain("K", "b"), Plain("K", "c"));

        var plan = Plan([Item(DiffKind.Conflict, b, l, r)]);

        Assert.Empty(plan.Changes);
        Assert.Equal(new Conflict("K", b, l, r), Assert.Single(plan.Conflicts));
    }

    [Fact]
    public void Plan_LocalAdded_IsAddWithoutBefore()
    {
        var change = Assert.Single(Plan([Item(DiffKind.LocalAdded, null, Plain("K", "v"), null)]).Changes);

        Assert.Equal(ChangeKind.Add, change.Kind);
        Assert.Null(change.Before);
        Assert.Equal(Entry("K", new ConfigValue.Plain("v")), change.After);
    }

    [Fact]
    public void Plan_LocalModified_IsUpdateWithRemoteBeforeAndETag()
    {
        var remote = Entry("K", new ConfigValue.Plain("a"), etag: "e1");

        var change = Assert.Single(Plan([Item(DiffKind.LocalModified, Plain("K", "a"), Plain("K", "b"), Plain("K", "a"))], remote).Changes);

        Assert.Equal(ChangeKind.Update, change.Kind);
        Assert.Equal(remote, change.Before);
        Assert.Equal(Entry("K", new ConfigValue.Plain("b")), change.After);
    }

    [Fact]
    public void Plan_ModifiedGlobalKey_OverridesInServiceScope()
    {
        var global = Entry("K", new ConfigValue.Plain("a"), ConfigScope.Global, "g1");

        var plan = Plan([GlobalModified()], global);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Add, change.Kind);
        Assert.Null(change.Before);
        Assert.Equal(Entry("K", new ConfigValue.Plain("b")), change.After);
        Assert.False(plan.TouchesGlobal);
    }

    [Fact]
    public void Plan_ModifiedGlobalKeyWithExistingServiceEntry_UpdatesServiceEntry()
    {
        var service = Entry("K", new ConfigValue.Plain("s"), etag: "s1");

        var change = Assert.Single(Plan([GlobalModified()], Entry("K", new ConfigValue.Plain("a"), ConfigScope.Global, "g1"), service).Changes);

        Assert.Equal(ChangeKind.Update, change.Kind);
        Assert.Equal(service, change.Before);
    }

    [Fact]
    public void Plan_ExplicitGlobal_TargetsGlobalScope_AndTouchesGlobal()
    {
        var global = Entry("K", new ConfigValue.Plain("a"), ConfigScope.Global, "g1");
        var item = Item(DiffKind.LocalModified, Plain("K", "a", ConfigScope.Global), Plain("K", "b", ConfigScope.Global), Plain("K", "a", ConfigScope.Global));

        var plan = Plan([item], global, Entry("K", new ConfigValue.Plain("s")));

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Update, change.Kind);
        Assert.Equal(global, change.Before);
        Assert.Equal(Entry("K", new ConfigValue.Plain("b"), ConfigScope.Global), change.After);
        Assert.True(plan.TouchesGlobal);
    }

    [Fact]
    public void Plan_DeletedGlobalKey_IsIgnored()
    {
        var global = Plain("K", "a", ConfigScope.Global);

        var plan = Plan([Item(DiffKind.LocalDeleted, global, null, global)], Entry("K", new ConfigValue.Plain("a"), ConfigScope.Global));

        Assert.Empty(plan.Changes);
        Assert.False(plan.TouchesGlobal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_LocalDeleted_IsDeleteWithRemoteBefore(bool secret)
    {
        var state = secret ? Secret("K", VaultUri) : Plain("K", "a");
        var remote = Entry("K", secret ? new ConfigValue.KeyVaultRef(VaultUri) : new ConfigValue.Plain("a"), etag: "e1");

        var change = Assert.Single(Plan([Item(DiffKind.LocalDeleted, state, null, state)], remote).Changes);

        Assert.Equal(ChangeKind.Delete, change.Kind);
        Assert.Equal(remote, change.Before);
        Assert.Null(change.After);
        Assert.Null(change.SecretName);
    }

    [Theory]
    [InlineData(true, "crm")]
    [InlineData(false, "orders--Crm--Secret")]
    public void Plan_SecretChange_IsCreateSecretNamedFromUriOrKey(bool hasUri, string expectedName)
    {
        var old = Secret("Crm:Secret", VaultUri, "old");
        var remote = Entry("Crm:Secret", new ConfigValue.KeyVaultRef(VaultUri), etag: "e1");

        var change = Assert.Single(Plan([Item(DiffKind.LocalModified, old, Secret("Crm:Secret", hasUri ? VaultUri : null), old)], remote).Changes);

        Assert.Equal(ChangeKind.CreateSecret, change.Kind);
        Assert.Equal(remote, change.Before);
        Assert.Null(change.After);
        Assert.Equal(expectedName, change.SecretName);
        Assert.Null(change.SecretValue);
    }

    [Fact]
    public void Plan_NewSecret_HasNoBeforeAndKeyDerivedName()
    {
        var change = Assert.Single(Plan([Item(DiffKind.LocalAdded, null, Secret("K", null), null)]).Changes);

        Assert.Equal(ChangeKind.CreateSecret, change.Kind);
        Assert.Null(change.Before);
        Assert.Equal("orders--K", change.SecretName);
    }

    [Fact]
    public void Plan_SecretOverrideOfGlobalKey_DoesNotReuseGlobalSecretName()
    {
        var global = Secret("K", VaultUri, "old", ConfigScope.Global);

        var change = Assert.Single(Plan([Item(DiffKind.LocalModified, global, Secret("K", VaultUri, "new"), global)]).Changes);

        Assert.Equal("orders--K", change.SecretName);
    }

    [Fact]
    public void Plan_Json_HasNoSecretValue()
    {
        var plan = Plan([Item(DiffKind.LocalAdded, null, Secret("K", null), null)]);

        Assert.DoesNotContain("SecretValue\":\"", JsonSerializer.Serialize(plan));
    }

    private static SyncPlan Plan(DiffItem[] items, params ConfigEntry[] remote) => ThreeWayDiff.Plan(new(items), remote, Binding, Env);

    private static DiffItem GlobalModified() =>
        Item(DiffKind.LocalModified, Plain("K", "a", ConfigScope.Global), Plain("K", "b"), Plain("K", "a", ConfigScope.Global));

    private static DiffItem Item(DiffKind kind, StateItem? b, StateItem? l, StateItem? r) => new((b ?? l ?? r)!.Key, kind, b, l, r);

    private static ConfigEntry Entry(string key, ConfigValue value, ConfigScope scope = ConfigScope.Service, string? etag = null) => new(key, scope, value, etag);

    private static StateItem Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new StateValue.Plain(value));

    private static StateItem Secret(string key, Uri? uri, string fingerprint = "fp", ConfigScope scope = ConfigScope.Service) =>
        new(key, scope, new StateValue.Secret(uri, fingerprint));
}
