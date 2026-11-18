namespace AzCfg.Ui.Application.Workspace;

/// <summary>Folders of <see cref="Path"/>; a <c>null</c> <see cref="Path"/> lists the drive/file-system roots.</summary>
public sealed record DirectoryListing(string? Path, string? Parent, IReadOnlyList<DirectoryItem> Items);
