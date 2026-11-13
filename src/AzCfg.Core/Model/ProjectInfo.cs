namespace AzCfg.Core.Model;

public sealed record ProjectInfo(
    string RepoPath,
    string CsprojPath,
    string Name,
    bool IsAzureFunctions,
    string? UserSecretsId);
