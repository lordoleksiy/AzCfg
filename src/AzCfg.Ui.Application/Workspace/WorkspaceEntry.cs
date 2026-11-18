namespace AzCfg.Ui.Application.Workspace;

public sealed record WorkspaceEntry(string RootPath, string DisplayName, DateTimeOffset LastOpened, bool Pinned);
