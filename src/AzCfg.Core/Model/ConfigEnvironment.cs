namespace AzCfg.Core.Model;

public sealed record ConfigEnvironment(string Name, Uri StoreEndpoint, string? Label);
