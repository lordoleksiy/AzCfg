namespace AzCfg.Core.Model;

/// <summary>
/// A project's remembered binding (decision 2): which service it reads, which local target receives
/// plain values and which receives secrets (may be the same target), and which vault to use per
/// environment when a new secret is created.
/// </summary>
public sealed record ProjectBinding(
    string RepoPath,
    string Service,
    LocalTargetBinding ValuesTarget,
    LocalTargetBinding SecretsTarget,
    IReadOnlyDictionary<string, Uri> VaultByEnvironment);
