using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// A project of the workspace tree with its binding (null when unbound); <see cref="Id"/> is the .csproj path.
/// <see cref="ManifestPath"/> is the shared <c>azcfg.json</c> naming this project (ADR 0012 amendment 2), <c>null</c> when not
/// shared; <see cref="ManifestChange"/> is set when the binding differs from it (not applied until the developer says so);
/// <see cref="ManifestError"/> is why the manifest belonging to this project could not be read or applied — the node then keeps
/// the per-user binding, if any.
/// </summary>
public sealed record ProjectNode(
    WorkspaceProject Project,
    ProjectBinding? Binding,
    string? ManifestPath = null,
    string? ManifestError = null,
    ManifestChange? ManifestChange = null)
{
    public string Id => Project.Project.CsprojPath;

    public bool IsShared => ManifestPath is not null;
}
