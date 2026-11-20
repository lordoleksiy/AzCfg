namespace AzCfg.Core.Model;

/// <summary>
/// A well-known config file of a project. <see cref="EnvironmentName"/> only for
/// <see cref="ConfigFileKind.AppSettingsEnvironment"/> (<c>appsettings.{Env}.json</c>); <see cref="ConfigFileKind.UserSecrets"/>:
/// <see cref="Exists"/> is <c>false</c> when the file does not exist yet.
/// </summary>
public sealed record ConfigFileInfo(string Path, ConfigFileKind Kind, string? EnvironmentName, bool Exists);
