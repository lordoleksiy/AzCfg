using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>Reads one secret value on explicit request (AD-6 (2)); the caller holds it only while it is shown.</summary>
internal sealed class SecretRevealService(ISecretStore secretStore, LocalTargetAccess access) : ISecretRevealService
{
    public async Task<SecretString> RevealRemoteAsync(Uri secretUri, CancellationToken ct) =>
        new(await secretStore.GetAsync(secretUri, ct));

    /// <summary>The key's value from the secrets target, else from the values target; null when the key is in neither.</summary>
    public async Task<SecretString?> RevealLocalAsync(ProjectBinding binding, string key, CancellationToken ct)
    {
        var values = await access.ReadAllAsync(binding, ct);
        var found = values.Where(v => v.Value.Key == key).OrderByDescending(v => v.Source == binding.SecretsTarget).FirstOrDefault();
        return found is null ? null : new SecretString(found.Value.Value);
    }
}
