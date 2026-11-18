namespace AzCfg.Ui.Application.Workspace;

/// <summary>Path comparison as in <c>JsonUserStateStore</c>: full path, ignoring case on Windows.</summary>
internal static class PathKeys
{
    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Full(string path) => Path.GetFullPath(path);

    public static bool Same(string left, string right) => Comparer.Equals(Full(left), Full(right));
}
