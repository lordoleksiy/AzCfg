namespace AzCfg.Core.Model;

/// <summary>
/// An Azure App Configuration environment (ADR 0002, ADR 0008). In the saved profile <see cref="Label"/> is the
/// configured label (store mode: label for new keys; label mode: the environment's label). After
/// <see cref="Abstractions.IEnvironmentResolver"/> it is the read selector: <c>null</c> reads every label, otherwise
/// keys with that label plus unlabeled keys as fallback. <see cref="WriteLabel"/> is the label given to new keys.
/// <see cref="IsProtected"/> gates the "type the environment name to confirm" rule on apply; by default it
/// is derived with <see cref="IsProtectedByName"/>, but the user may override it during <c>init</c>.
/// </summary>
public sealed record ConfigEnvironment(string Name, Uri StoreEndpoint, string? Label, bool IsProtected, string? WriteLabel = null)
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