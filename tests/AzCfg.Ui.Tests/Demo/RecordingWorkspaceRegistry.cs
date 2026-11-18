using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Demo;

public sealed class RecordingWorkspaceRegistry : IWorkspaceRegistry
{
    public List<string> Touched { get; } = [];

    public Task<IReadOnlyList<WorkspaceEntry>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<WorkspaceEntry>>([]);

    public Task AddOrTouchAsync(string rootPath, CancellationToken ct)
    {
        Touched.Add(rootPath);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string rootPath, CancellationToken ct) => Task.CompletedTask;

    public Task SetPinnedAsync(string rootPath, bool pinned, CancellationToken ct) => Task.CompletedTask;
}
