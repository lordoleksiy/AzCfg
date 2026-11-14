using System.Text.Json;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager.Resources;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// Finds App Configuration stores and Key Vaults visible to the current credential via Azure Resource Graph,
/// one KQL query per resource type. Throws on auth or query failures.
/// </summary>
public sealed class ResourceGraphDiscovery(ArmClient arm) : IResourceDiscovery
{
    private const string Query =
        "resources | where type =~ '{0}' | project id, name, subscriptionId, resourceGroup, tags, endpoint = tostring(properties.{1}) | order by id asc";

    public Task<IReadOnlyList<AzureResource>> FindAppConfigStoresAsync(CancellationToken ct) =>
        QueryAsync("microsoft.appconfiguration/configurationstores", "endpoint", ct);

    public Task<IReadOnlyList<AzureResource>> FindKeyVaultsAsync(CancellationToken ct) =>
        QueryAsync("microsoft.keyvault/vaults", "vaultUri", ct);

    internal static AzureResource MapRow(JsonElement row) => new(
        row.GetProperty("id").GetString()!,
        row.GetProperty("name").GetString()!,
        new Uri(row.GetProperty("endpoint").GetString()!),
        row.GetProperty("subscriptionId").GetString()!,
        row.GetProperty("resourceGroup").GetString()!,
        row.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object
            ? tags.EnumerateObject().ToDictionary(t => t.Name, t => t.Value.GetString() ?? "")
            : []);

    internal static IReadOnlyList<AzureResource> MapRows(JsonElement rows) =>
        [.. rows.EnumerateArray().Select(MapRow)];

    private async Task<IReadOnlyList<AzureResource>> QueryAsync(string type, string endpointProperty, CancellationToken ct)
    {
        TenantResource? tenant = null;
        await foreach (var t in arm.GetTenants().GetAllAsync(ct))
        {
            tenant = t;
            break;
        }

        var result = new List<AzureResource>();
        string? skipToken = null;

        do
        {
            var content = new ResourceQueryContent(string.Format(Query, type, endpointProperty))
            {
                Options = new ResourceQueryRequestOptions { ResultFormat = ResultFormat.ObjectArray, Top = 1000, SkipToken = skipToken },
            };
            var response = (await tenant!.GetResourcesAsync(content, ct)).Value;
            result.AddRange(MapRows(JsonDocument.Parse(response.Data).RootElement));
            skipToken = response.SkipToken;
        }
        while (skipToken is not null);

        return result;
    }
}
