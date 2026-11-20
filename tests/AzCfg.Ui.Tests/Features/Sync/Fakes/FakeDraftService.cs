using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Features.Sync.Fakes;

/// <summary>Only conflict resolution and pending keys are used by the Sync views.</summary>
internal sealed class FakeDraftService : IDraftService
{
    public List<(string Key, ConflictChoice Choice)> Resolved { get; } = [];

    public Task<RemoteDraft> ResolveConflictAsync(RemoteDraft draft, string key, ConflictChoice choice, CancellationToken ct)
    {
        Resolved.Add((key, choice));
        return Task.FromResult(draft with { Json = "{\"resolved\":true}" });
    }

    public IReadOnlyList<DraftKey> PendingKeys(RemoteDraft draft) => [new DraftKey(ConfigScope.Service, "A")];

    public Task<RemoteDraft> StartAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();

    public RemoteDraft SetValue(RemoteDraft draft, DraftKey key, string value) => throw new NotSupportedException();

    public RemoteDraft SetSecretValue(RemoteDraft draft, DraftKey key, SecretString value) => throw new NotSupportedException();

    public RemoteDraft MoveToKeyVault(RemoteDraft draft, DraftKey key) => throw new NotSupportedException();

    public RemoteDraft Remove(RemoteDraft draft, DraftKey key) => throw new NotSupportedException();

    public RemoteDraft Revert(RemoteDraft draft, DraftKey key) => throw new NotSupportedException();

    public RemoteDraft ReplaceJson(RemoteDraft draft, string json) => throw new NotSupportedException();

    public IReadOnlyList<JsonDiagnostic> Validate(RemoteDraft draft) => throw new NotSupportedException();

    public PushSource ToPushSource(RemoteDraft draft) => throw new NotSupportedException();
}
