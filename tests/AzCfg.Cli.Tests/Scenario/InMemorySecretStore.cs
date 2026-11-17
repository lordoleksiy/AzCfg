using AzCfg.Core.Abstractions;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// In-memory Key Vault: secrets keyed by <c>(vault, name)</c>. <see cref="GetAsync"/> accepts versioned and versionless
/// secret identifiers; <see cref="SetAsync"/> returns a versioned identifier like Key Vault does.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<(string Vault, string Name), string> _secrets = [];
    private int _versions;

    /// <summary>Seeds a secret and returns its versionless identifier (what an App Configuration reference holds).</summary>
    public Uri Add(string vault, string name, string value)
    {
        var vaultUri = new Uri(vault);
        _secrets[(Authority(vaultUri), name)] = value;
        return new Uri($"{Authority(vaultUri)}/secrets/{name}");
    }

    public string? Value(Uri secretUri) => _secrets.GetValueOrDefault(Parse(secretUri));

    public Task<string> GetAsync(Uri secretUri, CancellationToken ct) =>
        Task.FromResult(_secrets.TryGetValue(Parse(secretUri), out var value) ? value : throw new InvalidOperationException($"Secret '{secretUri}' not found."));

    public Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct)
    {
        _secrets[(Authority(vaultUri), name)] = value;
        return Task.FromResult(new Uri($"{Authority(vaultUri)}/secrets/{name}/v{++_versions}"));
    }

    private static (string Vault, string Name) Parse(Uri secretUri) =>
        (Authority(secretUri), secretUri.Segments.SkipWhile(s => s != "secrets/").Skip(1).First().TrimEnd('/'));

    private static string Authority(Uri uri) => uri.GetLeftPart(UriPartial.Authority);
}
