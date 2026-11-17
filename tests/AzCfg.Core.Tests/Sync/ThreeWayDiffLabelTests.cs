using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class ThreeWayDiffLabelTests
{
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "src/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("repo", "orders", Target, Target, new Dictionary<string, Uri>());
    private static readonly Uri Store = new("https://dev.azconfig.io");
    private static readonly ConfigEnvironment StoreMode = new("dev", Store, null, false, "Development");
    private static readonly ConfigEnvironment LabelMode = new("dev", Store, "Development", false, "Development");

    [Fact]
    public void Plan_Add_UsesWriteLabel()
    {
        var change = Assert.Single(ThreeWayDiff.Plan(new([Item(DiffKind.LocalAdded, null, Plain("K", "v"), null)]), [], Binding, StoreMode).Changes);

        Assert.Equal("Development", change.After!.Label);
    }

    [Fact]
    public void Plan_StoreMode_Update_KeepsLabelOfRemoteCopy()
    {
        var remote = Entry("K", "a", "Legacy");

        var change = Assert.Single(Plan(StoreMode, Item(DiffKind.LocalModified, Plain("K", "a"), Plain("K", "b"), Plain("K", "a")), remote).Changes);

        Assert.Equal(ChangeKind.Update, change.Kind);
        Assert.Same(remote, change.Before);
        Assert.Equal("Legacy", change.After!.Label);
    }

    [Fact]
    public void Plan_LabelMode_ChangeOfUnlabeledKey_AddsOverrideAndWarns()
    {
        var plan = Plan(LabelMode, Item(DiffKind.LocalModified, Plain("K", "a"), Plain("K", "b"), Plain("K", "a")), Entry("K", "a", null));

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Add, change.Kind);
        Assert.Null(change.Before);
        Assert.Equal("Development", change.After!.Label);
        Assert.Equal(SyncWarningKind.LabelOverride, Assert.Single(plan.Warnings).Kind);
    }

    [Fact]
    public void Plan_LabelMode_DeleteOfUnlabeledKey_DoesNothingAndWarns()
    {
        var plan = Plan(LabelMode, Item(DiffKind.LocalDeleted, Plain("K", "a"), null, Plain("K", "a")), Entry("K", "a", null));

        Assert.Empty(plan.Changes);
        Assert.Equal(SyncWarningKind.DeleteSkipped, Assert.Single(plan.Warnings).Kind);
    }

    [Fact]
    public void Plan_LabelMode_DeleteOfLabeledKey_DeletesIt()
    {
        var remote = Entry("K", "a", "Development");

        var change = Assert.Single(Plan(LabelMode, Item(DiffKind.LocalDeleted, Plain("K", "a"), null, Plain("K", "a")), remote).Changes);

        Assert.Equal(ChangeKind.Delete, change.Kind);
        Assert.Same(remote, change.Before);
    }

    private static SyncPlan Plan(ConfigEnvironment env, DiffItem item, params ConfigEntry[] remote) => ThreeWayDiff.Plan(new([item]), remote, Binding, env);

    private static DiffItem Item(DiffKind kind, StateItem? b, StateItem? l, StateItem? r) => new((b ?? l ?? r)!.Key, kind, b, l, r);

    private static ConfigEntry Entry(string key, string value, string? label) => new(key, ConfigScope.Service, new ConfigValue.Plain(value), "etag", label);

    private static StateItem Plain(string key, string value) => new(key, ConfigScope.Service, new StateValue.Plain(value));
}
