using System.Collections.Concurrent;
using AzCfg.Core.Abstractions;
using Azure;
using Azure.Security.KeyVault.Secrets;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// Reads and writes Key Vault secrets, keeping one <see cref="SecretClient"/> per vault.
/// <see cref="GetAsync"/> and <see cref="ExistsAsync"/> throw <see cref="ArgumentException"/> if the URI is not a secret identifier.
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

    /// <summary>
    /// Lists the secret's version <em>properties</em> (never a value). A versioned identifier exists when that version is listed and not disabled;
    /// a versionless one when its newest version (by creation time, all pages) is not disabled. A 404 means the secret does not exist.
    /// </summary>
    public async Task<bool> ExistsAsync(Uri secretUri, CancellationToken ct)
    {
        var id = new KeyVaultSecretIdentifier(secretUri);
        try
        {
            SecretProperties? newest = null;
            await foreach (var properties in Client(id.VaultUri).GetPropertiesOfSecretVersionsAsync(id.Name, ct))
            {
                if (id.Version is not null)
                {
                    if (string.Equals(properties.Version, id.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        return properties.Enabled != false;
                    }
                }
                else if (newest is null || (properties.CreatedOn ?? DateTimeOffset.MinValue) > (newest.CreatedOn ?? DateTimeOffset.MinValue))
                {
                    newest = properties;
                }
            }

            return newest is not null && newest.Enabled != false;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private SecretClient Client(Uri vaultUri) => _clients.GetOrAdd(vaultUri, clientFactory);
}
