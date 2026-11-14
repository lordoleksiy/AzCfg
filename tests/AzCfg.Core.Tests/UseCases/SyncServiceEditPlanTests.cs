using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceEditPlanTests
{
    private static readonly ConfigEnvironment Env = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Target, Target, new Dictionary<string, Uri>());
    private static readonly ConfigEntry[] Remote =
    [
        new("A", ConfigScope.Service, new ConfigValue.Plain("1"), "e"),
        new("B", ConfigScope.Service, new ConfigValue.Plain("2"), "e"),
        new("G", ConfigScope.Global, new ConfigValue.Plain("g"), "e"),
    ];

    private static Task<SyncPlan> Plan(string json) =>
        new SyncService(
            new FakeConfigStore([], new() { ["dev"] = Remote }),
            new FakeSecretStore([]),
            new FakeUserStateStore(),
            new HmacSecretFingerprinter(new byte[32]),
            []).PlanPushAsync(Binding, Env, new PushSource.EditedJson(json), CancellationToken.None);

    [Theory]
    [InlineData("""{"A":"x","B":"2","@global":{"G":"g"}}""", ChangeKind.Update, "A")]
    [InlineData("""{"A":"1","B":"2","@global":{"G":"x"}}""", ChangeKind.Update, "G")]
    [InlineData("""{"A":"1","B":"2","C":"3","@global":{"G":"g"}}""", ChangeKind.Add, "C")]
    [InlineData("""{"A":"1","@global":{"G":"g"}}""", ChangeKind.Delete, "B")]
    public async Task EditedJson_WithoutSnapshot_PlansOnlyTheEditedKey(string json, ChangeKind kind, string key)
    {
        var plan = await Plan(json);

        Assert.Empty(plan.Conflicts);
        var change = Assert.Single(plan.Changes);
        Assert.Equal((kind, key), (change.Kind, (change.After ?? change.Before)!.Key));
    }

    [Fact]
    public async Task EditedJson_DeletedGlobalKey_PlansNothing()
    {
        var plan = await Plan("""{"A":"1","B":"2"}""");

        Assert.Empty(plan.Conflicts);
        Assert.Empty(plan.Changes);
    }
}
