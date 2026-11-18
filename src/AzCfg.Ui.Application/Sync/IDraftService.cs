using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Sync;

public interface IDraftService
{
    /// <summary>Export of the environment; <c>BaseJson == Json</c>.</summary>
    Task<RemoteDraft> StartAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);

    RemoteDraft SetValue(RemoteDraft draft, DraftKey key, string value);

    /// <summary>
    /// Secret name: the existing reference's name (Json, else BaseJson), else
    /// <c>KeyMapper.ToKeyVaultSecretName(KeyMapper.ToStoreKey(scope, service, key))</c>. Json gets <c>{"@kv": name}</c>.
    /// </summary>
    RemoteDraft SetSecretValue(RemoteDraft draft, DraftKey key, SecretString value);

    /// <summary>The plain value becomes the secret value.</summary>
    RemoteDraft MoveToKeyVault(RemoteDraft draft, DraftKey key);

    RemoteDraft Remove(RemoteDraft draft, DraftKey key);

    /// <summary><c>CopyKey(Base → Json)</c>; drops the key's pending secret.</summary>
    RemoteDraft Revert(RemoteDraft draft, DraftKey key);

    /// <summary>Monaco edits; keeps Secrets whose key still holds <c>{"@kv"}</c> with the same name.</summary>
    RemoteDraft ReplaceJson(RemoteDraft draft, string json);

    /// <summary><c>FromEditJson</c> on the injected text; no network; messages and keys only, never values.</summary>
    IReadOnlyList<JsonDiagnostic> Validate(RemoteDraft draft);

    /// <summary><c>EditJsonPatch.ChangedKeys(BaseJson, Json)</c> ∪ Secrets keys.</summary>
    IReadOnlyList<DraftKey> PendingKeys(RemoteDraft draft);

    /// <summary><c>EditedJson</c>(Json with secret values injected via <c>EditJsonPatch.SetSecretValue</c>, BaseJson).</summary>
    PushSource ToPushSource(RemoteDraft draft);

    /// <summary>Fresh export; KeepRemote: <c>CopyKey(fresh → Json)</c>; KeepLocal: <c>CopyKey(fresh → BaseJson)</c>.</summary>
    Task<RemoteDraft> ResolveConflictAsync(RemoteDraft draft, string key, ConflictChoice choice, CancellationToken ct);
}
