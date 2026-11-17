using System.Collections.Concurrent;
using AzCfg.Core.Abstractions;
using Azure.Security.KeyVault.Secrets;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// Reads and writes Key Vault secrets, keeping one <see cref="SecretClient"/> per vault.
/// <see cref="GetAsync"/> throws <see cref="ArgumentException"/> if the URI is not a secret identifier.
/// </summary>
public sealed class KeyVaultSecretStore(Func<Uri, SecretClient> clientFactory) : ISecretStore
{
    private readonly ConcurrentDictionary<Uri, SecretClient> _clients = new();

    public async Task<string> GetAsync(Uri secretUri, CancellationToken ct)
    {
        var id = new KeyVaultSecretIdentifier(secretUri);
        return (await Client(id.VaultUri).GetSecretAsync(id.Name, id.Version, ct)).Value.Value;
    }

    public async Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct) =>
        (await Client(vaultUri).SetSecretAsync(name, value, ct)).Value.Id;

    private SecretClient Client(Uri vaultUri) => _clients.GetOrAdd(vaultUri, clientFactory);
}
