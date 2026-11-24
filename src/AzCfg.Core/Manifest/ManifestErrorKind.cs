namespace AzCfg.Core.Manifest;

/// <summary>Why a manifest was rejected; the three cases of <c>HealthCheckKind.ManifestInvalid</c>.</summary>
public enum ManifestErrorKind
{
    /// <summary>The file is not readable JSON (syntax error, empty file).</summary>
    Unreadable,

    /// <summary>The JSON does not match the schema: unknown/missing/duplicate field, wrong type or an invalid value.</summary>
    Schema,

    /// <summary>The <c>schemaVersion</c> is newer than this azcfg supports.</summary>
    UnsupportedVersion,
}
