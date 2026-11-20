using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Application.State;

/// <summary>The opened workspace of one circuit: tree, environments (null until loaded; Profile null → first run), selection.</summary>
public sealed class WorkspaceState
{
    public WorkspaceTree? Tree { get; private set; }

    public EnvironmentsSnapshot? Environments { get; private set; }

    public string? SelectedProjectId { get; private set; }

    public event Action? Changed;

    public void SetTree(WorkspaceTree? tree)
    {
        Tree = tree;
        Changed?.Invoke();
    }

    public void SetEnvironments(EnvironmentsSnapshot? environments)
    {
        Environments = environments;
        Changed?.Invoke();
    }

    public void SelectProject(string? projectId)
    {
        SelectedProjectId = projectId;
        Changed?.Invoke();
    }

    /// <summary>The node with <see cref="ProjectNode.Id"/> == <paramref name="projectId"/> (ordinal) in a solution or loose; null when absent.</summary>
    public ProjectNode? FindProject(string projectId) =>
        Tree is null ? null : Tree.Solutions.SelectMany(s => s.Projects).Concat(Tree.LooseProjects).FirstOrDefault(p => p.Id == projectId);

    /// <summary>Replaces every node of <paramref name="projectId"/> in <see cref="Tree"/> with one carrying <paramref name="binding"/>; no-op when absent.</summary>
    public void UpdateBinding(string projectId, ProjectBinding binding)
    {
        if (Tree is null || FindProject(projectId) is null)
        {
            return;
        }

        IReadOnlyList<ProjectNode> Replace(IReadOnlyList<ProjectNode> nodes) =>
            [.. nodes.Select(n => n.Id == projectId ? n with { Binding = binding } : n)];

        Tree = Tree with
        {
            Solutions = [.. Tree.Solutions.Select(s => s with { Projects = Replace(s.Projects) })],
            LooseProjects = Replace(Tree.LooseProjects),
        };
        Changed?.Invoke();
    }
}
