using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

public interface IResourceDiscovery
{
    Task<IReadOnlyList<AzureResource>> FindAppConfigStoresAsync(CancellationToken ct);
    Task<IReadOnlyList<AzureResource>> FindKeyVaultsAsync(CancellationToken ct);
}
