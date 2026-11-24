namespace AzCfg.Core.UseCases;

/// <summary>How a Key Vault URI is stored in a binding (ADR 0014): the vault root, compared and saved the same way everywhere.</summary>
internal static class VaultUri
{
    /// <summary>
    /// A vault is its root URI: https, absolute, no path (or "/"), query, fragment or user info; returned without the trailing slash.
    /// Anything else throws <see cref="ArgumentException"/> naming <paramref name="key"/>.
    /// </summary>
    public static Uri Normalize(string key, string value)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.AbsolutePath == "/"
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && uri.UserInfo.Length == 0)
        {
            return new(uri.GetLeftPart(UriPartial.Authority));
        }

        throw new ArgumentException(
            $"Invalid value '{value}' for '{key}': expected an absolute https Key Vault root URI without path, query or fragment, e.g. https://<name>.vault.azure.net.");
    }
}
