using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Workspace.Fakes;

internal sealed class FakeWorkspaceRegistry(params WorkspaceEntry[] entries) : IWorkspaceRegistry
{
    private readonly List<WorkspaceEntry> _entries = [.. entries];

    public Task<IReadOnlyList<WorkspaceEntry>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<WorkspaceEntry>>([.. _entries]);

    public Task AddOrTouchAsync(string rootPath, CancellationToken ct) => Task.CompletedTask;

    public Task RemoveAsync(string rootPath, CancellationToken ct)
    {
        _entries.RemoveAll(e => e.RootPath == rootPath);
        return Task.CompletedTask;
    }

    public Task SetPinnedAsync(string rootPath, bool pinned, CancellationToken ct)
    {
        var index = _entries.FindIndex(e => e.RootPath == rootPath);
        _entries[index] = _entries[index] with { Pinned = pinned };
        return Task.CompletedTask;
    }
}
