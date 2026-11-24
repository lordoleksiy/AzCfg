namespace AzCfg.Core.Manifest;

/// <summary>
/// A local target in the manifest: <see cref="Target"/> is a <c>config project.*</c> target name (<c>user-secrets</c>,
/// <c>appsettings-local</c>, <c>local-settings</c>); <see cref="Project"/> is the <c>.csproj</c> (or its folder) relative
/// to the manifest folder.
/// </summary>
public sealed record ManifestTarget(string Target, string Project);
