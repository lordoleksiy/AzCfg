using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

public interface IWorkspaceScanner
{
    /// <summary>
    /// Solutions (only .csproj entries, absolute paths), projects (via <see cref="IProjectDetector"/>) and well-known
    /// config files in each project folder. A broken solution file is skipped with a logged warning and never fails the scan.
    /// </summary>
    Task<WorkspaceScan> ScanAsync(string rootPath, CancellationToken ct);
}
