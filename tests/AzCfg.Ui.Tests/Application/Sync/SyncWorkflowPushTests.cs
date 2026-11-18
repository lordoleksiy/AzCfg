using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Application.Sync;

public sealed class SyncWorkflowPushTests
{
    private static async Task<SyncWorld> PulledAsync(string env = "dev", bool isProtected = false)
    {
        var w = new SyncWorld(env, isProtected);
        w.Seed("Db:Host", "h1");
        w.Seed("Db:Port", "1");
        await w.PullAsync();
        return w;
    }

    [Fact]
    public async Task PreviewPushAsync_NotActiveEnvironment_Throws()
    {
        var w = new SyncWorld();
        w.Seed("Db:Host", "h1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Workflow.PreviewPushAsync(w.Binding with { ActiveEnvironment = "stage" }, w.Env, false, w.Ct));
    }

    [Fact]
    public async Task PreviewPushAsync_PlainChanges_RowsCarryBeforeAndAfter()
    {
        var w = await PulledAsync();
        w.World.UserSecrets.SetLocal("Db:Host", "h2");
        w.World.UserSecrets.SetLocal("Db:Name", "crm");

        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);

        Assert.Equal(PushOrigin.LocalFiles, preview.Origin);
        Assert.Equal(
            [
                new ChangeRow("Db:Host", ChangeKind.Update, ConfigScope.Service, false, "h1", "h2", null, false),
                new ChangeRow("Db:Name", ChangeKind.Add, ConfigScope.Service, false, null, "crm", null, false),
            ],
            preview.Changes);
        Assert.Empty(preview.Conflicts);
    }

    [Fact]
    public async Task PreviewPushAsync_IncludeDeletes_MapsToLocalFilesDelete()
    {
        var w = await PulledAsync();
        w.World.UserSecrets.RemoveLocal("Db:Port");

        var upsert = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);
        var withDeletes = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, true, w.Ct);

        Assert.Empty(upsert.Changes);
        Assert.Contains(upsert.Warnings, x => x.Kind == SyncWarningKind.DeleteSkipped);
        var row = Assert.Single(withDeletes.Changes);
        Assert.Equal(new ChangeRow("Db:Port", ChangeKind.Delete, ConfigScope.Service, false, "1", null, null, false), row);
    }

    [Fact]
    public async Task PreviewPushAsync_ChangedSecret_RowHasNoValuesAndKeepsSecretName()
    {
        var w = new SyncWorld();
        w.SeedSecret("Api:Key", "old");
        await w.PullAsync();
        w.World.UserSecrets.SetLocal("Api:Key", SyncWorld.Sentinel);

        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);

        var row = Assert.Single(preview.Changes);
        Assert.Equal(new ChangeRow("Api:Key", ChangeKind.CreateSecret, ConfigScope.Service, true, null, null, "crm--Api--Key", false), row);
        Assert.DoesNotContain(SyncWorld.Sentinel, JsonSerializer.Serialize(preview));
        Assert.DoesNotContain(SyncWorld.Sentinel, preview.ToString());
    }

    [Fact]
    public async Task PreviewDraftAsync_NewSecretWithoutVault_AwaitsVault()
    {
        var w = new SyncWorld(withVault: false);
        w.Seed("Db:Host", "h1");
        var draft = w.Drafts.SetSecretValue(await w.Drafts.StartAsync(w.Binding, w.Env, w.Ct), new DraftKey(ConfigScope.Service, "Api:Token"), new SecretString(SyncWorld.Sentinel));

        var preview = await w.Workflow.PreviewDraftAsync(draft, w.Ct);

        Assert.Equal(PushOrigin.Draft, preview.Origin);
        Assert.True(preview.Plan.RequiresVaultChoice);
        var row = Assert.Single(preview.Changes);
        Assert.Equal(new ChangeRow("crm--Api--Token", ChangeKind.CreateSecret, ConfigScope.Service, true, null, null, "crm--Api--Token", true), row);
        Assert.Contains(ApplyBlock.VaultChoiceRequired, w.Workflow.Check(preview, new ApplyConfirmation(null, false)));
        Assert.DoesNotContain(SyncWorld.Sentinel, JsonSerializer.Serialize(preview));
        Assert.DoesNotContain(SyncWorld.Sentinel, preview.ToString());
    }

    [Fact]
    public async Task ListVaultsAsync_OrdersByName()
    {
        var w = new SyncWorld();
        w.World.Discovery.AddVault("kv-a", "https://kv-a.vault.azure.net");

        var vaults = await w.Workflow.ListVaultsAsync(w.Ct);

        Assert.Equal(["kv-a", "kv-dev"], vaults.Select(v => v.Name));
    }

    [Fact]
    public async Task SelectAndCheck_SubsetAndGuards()
    {
        var w = await PulledAsync();
        w.World.UserSecrets.SetLocal("Db:Host", "h2");
        w.World.UserSecrets.SetLocal("Db:Port", "2");
        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);
        var confirmation = new ApplyConfirmation(null, false);

        var subset = w.Workflow.Select(preview, new HashSet<string> { "Db:Host" });
        var none = w.Workflow.Select(preview, new HashSet<string>());

        Assert.Equal(["Db:Host"], subset.Changes.Select(c => c.Key));
        Assert.Equal(PlanSelection.Select(preview.Plan, new HashSet<string> { "Db:Host" }).Changes, subset.Plan.Changes);
        Assert.Empty(w.Workflow.Check(subset, confirmation));
        Assert.Equal([ApplyBlock.NothingSelected], w.Workflow.Check(none, confirmation));
    }

    [Fact]
    public async Task ApplyAsync_Subset_UpdatesSnapshotOnlyForSelectedKeys()
    {
        var w = await PulledAsync();
        w.World.UserSecrets.SetLocal("Db:Host", "h2");
        w.World.UserSecrets.SetLocal("Db:Port", "2");
        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);

        var result = await w.Workflow.ApplyAsync(w.Workflow.Select(preview, new HashSet<string> { "Db:Host" }), new ApplyConfirmation(null, false), w.Ct);

        Assert.Equal(1, result.Applied);
        Assert.Equal("h2", w.Remote("Db:Host"));
        Assert.Equal("1", w.Remote("Db:Port"));
        Assert.Equal("h2", (w.Snapshot()!.Items["Db:Host"].Value as StateValue.Plain)!.Value);
        Assert.Equal("1", (w.Snapshot()!.Items["Db:Port"].Value as StateValue.Plain)!.Value);
    }

    [Fact]
    public async Task ApplyAsync_ProtectedEnvironment_NeedsTypedNameAndPassesItToCore()
    {
        var w = await PulledAsync("prod", isProtected: true);
        w.World.UserSecrets.SetLocal("Db:Host", "h2");
        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);

        Assert.Equal([ApplyBlock.EnvironmentNotConfirmed], w.Workflow.Check(preview, new ApplyConfirmation(null, false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Workflow.ApplyAsync(preview, new ApplyConfirmation("dev", false), w.Ct));
        Assert.Equal("h1", w.Remote("Db:Host"));

        await w.Workflow.ApplyAsync(preview, new ApplyConfirmation("PROD", false), w.Ct);

        Assert.Equal("h2", w.Remote("Db:Host"));
    }

    [Fact]
    public async Task ApplyAsync_GlobalChange_NeedsConfirmationAndPassesItToCore()
    {
        var w = new SyncWorld();
        w.SeedGlobal("Feature:On", "1");
        await w.PullAsync();
        w.World.UserSecrets.SetLocal("Feature:On", "2");
        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);

        Assert.Equal(ConfigScope.Global, Assert.Single(preview.Changes).Scope);
        Assert.Equal([ApplyBlock.GlobalNotConfirmed], w.Workflow.Check(preview, new ApplyConfirmation(null, false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Workflow.ApplyAsync(preview, new ApplyConfirmation(null, false), w.Ct));

        await w.Workflow.ApplyAsync(preview, new ApplyConfirmation(null, true), w.Ct);

        Assert.Equal("2", w.World.AppConfig.Get(w.Store, "global:Feature:On")!.Text);
    }

    [Fact]
    public async Task ApplyAsync_Conflicts_AreBlocked()
    {
        var w = await PulledAsync();
        w.World.UserSecrets.SetLocal("Db:Host", "local");
        w.Seed("Db:Host", "remote");
        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);

        var row = Assert.Single(preview.Conflicts);
        Assert.Equal(new ConflictRow("Db:Host", ConfigScope.Service, false, "h1", "local", "remote"), row);
        Assert.Contains(ApplyBlock.Conflicts, w.Workflow.Check(preview, new ApplyConfirmation(null, false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Workflow.ApplyAsync(preview, new ApplyConfirmation(null, false), w.Ct));
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepLocal_RePlansLocalValueAsUpdate()
    {
        var w = await PulledAsync();
        w.World.UserSecrets.SetLocal("Db:Host", "local");
        w.Seed("Db:Host", "remote");

        await w.Workflow.ResolveConflictAsync(w.Binding, w.Env, "Db:Host", ConflictChoice.KeepLocal, w.Ct);
        var preview = await w.Workflow.PreviewPushAsync(w.Binding, w.Env, false, w.Ct);

        Assert.Empty(preview.Conflicts);
        var row = Assert.Single(preview.Changes);
        Assert.Equal((ChangeKind.Update, "remote", "local"), (row.Kind, row.Before, row.After));
    }
}
