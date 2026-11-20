using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Workspace;

public sealed record SolutionNode(SolutionInfo Solution, IReadOnlyList<ProjectNode> Projects);
