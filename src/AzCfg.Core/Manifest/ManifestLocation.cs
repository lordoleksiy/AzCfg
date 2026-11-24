namespace AzCfg.Core.Manifest;

/// <summary>A manifest found by <c>IProjectManifestStore.FindAsync</c> and the full path of its <c>azcfg.json</c>.</summary>
public sealed record ManifestLocation(ProjectManifest Manifest, string FilePath);
