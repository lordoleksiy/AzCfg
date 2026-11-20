using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Application.Sync;

public sealed class SyncWorkflowPullTests
{
    private static async Task<SyncWorld> DivergedAsync()
    {
        var w = new SyncWorld();
        foreach (var key in new[] { "A", "B", "C" })
        {
            w.Seed(key, "1");
        }

        w.SeedSecret("Api:Key", "old");
        await w.PullAsync();
        w.Seed("A", "2");
        w.Seed("D", "1");
        w.World.Secrets.Add(w.Vault, "crm--Api--Key", SyncWorld.Sentinel);
        w.World.UserSecrets.SetLocal("B", "local-b");
        w.World.UserSecrets.SetLocal("C", "local-c");
        w.Seed("C", "remote-c");
        return w;
    }

    [Fact]
    public async Task PreviewPullAsync_GroupsIncomingConflictsAndLocalChanges()
    {
        var w = await DivergedAsync();

        var preview = await w.Workflow.PreviewPullAsync(w.Binding, w.Env, w.Ct);

        Assert.Equal(
            [
                new PullRow("A", DiffKind.RemoteModified, false, "1", "2"),
                new PullRow("Api:Key", DiffKind.RemoteModified, true, null, null),
                new PullRow("D", DiffKind.RemoteAdded, false, null, "1"),
            ],
            preview.Incoming);
        Assert.Equal([new PullRow("C", DiffKind.Conflict, false, "local-c", "remote-c")], preview.Conflicts);
        Assert.Equal(["B"], preview.LocalChanges);
        Assert.DoesNotContain(SyncWorld.Sentinel, JsonSerializer.Serialize(preview));
        Assert.DoesNotContain(SyncWorld.Sentinel, preview.ToString());
    }

    [Fact]
    public async Task PreviewPullAsync_NotActiveEnvironment_Throws()
    {
        var w = new SyncWorld();
        w.Seed("A", "1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Workflow.PreviewPullAsync(w.Binding, w.Env, w.Ct));
    }

    [Fact]
    public async Task UnmanagedLocalKeysAsync_WithoutActiveEnvironment_ListsKeysOfBothTargets()
    {
        var w = new SyncWorld();
        await w.World.UserSecrets.WriteAsync(w.Binding.ValuesTarget, [new("Z", "1"), new("Db:Host", "x")], w.Ct);

        Assert.Equal(["Db:Host", "Z"], await w.Workflow.UnmanagedLocalKeysAsync(w.Binding, w.Ct));
        Assert.Empty(await w.Workflow.UnmanagedLocalKeysAsync(w.Binding with { ActiveEnvironment = "dev" }, w.Ct));
    }

    [Fact]
    public async Task PullAsync_WithoutForce_IsBlockedByLocalChanges_ForceOverwrites()
    {
        var w = await DivergedAsync();

        var blocked = await w.Workflow.PullAsync(w.Binding, w.Env, false, w.Ct);
        var forced = await w.Workflow.PullAsync(w.Binding, w.Env, true, w.Ct);

        Assert.True(blocked.Blocked);
        Assert.Equal(["B", "C"], blocked.LocalChanges.Order());
        Assert.False(forced.Blocked);
        Assert.Equal("2", w.World.UserSecrets.Local("A"));
        Assert.Equal("1", w.World.UserSecrets.Local("B"));
    }

    [Fact]
    public async Task PullSelectedAsync_WritesOnlySelectedKeys()
    {
        var w = await DivergedAsync();

        var result = await w.Workflow.PullSelectedAsync(w.Binding, w.Env, ["A"], w.Ct);

        Assert.Equal(1, result.ValuesWritten);
        Assert.Equal("2", w.World.UserSecrets.Local("A"));
        Assert.Null(w.World.UserSecrets.Local("D"));
        Assert.Equal("local-b", w.World.UserSecrets.Local("B"));
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepRemote_PullsTheKey()
    {
        var w = await DivergedAsync();

        await w.Workflow.ResolveConflictAsync(w.Binding, w.Env, "C", ConflictChoice.KeepRemote, w.Ct);

        Assert.Equal("remote-c", w.World.UserSecrets.Local("C"));
        Assert.Empty((await w.Workflow.PreviewPullAsync(w.Binding, w.Env, w.Ct)).Conflicts);
    }
}
