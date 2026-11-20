using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Workspace;

/// <summary>A project of the workspace tree with its binding (null when unbound); <see cref="Id"/> is the .csproj path.</summary>
public sealed record ProjectNode(WorkspaceProject Project, ProjectBinding? Binding)
{
    public string Id => Project.Project.CsprojPath;
}
