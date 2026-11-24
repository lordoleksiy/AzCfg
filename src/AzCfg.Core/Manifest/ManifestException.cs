namespace AzCfg.Core.Manifest;

/// <summary>
/// Thrown by <see cref="ManifestSerializer"/> when a manifest cannot be read or written. <see cref="Kind"/> is the
/// dominant category (<see cref="ManifestErrorKind.Unreadable"/> over <see cref="ManifestErrorKind.UnsupportedVersion"/>
/// over <see cref="ManifestErrorKind.Schema"/>); <see cref="Errors"/> lists every problem found. A file store attaches
/// the path with <see cref="WithFilePath"/>.
/// </summary>
public sealed class ManifestException : Exception
{
    public ManifestException(IReadOnlyList<ManifestError> errors, string? filePath = null, Exception? innerException = null)
        : base(Format(errors, filePath), innerException)
    {
        Errors = errors;
        FilePath = filePath;
        Kind = errors.Any(e => e.Kind == ManifestErrorKind.Unreadable) ? ManifestErrorKind.Unreadable
            : errors.Any(e => e.Kind == ManifestErrorKind.UnsupportedVersion) ? ManifestErrorKind.UnsupportedVersion
            : ManifestErrorKind.Schema;
    }

    public ManifestErrorKind Kind { get; }

    public IReadOnlyList<ManifestError> Errors { get; }

    /// <summary>The manifest file, when known.</summary>
    public string? FilePath { get; }

    /// <summary>The same errors attributed to <paramref name="filePath"/>; this exception becomes the inner one.</summary>
    public ManifestException WithFilePath(string filePath) => new(Errors, filePath, this);

    private static string Format(IReadOnlyList<ManifestError> errors, string? filePath)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentOutOfRangeException.ThrowIfZero(errors.Count);
        var head = filePath is null ? "Invalid azcfg manifest" : $"Invalid azcfg manifest '{filePath}'";
        return $"{head}: {string.Join("; ", errors.Select(e => e.Message))}";
    }
}
