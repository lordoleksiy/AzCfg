using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Fakes;

public sealed class StubResourceDiscovery(params AzureResource[] stores) : IResourceDiscovery
{
    public Task<IReadOnlyList<AzureResource>> FindAppConfigStoresAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AzureResource>>(stores);
    public Task<IReadOnlyList<AzureResource>> FindKeyVaultsAsync(CancellationToken ct) => throw new NotSupportedException();
}
