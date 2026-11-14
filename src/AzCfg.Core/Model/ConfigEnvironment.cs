namespace AzCfg.Core.Model;

/// <summary>
/// An Azure App Configuration environment (ADR 0002: one store per environment for now; <see cref="Label"/>
/// is reserved for a future label-based strategy and is <c>null</c> under <c>SeparateStoresResolver</c>).
/// <see cref="IsProtected"/> gates the "type the environment name to confirm" rule on apply; by default it
/// is derived with <see cref="IsProtectedByName"/>, but the user may override it during <c>init</c>.
/// </summary>
public sealed record ConfigEnvironment(string Name, Uri StoreEndpoint, string? Label, bool IsProtected)
{
    private static readonly HashSet<string> ProtectedNames =
        new(new[] { "prod", "production", "prd" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Case-insensitive check whether <paramref name="name"/> looks like a protected environment name
    /// (<c>prod</c>, <c>production</c>, <c>prd</c>). A <c>null</c>, empty or whitespace-only name is not protected.
    /// </summary>
    public static bool IsProtectedByName(string name) =>
        !string.IsNullOrWhiteSpace(name) && ProtectedNames.Contains(name);
}
