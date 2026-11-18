using AzCfg.Ui.Application.Sync;
using BlazorMonaco;
using BlazorMonaco.Editor;

namespace AzCfg.Ui.Components.Editors;

/// <summary>Maps <see cref="JsonDiagnostic"/>s to Monaco markers: explicit line/column first, then the key's position, then line 1.</summary>
public static class EditorMarkers
{
    public const string Owner = "azcfg";

    public static List<MarkerData> From(IReadOnlyList<JsonDiagnostic> diagnostics, string text)
    {
        var lines = text.Split('\n');
        return [.. diagnostics.Select(d => ToMarker(d, text, lines))];
    }

    private static MarkerData ToMarker(JsonDiagnostic diagnostic, string text, string[] lines)
    {
        var located = diagnostic.Key is null ? null : JsonKeyLocator.Find(text, diagnostic.Key);
        var line = Math.Clamp(diagnostic.Line ?? located?.Line ?? 1, 1, lines.Length);
        var column = Math.Max(diagnostic.Column ?? located?.Column ?? 1, 1);
        var endColumn = Math.Max(lines[line - 1].TrimEnd('\r').Length + 1, column + 1);
        return new MarkerData
        {
            Severity = MarkerSeverity.Error,
            Message = diagnostic.Message,
            Source = Owner,
            StartLineNumber = line,
            StartColumn = column,
            EndLineNumber = line,
            EndColumn = endColumn,
        };
    }
}
