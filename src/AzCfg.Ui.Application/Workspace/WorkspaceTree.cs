namespace AzCfg.Ui.Application.Workspace;

public sealed record WorkspaceTree(string RootPath, IReadOnlyList<SolutionNode> Solutions, IReadOnlyList<ProjectNode> LooseProjects);
