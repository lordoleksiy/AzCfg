namespace AzCfg.Core.Abstractions;

public interface ISecretStore
{
    Task<string> GetAsync(Uri secretUri, CancellationToken ct);
    Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct);
}
