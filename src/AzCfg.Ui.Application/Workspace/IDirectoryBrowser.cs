namespace AzCfg.Ui.Application.Workspace;

public interface IDirectoryBrowser
{
    /// <summary>Folders only, no hidden ones; a folder without access lists as empty.</summary>
    Task<DirectoryListing> ListAsync(string? path, CancellationToken ct);

    IReadOnlyList<string> Complete(string partialPath, int max = 20);
}
