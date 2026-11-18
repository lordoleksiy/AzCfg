using AzCfg.Core.Abstractions;

namespace AzCfg.Ui.Tests.Application.Configuration;

/// <summary>Counts <see cref="GetAsync"/> calls of the wrapped store.</summary>
internal sealed class CountingSecretStore(ISecretStore inner) : ISecretStore
{
    public int Gets { get; private set; }

    public Task<string> GetAsync(Uri secretUri, CancellationToken ct)
    {
        Gets++;
        return inner.GetAsync(secretUri, ct);
    }

    public Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct) => inner.SetAsync(vaultUri, name, value, ct);
}
