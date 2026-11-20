namespace AzCfg.Core.Model;

/// <summary>A project found in a workspace with the well-known config files of its folder.</summary>
public sealed record WorkspaceProject(ProjectInfo Project, IReadOnlyList<ConfigFileInfo> ConfigFiles);
