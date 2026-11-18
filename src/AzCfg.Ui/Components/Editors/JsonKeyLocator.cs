using System.Text;
using System.Text.Json;

namespace AzCfg.Ui.Components.Editors;

/// <summary>Finds the property of a <c>A:B:C</c> key path in nested JSON text; used to reveal a key and to place markers.</summary>
public static class JsonKeyLocator
{
    public readonly record struct Location(int Line, int Column);

    /// <summary>
    /// 1-based position of the property name token. Looks under the root first, then under <c>@global</c>.
    /// Invalid JSON or an unknown key → null.
    /// </summary>
    public static Location? Find(string text, string key)
    {
        var segments = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? null : FindPath(text, segments) ?? FindPath(text, ["@global", .. segments]);
    }

    private static Location? FindPath(string text, string[] segments)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var names = new List<string>();
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var depth = reader.CurrentDepth;
                names.RemoveRange(depth - 1, names.Count - (depth - 1));
                names.Add(reader.GetString()!);
                if (depth == segments.Length && names.SequenceEqual(segments))
                {
                    return ToLocation(bytes, (int)reader.TokenStartIndex);
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static Location ToLocation(byte[] bytes, int offset)
    {
        var prefix = Encoding.UTF8.GetString(bytes, 0, offset);
        var line = prefix.Count(c => c == '\n') + 1;
        return new Location(line, prefix.Length - prefix.LastIndexOf('\n'));
    }
}
