namespace AzCfg.Core.Model;

/// <param name="IsLibrary">
/// <c>true</c> when the project is not an application (class library or test project), so app-only local targets do not apply.
/// </param>
public sealed record ProjectInfo(
    string RepoPath,
    string CsprojPath,
    string Name,
    bool IsAzureFunctions,
    string? UserSecretsId,
    bool IsLibrary = false);
