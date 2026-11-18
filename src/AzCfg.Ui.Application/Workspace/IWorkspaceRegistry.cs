namespace AzCfg.Ui.Application.Workspace;

/// <summary>Opened folders, stored in <c>&lt;UiStatePaths.Root&gt;/ui/workspaces.json</c>.</summary>
public interface IWorkspaceRegistry
{
    /// <summary>Pinned first, then <see cref="WorkspaceEntry.LastOpened"/> descending.</summary>
    Task<IReadOnlyList<WorkspaceEntry>> ListAsync(CancellationToken ct);

    Task AddOrTouchAsync(string rootPath, CancellationToken ct);

    /// <summary>Never touches <c>projects.json</c>.</summary>
    Task RemoveAsync(string rootPath, CancellationToken ct);

    Task SetPinnedAsync(string rootPath, bool pinned, CancellationToken ct);
}
