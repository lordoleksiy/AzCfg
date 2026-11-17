using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>Returns the App Configuration stores and Key Vaults added by the scenario (no Azure Resource Graph).</summary>
public sealed class FixedResourceDiscovery : IResourceDiscovery
{
    private readonly List<AzureResource> _stores = [];
    private readonly List<AzureResource> _vaults = [];

    public void AddStore(string name, string endpoint, IReadOnlyDictionary<string, string>? tags = null) =>
        _stores.Add(Resource("Microsoft.AppConfiguration/configurationStores", name, endpoint, tags));

    public void AddVault(string name, string endpoint) => _vaults.Add(Resource("Microsoft.KeyVault/vaults", name, endpoint, null));

    public Task<IReadOnlyList<AzureResource>> FindAppConfigStoresAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AzureResource>>([.. _stores]);

    public Task<IReadOnlyList<AzureResource>> FindKeyVaultsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AzureResource>>([.. _vaults]);

    private static AzureResource Resource(string type, string name, string endpoint, IReadOnlyDictionary<string, string>? tags) =>
        new($"/subscriptions/sub/resourceGroups/rg/providers/{type}/{name}", name, new Uri(endpoint), "sub", "rg", tags ?? new Dictionary<string, string>());
}
