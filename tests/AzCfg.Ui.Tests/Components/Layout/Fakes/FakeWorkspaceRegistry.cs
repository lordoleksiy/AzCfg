using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Components.Layout.Fakes;

internal sealed class FakeWorkspaceRegistry(params WorkspaceEntry[] entries) : IWorkspaceRegistry
{
    public Task<IReadOnlyList<WorkspaceEntry>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<WorkspaceEntry>>(entries);

    public Task AddOrTouchAsync(string rootPath, CancellationToken ct) => Task.CompletedTask;

    public Task RemoveAsync(string rootPath, CancellationToken ct) => Task.CompletedTask;

    public Task SetPinnedAsync(string rootPath, bool pinned, CancellationToken ct) => Task.CompletedTask;
}
