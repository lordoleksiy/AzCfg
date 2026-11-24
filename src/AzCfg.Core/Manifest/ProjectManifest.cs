namespace AzCfg.Core.Manifest;

/// <summary>
/// The shared, git-tracked project configuration <c>azcfg.json</c> (ADR 0012, amendment 2, schema v1). It holds names
/// and paths only — never a value, plain or secret. <see cref="Vaults"/> maps an environment name to its Key Vault URI.
/// Use <see cref="ManifestSerializer"/> to read/write it and <see cref="ManifestValidator"/> to check it.
/// </summary>
public sealed record ProjectManifest(
    int SchemaVersion,
    string Service,
    ManifestTarget Values,
    ManifestTarget Secrets,
    IReadOnlyDictionary<string, Uri>? Vaults);
