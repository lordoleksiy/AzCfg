using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// A project of the workspace tree with its binding (null when unbound); <see cref="Id"/> is the .csproj path.
/// <see cref="ManifestPath"/> is the shared <c>azcfg.json</c> naming this project (ADR 0012 amendment 2), <c>null</c> when not
/// shared; <see cref="ManifestError"/> is why the manifest found for it could not be applied (unreadable, invalid, or naming
/// targets this project cannot use) — the node then keeps the per-user binding, if any.
/// </summary>
public sealed record ProjectNode(WorkspaceProject Project, ProjectBinding? Binding, string? ManifestPath = null, string? ManifestError = null)
{
    public string Id => Project.Project.CsprojPath;

    public bool IsShared => ManifestPath is not null;
}
