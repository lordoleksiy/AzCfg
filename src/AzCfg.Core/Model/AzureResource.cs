namespace AzCfg.Core.Model;

public sealed record AzureResource(
    string Id,
    string Name,
    Uri Endpoint,
    string SubscriptionId,
    string ResourceGroup,
    IReadOnlyDictionary<string, string> Tags);
