using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.Sync;

public class PlanSelectionTests
{
    private static readonly LocalTargetBinding Local = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Local, Local, new Dictionary<string, Uri>());
    private static readonly ConfigEnvironment Env = new("dev", new("https://dev.azconfig.io"), null, false);

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value), "etag");
    private static Change Add(string key, ConfigScope scope = ConfigScope.Service) => new(ChangeKind.Add, null, Plain(key, "v", scope));
    private static Change Delete(string key, ConfigScope scope = ConfigScope.Service) => new(ChangeKind.Delete, Plain(key, "v", scope), null);
    private static Change PendingSecret(string name) => new(ChangeKind.CreateSecret, null, null, name);
    private static HashSet<string> Keys(params string[] keys) => [.. keys];

    private static SyncPlan Plan(Change[] changes, Conflict[]? conflicts = null, SyncWarning[]? warnings = null, bool vaultChoice = true, bool global = true) =>
        new(Binding, Env, changes, conflicts ?? [], vaultChoice, global) { Warnings = warnings ?? [] };

    [Fact]
    public void Select_KeepsOnlySelectedChangesConflictsAndWarnings()
    {
        var plan = Plan(
            [Add("A"), new(ChangeKind.Update, Plain("B", "1"), Plain("B", "2")), Delete("C")],
            [new("A", null, null, null), new("X", null, null, null)],
            [new(SyncWarningKind.DeleteSkipped, "C", "c"), new(SyncWarningKind.LabelOverride, "B", "b")]);

        var selected = PlanSelection.Select(plan, Keys("B", "C", "X"));

        Assert.Equal(["B", "C"], selected.Changes.Select(c => (c.After ?? c.Before)!.Key));
        Assert.Equal(["X"], selected.Conflicts.Select(c => c.Key));
        Assert.Equal(["C", "B"], selected.Warnings.Select(w => w.Key));
        Assert.Same(plan.Binding, selected.Binding);
        Assert.Same(plan.Environment, selected.Environment);
    }

    [Fact]
    public void Select_MatchesKeysOrdinally()
    {
        var plan = Plan([Add("Key"), Add("key")]);

        var selected = PlanSelection.Select(plan, new HashSet<string>(["key"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["key"], selected.Changes.Select(c => c.After!.Key));
    }

    [Fact]
    public void Select_KeylessCreateSecret_KeptRegardlessOfKeys()
    {
        var plan = Plan([Add("A"), PendingSecret("orders--New"), new(ChangeKind.CreateSecret, Plain("B", "1"), null, "orders--B")], vaultChoice: true);

        var selected = PlanSelection.Select(plan, Keys());

        var change = Assert.Single(selected.Changes);
        Assert.Equal("orders--New", change.SecretName);
        Assert.True(selected.RequiresVaultChoice);
    }

    [Fact]
    public void Select_RecomputesRequiresVaultChoice()
    {
        var plan = Plan([Add("A"), new(ChangeKind.CreateSecret, Plain("B", "1"), null, "orders--B")], vaultChoice: true);

        Assert.False(PlanSelection.Select(plan, Keys("A")).RequiresVaultChoice);
        Assert.True(PlanSelection.Select(plan, Keys("B")).RequiresVaultChoice);
        Assert.False(PlanSelection.Select(plan with { RequiresVaultChoice = false }, Keys("A")).RequiresVaultChoice);
    }

    [Fact]
    public void Select_RecomputesTouchesGlobal()
    {
        var plan = Plan([Add("A"), Add("G", ConfigScope.Global), Delete("D", ConfigScope.Global),
            new(ChangeKind.Update, Plain("U", "1", ConfigScope.Global), Plain("U", "2", ConfigScope.Global))], global: false);

        Assert.False(PlanSelection.Select(plan, Keys("A")).TouchesGlobal);
        Assert.True(PlanSelection.Select(plan, Keys("G")).TouchesGlobal);
        Assert.True(PlanSelection.Select(plan, Keys("U")).TouchesGlobal);
        Assert.False(PlanSelection.Select(plan, Keys("A", "D")).TouchesGlobal);
    }

    [Fact]
    public void Select_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PlanSelection.Select(null!, Keys()));
        Assert.Throws<ArgumentNullException>(() => PlanSelection.Select(Plan([]), null!));
    }

    [Fact]
    public async Task Select_ThenApply_UpdatesSnapshotOnlyForSelectedKeys()
    {
        var state = new FakeUserStateStore();
        state.Snapshots["dev"] = new(new Dictionary<string, StateItem>
        {
            ["X"] = new("X", ConfigScope.Service, new StateValue.Plain("old")),
            ["Y"] = new("Y", ConfigScope.Service, new StateValue.Plain("1")),
        });
        var store = new FakeConfigStore([], new() { ["dev"] = [Plain("X", "new"), Plain("Y", "2")] });
        var service = new SyncService(store, new FakeSecretStore([]), state, new HmacSecretFingerprinter(new byte[32]), []);
        var plan = Plan([new(ChangeKind.Update, Plain("X", "old"), Plain("X", "new")), new(ChangeKind.Update, Plain("Y", "1"), Plain("Y", "2"))],
            vaultChoice: false, global: false);

        await service.ApplyAsync(PlanSelection.Select(plan, Keys("Y")), new(), CancellationToken.None);

        Assert.Equal(["Y"], store.Applied!.Select(c => c.After!.Key));
        var snapshot = state.SavedSnapshot!.Value.State;
        Assert.Equal(new StateValue.Plain("old"), snapshot.Items["X"].Value);
        Assert.Equal(new StateValue.Plain("2"), snapshot.Items["Y"].Value);
    }
}
