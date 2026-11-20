namespace AzCfg.Core.Abstractions;

public interface ISecretStore
{
    Task<string> GetAsync(Uri secretUri, CancellationToken ct);
    Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct);

    /// <summary>Whether the secret exists; reads only its properties, never the value. A missing secret is <c>false</c>; other failures (e.g. no permission) throw.</summary>
    Task<bool> ExistsAsync(Uri secretUri, CancellationToken ct);
}
