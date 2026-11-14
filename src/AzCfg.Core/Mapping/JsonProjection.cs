using System.Text.Json.Nodes;
using AzCfg.Core.Model;

namespace AzCfg.Core.Mapping;

/// <summary>
/// Projects flat configuration paths onto nested JSON (ADR 0004, <see cref="LocalFileFormat.NestedJson"/>):
/// <c>A:B:C = v</c> → <c>{"A":{"B":{"C":"v"}}}</c>. Keys carry no scope prefix; values are always JSON strings.
/// </summary>
public static class JsonProjection
{
    /// <summary>
    /// Builds a nested JSON object from flat values. Properties are ordered ordinally at every level,
    /// so the output is deterministic regardless of input order. Keys are compared ordinally (case-sensitive).
    /// Throws <see cref="ArgumentException"/> for invalid keys, duplicate keys, or a key that is both
    /// a value and a parent of another key (<c>A</c> and <c>A:B</c>). Values never appear in error messages.
    /// </summary>
    public static JsonObject ToNestedJson(IEnumerable<LocalValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var paths = new List<(IReadOnlyList<string> Segments, LocalValue Value)>();
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("Values must not contain null.", nameof(values));
            }

            if (value.Value is null)
            {
                throw new ArgumentException($"Value of '{value.Key}' must not be null.", nameof(values));
            }

            paths.Add((KeyMapper.ToLocalJsonPath(value.Key), value));
        }

        paths.Sort((x, y) => CompareSegments(x.Segments, y.Segments));

        var root = new JsonObject();
        foreach (var (segments, value) in paths)
        {
            Add(root, segments, value);
        }

        return root;
    }

    private static void Add(JsonObject root, IReadOnlyList<string> segments, LocalValue value)
    {
        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var segment = segments[i];
            if (!current.TryGetPropertyValue(segment, out var child))
            {
                child = new JsonObject();
                current[segment] = child;
            }
            else if (child is not JsonObject)
            {
                throw Conflict(value.Key, KeyMapper.FromLocalJsonPath(segments.Take(i + 1).ToList()));
            }

            current = (JsonObject)child;
        }

        var leaf = segments[^1];
        if (current.TryGetPropertyValue(leaf, out var existing))
        {
            throw existing is JsonObject
                ? Conflict(value.Key, value.Key)
                : new ArgumentException($"Duplicate key '{value.Key}'.", "values");
        }

        current[leaf] = JsonValue.Create(value.Value);
    }

    private static ArgumentException Conflict(string key, string valueKey) =>
        new($"Key '{key}' conflicts with '{valueKey}': a key cannot be both a value and a parent of other keys.",
            "values");

    private static int CompareSegments(IReadOnlyList<string> x, IReadOnlyList<string> y)
    {
        var count = Math.Min(x.Count, y.Count);
        for (var i = 0; i < count; i++)
        {
            var result = string.CompareOrdinal(x[i], y[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return x.Count.CompareTo(y.Count);
    }
}
