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
    IReadOnlyDictionary<string, Uri>? Vaults)
{
    /// <summary>Value equality, with <see cref="Vaults"/> compared by content (keys ordinal, order ignored).</summary>
    public bool Equals(ProjectManifest? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Service == other.Service
        && Values == other.Values
        && Secrets == other.Secrets
        && VaultsEqual(Vaults, other.Vaults);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(Service);
        hash.Add(Values);
        hash.Add(Secrets);
        hash.Add(Vaults?.Count ?? -1);

        // Order-independent combination so equal content hashes equally.
        var vaults = 0;
        foreach (var (environment, uri) in Vaults ?? new Dictionary<string, Uri>())
        {
            vaults ^= HashCode.Combine(StringComparer.Ordinal.GetHashCode(environment), uri);
        }

        hash.Add(vaults);
        return hash.ToHashCode();
    }

    private static bool VaultsEqual(IReadOnlyDictionary<string, Uri>? a, IReadOnlyDictionary<string, Uri>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        foreach (var (environment, uri) in a)
        {
            if (!b.Any(kv => string.Equals(kv.Key, environment, StringComparison.Ordinal) && kv.Value == uri))
            {
                return false;
            }
        }

        return true;
    }
}
