using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Tests.Application.Workspace.Fakes;

/// <summary>Returns a fixed scan for any root.</summary>
public sealed class FakeWorkspaceScanner(IReadOnlyList<SolutionInfo> solutions, IReadOnlyList<WorkspaceProject> projects) : IWorkspaceScanner
{
    public Task<WorkspaceScan> ScanAsync(string rootPath, CancellationToken ct) => Task.FromResult(new WorkspaceScan(rootPath, solutions, projects));
}
