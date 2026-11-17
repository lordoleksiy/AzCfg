namespace AzCfg.Core.Model;

/// <summary>
/// User-facing names of <see cref="EnvironmentMode"/> used by <c>azcfg init --mode</c> and <c>azcfg config</c> (ADR 0008):
/// <c>stores</c> = <see cref="EnvironmentMode.StorePerEnvironment"/>, <c>labels</c> = <see cref="EnvironmentMode.LabelPerEnvironment"/>.
/// </summary>
public static class EnvironmentModeNames
{
    public const string Stores = "stores";
    public const string Labels = "labels";

    /// <summary>Returns <c>stores</c> or <c>labels</c>.</summary>
    public static string Format(EnvironmentMode mode) => mode == EnvironmentMode.LabelPerEnvironment ? Labels : Stores;

    /// <summary>Parses <c>stores</c> / <c>labels</c> (case-insensitive, surrounding whitespace ignored).</summary>
    public static bool TryParse(string? value, out EnvironmentMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case Stores:
                mode = EnvironmentMode.StorePerEnvironment;
                return true;
            case Labels:
                mode = EnvironmentMode.LabelPerEnvironment;
                return true;
            default:
                mode = default;
                return false;
        }
    }
}
