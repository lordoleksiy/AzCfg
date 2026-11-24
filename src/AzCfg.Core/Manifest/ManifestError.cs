namespace AzCfg.Core.Manifest;

/// <summary>
/// One manifest problem. <see cref="Field"/> is the JSON path of the offending field (e.g. <c>values.project</c>,
/// <c>vaults.dev</c>), or <c>null</c> when the whole document is at fault. Messages never contain field values.
/// </summary>
public sealed record ManifestError(ManifestErrorKind Kind, string? Field, string Message);
