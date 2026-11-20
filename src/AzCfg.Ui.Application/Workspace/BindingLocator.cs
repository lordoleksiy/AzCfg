using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// Finds the binding of a project (G4): the binding keyed by the project's folder, else the nearest ancestor binding up to
/// the workspace root whose targets point at this very <c>.csproj</c>. An accepted binding is used as is.
/// </summary>
public sealed class BindingLocator(IUserStateStore state)
{
    public async Task<ProjectBinding?> FindAsync(string csprojPath, string workspaceRoot, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(PathKeys.Full(csprojPath));
        if (folder is null)
        {
            return null;
        }

        if (await state.LoadBindingAsync(folder, ct) is { } own)
        {
            return own;
        }

        var root = Path.TrimEndingDirectorySeparator(PathKeys.Full(workspaceRoot));
        for (var dir = Path.GetDirectoryName(folder); dir is not null && IsUnder(dir, root); dir = Path.GetDirectoryName(dir))
        {
            if (await state.LoadBindingAsync(dir, ct) is { } ancestor && Points(ancestor, csprojPath))
            {
                return ancestor;
            }
        }

        return null;
    }

    private static bool IsUnder(string dir, string root) =>
        PathKeys.Comparer.Equals(Path.TrimEndingDirectorySeparator(dir), root)
        || Path.TrimEndingDirectorySeparator(dir).StartsWith(root + Path.DirectorySeparatorChar, PathKeys.Comparison);

    private static bool Points(ProjectBinding binding, string csprojPath) =>
        PathKeys.Same(binding.ValuesTarget.ProjectPath, csprojPath) || PathKeys.Same(binding.SecretsTarget.ProjectPath, csprojPath);
}
