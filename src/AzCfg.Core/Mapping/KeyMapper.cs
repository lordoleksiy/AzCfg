using AzCfg.Core.Model;

namespace AzCfg.Core.Mapping;

/// <summary>
/// Key naming conventions (ADR 0004): flat scoped store keys, Key Vault secret names and nested local JSON paths.
/// </summary>
public static class KeyMapper
{
    private const string GlobalName = "global";
    private const string GlobalPrefix = GlobalName + Separator;
    private const string Separator = ":";
    private const string KeyVaultSeparator = "--";

    /// <summary>
    /// Builds an App Configuration key: <c>global:&lt;path&gt;</c> or <c>&lt;service&gt;:&lt;path&gt;</c>.
    /// For <see cref="ConfigScope.Global"/> the <paramref name="service"/> is ignored.
    /// When the store has a global scope (<paramref name="hasGlobalScope"/>, default) the service name
    /// <c>global</c> is reserved and rejected; a store without a global scope may use it as a normal service name.
    /// </summary>
    public static string ToStoreKey(ConfigScope scope, string service, string path, bool hasGlobalScope = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        switch (scope)
        {
            case ConfigScope.Global:
                if (!hasGlobalScope)
                {
                    throw new InvalidOperationException("The store has no global scope.");
                }

                return GlobalPrefix + path;

            case ConfigScope.Service:
                ArgumentException.ThrowIfNullOrWhiteSpace(service);
                if (service.Contains(Separator, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Service name must not contain '{Separator}'.", nameof(service));
                }

                if (hasGlobalScope && service == GlobalName)
                {
                    throw new ArgumentException($"'{GlobalName}' is reserved for the global scope.", nameof(service));
                }

                return service + Separator + path;

            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown config scope.");
        }
    }

    /// <summary>
    /// Parses an App Configuration key belonging to <paramref name="service"/> or to the global scope.
    /// Matching is ordinal (case-sensitive). Returns <c>false</c> with an empty <paramref name="path"/>
    /// for foreign, malformed or empty-remainder keys. When <paramref name="hasGlobalScope"/> is <c>false</c>,
    /// <c>global:</c> is not special: it matches only if <paramref name="service"/> is itself named <c>global</c>.
    /// </summary>
    public static bool TryParseStoreKey(
        string storeKey, string service, out ConfigScope scope, out string path, bool hasGlobalScope = true)
    {
        scope = default;
        path = string.Empty;

        if (string.IsNullOrEmpty(storeKey))
        {
            return false;
        }

        if (hasGlobalScope && storeKey.StartsWith(GlobalPrefix, StringComparison.Ordinal))
        {
            return TryTakeRemainder(storeKey, GlobalPrefix.Length, ConfigScope.Global, ref scope, ref path);
        }

        if (string.IsNullOrWhiteSpace(service) || service.Contains(Separator, StringComparison.Ordinal))
        {
            return false;
        }

        var servicePrefix = service + Separator;
        if (storeKey.StartsWith(servicePrefix, StringComparison.Ordinal))
        {
            return TryTakeRemainder(storeKey, servicePrefix.Length, ConfigScope.Service, ref scope, ref path);
        }

        return false;
    }

    /// <summary>Converts a configuration key to a Key Vault secret name (<c>:</c> → <c>--</c>).</summary>
    public static string ToKeyVaultSecretName(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Replace(Separator, KeyVaultSeparator, StringComparison.Ordinal);
    }

    /// <summary>
    /// Secret name of a Key Vault secret URI: the segment after <c>secrets/</c> (a version segment is ignored).
    /// Throws <see cref="ArgumentException"/> when the URI is relative or has no <c>secrets/&lt;name&gt;</c> segment.
    /// </summary>
    public static string SecretNameOf(Uri secretUri)
    {
        ArgumentNullException.ThrowIfNull(secretUri);
        var segments = secretUri.IsAbsoluteUri ? secretUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) : [];
        var index = Array.IndexOf(segments, "secrets");
        return index >= 0 && index + 1 < segments.Length
            ? segments[index + 1]
            : throw new ArgumentException("A Key Vault secret URI must contain a 'secrets/<name>' segment.", nameof(secretUri));
    }

    /// <summary>
    /// Splits a configuration path (no scope prefix) into nested JSON object names for local files:
    /// <c>Dataverse:ConnString</c> → <c>["Dataverse", "ConnString"]</c>. Empty segments are rejected.
    /// </summary>
    public static IReadOnlyList<string> ToLocalJsonPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = path.Split(Separator);
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Path must not contain empty segments.", nameof(path));
        }

        return segments;
    }

    /// <summary>Joins nested JSON object names from a local file into a configuration path (inverse of <see cref="ToLocalJsonPath"/>).</summary>
    public static string FromLocalJsonPath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0
            || segments.Any(s => string.IsNullOrWhiteSpace(s) || s.Contains(Separator, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"Segments must be non-empty and must not contain '{Separator}'.", nameof(segments));
        }

        return string.Join(Separator, segments);
    }

    private static bool TryTakeRemainder(
        string storeKey, int prefixLength, ConfigScope parsedScope, ref ConfigScope scope, ref string path)
    {
        if (storeKey.Length == prefixLength)
        {
            return false;
        }

        scope = parsedScope;
        path = storeKey[prefixLength..];
        return true;
    }
}
