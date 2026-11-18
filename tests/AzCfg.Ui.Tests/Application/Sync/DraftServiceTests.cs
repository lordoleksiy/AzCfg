using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Application.Sync;

public sealed class DraftServiceTests
{
    private static readonly DraftKey Host = new(ConfigScope.Service, "Db:Host");

    private static async Task<(SyncWorld World, RemoteDraft Draft)> StartedAsync()
    {
        var w = new SyncWorld();
        w.Seed("Db:Host", "h1");
        w.Seed("Db:Port", "1");
        w.SeedSecret("Api:Key", "remote-secret");
        w.SeedGlobal("Feature:On", "1");
        return (w, await w.Drafts.StartAsync(w.Binding, w.Env, w.Ct));
    }

    [Fact]
    public async Task StartAsync_ExportsRemote_BaseEqualsJson()
    {
        var (_, draft) = await StartedAsync();

        Assert.Equal(draft.BaseJson, draft.Json);
        Assert.Contains("\"@kv\": \"crm--Api--Key\"", draft.Json);
        Assert.Contains("\"@global\"", draft.Json);
        Assert.NotNull(draft.Json);
        Assert.Empty(draft.Secrets);
    }

    [Fact]
    public async Task EditOperations_UpdatePendingKeys()
    {
        var (w, draft) = await StartedAsync();
        Assert.Empty(w.Drafts.PendingKeys(draft));

        var edited = w.Drafts.SetValue(draft, Host, "h2");
        var added = w.Drafts.SetValue(edited, new DraftKey(ConfigScope.Global, "Feature:New"), "x");
        var removed = w.Drafts.Remove(added, new DraftKey(ConfigScope.Service, "Db:Port"));

        Assert.Equal([new DraftKey(ConfigScope.Global, "Feature:New"), Host, new DraftKey(ConfigScope.Service, "Db:Port")], w.Drafts.PendingKeys(removed));
        Assert.Equal([new DraftKey(ConfigScope.Global, "Feature:New"), new DraftKey(ConfigScope.Service, "Db:Port")], w.Drafts.PendingKeys(w.Drafts.Revert(removed, Host)));
        Assert.Empty(w.Drafts.PendingKeys(w.Drafts.Revert(w.Drafts.Revert(w.Drafts.Revert(removed, Host), new DraftKey(ConfigScope.Global, "Feature:New")), new DraftKey(ConfigScope.Service, "Db:Port"))));
    }

    [Fact]
    public async Task SetSecretValue_NewKey_NamesByConventionAndKeepsValueOutOfJson()
    {
        var (w, draft) = await StartedAsync();

        var service = w.Drafts.SetSecretValue(draft, new DraftKey(ConfigScope.Service, "Api:Token"), new SecretString(SyncWorld.Sentinel));
        var global = w.Drafts.SetSecretValue(service, new DraftKey(ConfigScope.Global, "Flag:Secret"), new SecretString(SyncWorld.Sentinel));

        Assert.Equal(
            ["crm--Api--Token", "global--Flag--Secret"],
            global.Secrets.OrderBy(s => s.Value.SecretName, StringComparer.Ordinal).Select(s => s.Value.SecretName));
        Assert.Contains("crm--Api--Token", global.Json);
        AssertNoSentinel(global);
        Assert.Equal(
            [new DraftKey(ConfigScope.Global, "Flag:Secret"), new DraftKey(ConfigScope.Service, "Api:Token")],
            w.Drafts.PendingKeys(global));
    }

    [Fact]
    public async Task SetSecretValue_ExistingReference_KeepsItsName()
    {
        var w = new SyncWorld();
        w.World.AppConfig.SetSecretReference(w.Store, "crm:Api:Key", w.World.Secrets.Add(w.Vault, "custom-name", "old"));
        var draft = await w.Drafts.StartAsync(w.Binding, w.Env, w.Ct);

        var updated = w.Drafts.SetSecretValue(draft, new DraftKey(ConfigScope.Service, "Api:Key"), new SecretString(SyncWorld.Sentinel));

        Assert.Equal("custom-name", updated.Secrets.Single().Value.SecretName);
        Assert.Equal(draft.Json, updated.Json);
        AssertNoSentinel(updated);
    }

    [Fact]
    public async Task MoveToKeyVault_TurnsPlainValueIntoPendingSecret()
    {
        var (w, draft) = await StartedAsync();

        var moved = w.Drafts.MoveToKeyVault(draft, Host);

        Assert.Equal("h1", moved.Secrets[Host].Value.Reveal());
        Assert.Equal("crm--Db--Host", moved.Secrets[Host].SecretName);
        Assert.DoesNotContain("\"Host\": \"h1\"", moved.Json);
        Assert.Throws<ArgumentException>(() => w.Drafts.MoveToKeyVault(draft, new DraftKey(ConfigScope.Service, "Api:Key")));
        Assert.Throws<ArgumentException>(() => w.Drafts.MoveToKeyVault(draft, new DraftKey(ConfigScope.Service, "Missing")));
    }

    [Fact]
    public async Task Revert_DropsPendingSecret()
    {
        var (w, draft) = await StartedAsync();
        var key = new DraftKey(ConfigScope.Service, "Api:Key");

        var reverted = w.Drafts.Revert(w.Drafts.SetSecretValue(draft, key, new SecretString(SyncWorld.Sentinel)), key);

        Assert.Empty(reverted.Secrets);
        Assert.Empty(w.Drafts.PendingKeys(reverted));
    }

    [Fact]
    public async Task ToPushSource_InjectsSecretValuesAndBaseJson()
    {
        var (w, draft) = await StartedAsync();
        var withSecret = w.Drafts.SetSecretValue(draft, new DraftKey(ConfigScope.Service, "Api:Token"), new SecretString(SyncWorld.Sentinel));

        var source = Assert.IsType<PushSource.EditedJson>(w.Drafts.ToPushSource(withSecret));

        Assert.Contains(SyncWorld.Sentinel, source.Json);
        Assert.Equal(draft.BaseJson, source.BaseJson);
        Assert.DoesNotContain(SyncWorld.Sentinel, source.ToString());
        AssertNoSentinel(withSecret);
    }

    [Fact]
    public async Task Validate_ReportsKeyOrPositionNeverValues()
    {
        var (w, draft) = await StartedAsync();

        Assert.Empty(w.Drafts.Validate(draft));

        var syntax = Assert.Single(w.Drafts.Validate(w.Drafts.ReplaceJson(draft, "{\n  \"A\": \"x\",\n  \"B\": }")));
        Assert.Equal((3, true), (syntax.Line, syntax.Column > 0));

        var duplicate = Assert.Single(w.Drafts.Validate(w.Drafts.ReplaceJson(draft, """{"Db":"x","Db":"y"}""")));
        Assert.Equal("Db", duplicate.Key);

        var colon = Assert.Single(w.Drafts.Validate(w.Drafts.ReplaceJson(draft, """{"Db:Host":"v-secret-ish"}""")));
        Assert.Null(colon.Key);
        Assert.DoesNotContain("v-secret-ish", colon.Message);

        var nullValue = Assert.Single(w.Drafts.Validate(w.Drafts.ReplaceJson(draft, """{"Db":{"Host":null}}""")));
        Assert.Equal("Db:Host", nullValue.Key);
    }

    [Fact]
    public async Task ReplaceJson_KeepsMatchingSecretsAndDropsStaleOnes()
    {
        var (w, draft) = await StartedAsync();
        var token = new DraftKey(ConfigScope.Service, "Api:Token");
        var other = new DraftKey(ConfigScope.Service, "Api:Other");
        var withSecrets = w.Drafts.SetSecretValue(w.Drafts.SetSecretValue(draft, token, new SecretString(SyncWorld.Sentinel)), other, new SecretString(SyncWorld.Sentinel));

        var renamed = w.Drafts.ReplaceJson(withSecrets, withSecrets.Json.Replace("crm--Api--Other", "crm--renamed", StringComparison.Ordinal));
        var plain = w.Drafts.ReplaceJson(withSecrets, """{"Api":{"Token":{"@kv":"crm--Api--Token"},"Other":"plain"}}""");
        var unchanged = w.Drafts.ReplaceJson(withSecrets, withSecrets.Json);

        Assert.Equal([token], plain.Secrets.Keys);
        Assert.Equal([token], renamed.Secrets.Keys);
        Assert.Equal(2, unchanged.Secrets.Count);
    }

    [Fact]
    public async Task ReplaceJson_InvalidText_KeepsSecrets()
    {
        var (w, draft) = await StartedAsync();
        var withSecret = w.Drafts.SetSecretValue(draft, new DraftKey(ConfigScope.Service, "Api:Token"), new SecretString(SyncWorld.Sentinel));

        var broken = w.Drafts.ReplaceJson(withSecret, "{ \"Api\": ");

        Assert.Single(broken.Secrets);
        Assert.NotEmpty(w.Drafts.Validate(broken));
        Assert.Equal([new DraftKey(ConfigScope.Service, "Api:Token")], w.Drafts.PendingKeys(broken));
    }

    [Fact]
    public async Task ReplaceJson_TypedSecretValue_MovesIntoSecretsAndOutOfJson()
    {
        var (w, draft) = await StartedAsync();
        var text = """{"Api":{"Token":{"@kv":"crm--Api--Token","value":"SENTINEL"}},"@global":{"G":{"@kv":"global--G","value":"g"}}}"""
            .Replace("SENTINEL", SyncWorld.Sentinel, StringComparison.Ordinal);

        var replaced = w.Drafts.ReplaceJson(draft, text);

        Assert.Equal(SyncWorld.Sentinel, replaced.Secrets[new DraftKey(ConfigScope.Service, "Api:Token")].Value.Reveal());
        Assert.Equal("g", replaced.Secrets[new DraftKey(ConfigScope.Global, "G")].Value.Reveal());
        AssertNoSentinel(replaced);
        Assert.Contains("crm--Api--Token", replaced.Json);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepRemoteAndKeepLocal_RemoveTheConflict()
    {
        var (w, draft) = await StartedAsync();
        var mine = w.Drafts.SetValue(draft, Host, "mine");
        w.Seed("Db:Host", "theirs");
        Assert.Single((await w.Workflow.PreviewDraftAsync(mine, w.Ct)).Conflicts);

        var keepRemote = await w.Drafts.ResolveConflictAsync(mine, "Db:Host", ConflictChoice.KeepRemote, w.Ct);
        var keepLocal = await w.Drafts.ResolveConflictAsync(mine, "Db:Host", ConflictChoice.KeepLocal, w.Ct);

        var remotePreview = await w.Workflow.PreviewDraftAsync(keepRemote, w.Ct);
        Assert.Empty(remotePreview.Conflicts);
        Assert.Empty(remotePreview.Changes);
        var localPreview = await w.Workflow.PreviewDraftAsync(keepLocal, w.Ct);
        Assert.Empty(localPreview.Conflicts);
        var row = Assert.Single(localPreview.Changes);
        Assert.Equal((ChangeKind.Update, "theirs", "mine"), (row.Kind, row.Before, row.After));
    }

    [Fact]
    public async Task PreviewDraftAsync_AnotherWriterChangesUntouchedKey_IsNotReverted()
    {
        var (w, draft) = await StartedAsync();
        var edited = w.Drafts.SetValue(draft, Host, "mine");
        w.Seed("Db:Port", "9999");

        var preview = await w.Workflow.PreviewDraftAsync(edited, w.Ct);
        await w.Workflow.ApplyAsync(preview, new ApplyConfirmation(null, false), w.Ct);

        Assert.Empty(preview.Conflicts);
        Assert.Equal(["Db:Host"], preview.Changes.Select(c => c.Key));
        Assert.Equal("mine", w.Remote("Db:Host"));
        Assert.Equal("9999", w.Remote("Db:Port"));
    }

    [Fact]
    public async Task ApplyAsync_DraftSecret_CreatesSecretInVault()
    {
        var (w, draft) = await StartedAsync();
        var withSecret = w.Drafts.SetSecretValue(draft, new DraftKey(ConfigScope.Service, "Api:Token"), new SecretString(SyncWorld.Sentinel));

        var preview = await w.Workflow.PreviewDraftAsync(withSecret, w.Ct);
        await w.Workflow.ApplyAsync(preview, new ApplyConfirmation(null, false), w.Ct);

        Assert.Equal(SyncWorld.Sentinel, w.World.Secrets.Value(new Uri($"{w.Vault}/secrets/crm--Api--Token")));
    }

    private static void AssertNoSentinel(RemoteDraft draft)
    {
        Assert.DoesNotContain(SyncWorld.Sentinel, draft.Json);
        Assert.DoesNotContain(SyncWorld.Sentinel, draft.BaseJson);
        Assert.DoesNotContain(SyncWorld.Sentinel, draft.ToString());
        Assert.DoesNotContain(SyncWorld.Sentinel, JsonSerializer.Serialize(draft.Secrets.ToDictionary(s => s.Key.Key, s => s.Value)));
    }
}
