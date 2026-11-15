namespace AzCfg.Core.Model;

/// <summary>
/// A local file a developer has bound a project's values or secrets to, together with its serialization
/// format (decision 1). <see cref="ProjectPath"/> is the path to the project's <c>.csproj</c> file.
/// <see cref="LocalTargetKind.LocalSettings"/> requires <see cref="LocalFileFormat.FlatDictionary"/> —
/// nested JSON is forbidden for it, since Azure Functions Core Tools reads only a flat string dictionary
/// from <c>Values</c>.
/// </summary>
public sealed record LocalTargetBinding(LocalTargetKind Kind, string ProjectPath, LocalFileFormat Format)
{
    // Get-only properties: a `with` expression cannot bypass the validation below.
    public LocalTargetKind Kind { get; } = Kind;

    public string ProjectPath { get; } = Validate(ProjectPath, Kind, Format);

    public LocalFileFormat Format { get; } = Format;

    private static string Validate(string projectPath, LocalTargetKind kind, LocalFileFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        if (kind == LocalTargetKind.LocalSettings && format != LocalFileFormat.FlatDictionary)
        {
            throw new ArgumentException(
                $"{LocalTargetKind.LocalSettings} requires {LocalFileFormat.FlatDictionary} format.", nameof(format));
        }

        return projectPath;
    }
}
