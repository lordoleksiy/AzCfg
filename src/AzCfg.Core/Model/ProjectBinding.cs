namespace AzCfg.Core.Model;

public sealed record ProjectBinding(
    string RepoPath,
    string Service,
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, Uri> VaultByEnvironment);
