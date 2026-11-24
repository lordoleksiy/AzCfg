using AzCfg.Core.UseCases;

namespace AzCfg.Core.Manifest;

/// <summary>
/// Semantic rules of a <see cref="ProjectManifest"/> (ADR 0012, amendment 2). Schema-level rules that need the raw JSON
/// (unknown, missing, duplicate fields, wrong types) are checked by <see cref="ManifestSerializer"/>, which also runs
/// these. The git safety of the secrets target (ADR 0006) depends on the working tree and is enforced by the local
/// target at write time; here a target must be one of the known <c>config project.*</c> names.
/// </summary>
public static class ManifestValidator
{
    /// <summary>The newest <c>schemaVersion</c> this azcfg reads and writes.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>The <c>target</c> names a manifest may use (as in <c>config project.values|secrets</c>).</summary>
    public static IReadOnlyList<string> KnownTargets { get; } = [.. ProjectLookup.KindNames.Select(k => k.Name)];

    /// <summary>Every problem found, in field order; empty when the manifest is valid. Messages never include values.</summary>
    public static IReadOnlyList<ManifestError> Validate(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<ManifestError>();

        if (manifest.SchemaVersion > SupportedSchemaVersion)
        {
            errors.Add(UnsupportedVersion(manifest.SchemaVersion));
        }
        else if (manifest.SchemaVersion < 1)
        {
            errors.Add(Schema("schemaVersion", "'schemaVersion' must be a positive integer."));
        }

        if (string.IsNullOrWhiteSpace(manifest.Service))
        {
            errors.Add(Schema("service", "'service' must not be empty."));
        }
        else if (manifest.Service.Contains(':', StringComparison.Ordinal))
        {
            errors.Add(Schema("service", "'service' must not contain ':'."));
        }

        ValidateTarget(errors, "values", manifest.Values);
        ValidateTarget(errors, "secrets", manifest.Secrets);

        foreach (var (environment, uri) in manifest.Vaults ?? new Dictionary<string, Uri>())
        {
            if (string.IsNullOrWhiteSpace(environment))
            {
                errors.Add(Schema("vaults", "'vaults' must not contain an empty environment name."));
            }
            else if (uri is not { IsAbsoluteUri: true } || uri.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add(Schema($"vaults.{environment}", $"'vaults.{environment}' must be an absolute https URI."));
            }
        }

        return errors;
    }

    internal static ManifestError UnsupportedVersion(int version) => new(
        ManifestErrorKind.UnsupportedVersion,
        "schemaVersion",
        $"'schemaVersion' {version} is newer than supported ({SupportedSchemaVersion}); update azcfg (dotnet tool update -g azcfg).");

    internal static ManifestError Schema(string field, string message) => new(ManifestErrorKind.Schema, field, message);

    private static void ValidateTarget(List<ManifestError> errors, string name, ManifestTarget? target)
    {
        if (target is null)
        {
            errors.Add(Schema(name, $"'{name}' is required."));
            return;
        }

        if (!KnownTargets.Contains(target.Target, StringComparer.Ordinal))
        {
            errors.Add(Schema($"{name}.target", $"'{name}.target' must be one of: {string.Join(", ", KnownTargets)}."));
        }

        if (!IsRelativeInside(target.Project))
        {
            errors.Add(Schema(
                $"{name}.project",
                $"'{name}.project' must be a relative path inside the manifest folder (no absolute path, no '..' above it)."));
        }
    }

    private static bool IsRelativeInside(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path[0] is '/' or '\\'
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
        {
            return false;
        }

        var depth = 0;
        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment == "..")
            {
                if (--depth < 0)
                {
                    return false;
                }
            }
            else if (segment is not ("" or "."))
            {
                depth++;
            }
        }

        return true;
    }
}
