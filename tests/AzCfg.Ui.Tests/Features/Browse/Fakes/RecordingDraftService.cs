using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Features.Browse.Fakes;

/// <summary>The real <see cref="DraftService"/> with a call log; secret values are never logged. <see cref="Diagnostics"/> overrides Validate.</summary>
internal sealed class RecordingDraftService(IDraftService inner) : IDraftService
{
    public List<string> Calls { get; } = [];

    public IReadOnlyList<JsonDiagnostic>? Diagnostics { get; set; }

    public Task<RemoteDraft> StartAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        Calls.Add($"Start {env.Name}");
        return inner.StartAsync(binding, env, ct);
    }

    public RemoteDraft SetValue(RemoteDraft draft, DraftKey key, string value)
    {
        Calls.Add($"SetValue {key.Scope}:{key.Key}={value}");
        return inner.SetValue(draft, key, value);
    }

    public RemoteDraft SetSecretValue(RemoteDraft draft, DraftKey key, SecretString value)
    {
        Calls.Add($"SetSecretValue {key.Scope}:{key.Key}");
        return inner.SetSecretValue(draft, key, value);
    }

    public RemoteDraft MoveToKeyVault(RemoteDraft draft, DraftKey key)
    {
        Calls.Add($"MoveToKeyVault {key.Scope}:{key.Key}");
        return inner.MoveToKeyVault(draft, key);
    }

    public RemoteDraft Remove(RemoteDraft draft, DraftKey key)
    {
        Calls.Add($"Remove {key.Scope}:{key.Key}");
        return inner.Remove(draft, key);
    }

    public RemoteDraft Revert(RemoteDraft draft, DraftKey key)
    {
        Calls.Add($"Revert {key.Scope}:{key.Key}");
        return inner.Revert(draft, key);
    }

    public RemoteDraft ReplaceJson(RemoteDraft draft, string json)
    {
        Calls.Add("ReplaceJson");
        return inner.ReplaceJson(draft, json);
    }

    public IReadOnlyList<JsonDiagnostic> Validate(RemoteDraft draft) => Diagnostics ?? inner.Validate(draft);

    public IReadOnlyList<DraftKey> PendingKeys(RemoteDraft draft) => inner.PendingKeys(draft);

    public PushSource ToPushSource(RemoteDraft draft) => inner.ToPushSource(draft);

    public Task<RemoteDraft> ResolveConflictAsync(RemoteDraft draft, string key, ConflictChoice choice, CancellationToken ct) =>
        inner.ResolveConflictAsync(draft, key, choice, ct);
}
