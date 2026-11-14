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

        var nodes = new List<(string Key, JsonNode Node)>();
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

            nodes.Add((value.Key, JsonValue.Create(value.Value)!));
        }

        return Build(nodes, "values");
    }

    /// <summary>
    /// Builds the edit JSON (architecture, "JSON-формат редагування") from merged entries: nesting by <c>:</c>,
    /// plain values as strings, Key Vault references as <c>{"@kv":"&lt;secret-name&gt;"}</c> (secret values are never
    /// emitted), global entries under <c>@global</c> (omitted when empty). Properties are ordered ordinally.
    /// Throws <see cref="ArgumentException"/> for null elements, invalid keys, or a key that is both a value and a parent.
    /// </summary>
    public static JsonObject ToEditJson(IEnumerable<ConfigEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var list = entries.ToList();
        if (list.Contains(null!))
        {
            throw new ArgumentException("Entries must not contain null.", nameof(entries));
        }

        var root = Build(list.Where(e => e.Scope == ConfigScope.Service).Select(ToNode), nameof(entries));
        var global = Build(list.Where(e => e.Scope == ConfigScope.Global).Select(ToNode), nameof(entries));
        if (global.Count > 0)
        {
            root["@global"] = global;
        }

        return root;
    }

    private static (string Key, JsonNode Node) ToNode(ConfigEntry entry) => (entry.Key, entry.Value switch
    {
        ConfigValue.Plain p => JsonValue.Create(p.Value)!,
        ConfigValue.KeyVaultRef r => new JsonObject { ["@kv"] = SecretName(r.SecretUri) },
        ConfigValue.ResolvedSecret r => new JsonObject { ["@kv"] = SecretName(r.SecretUri) },
        _ => throw new ArgumentException($"Unsupported value of '{entry.Key}'.", "entries"),
    });

    private static string SecretName(Uri secretUri) => secretUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];

    private static JsonObject Build(IEnumerable<(string Key, JsonNode Node)> nodes, string paramName)
    {
        var paths = nodes.Select(n => (Segments: KeyMapper.ToLocalJsonPath(n.Key), n.Key, n.Node)).ToList();
        paths.Sort((x, y) => CompareSegments(x.Segments, y.Segments));

        var root = new JsonObject();
        foreach (var (segments, key, node) in paths)
        {
            Add(root, segments, key, node, paramName);
        }

        return root;
    }

    private static void Add(JsonObject root, IReadOnlyList<string> segments, string key, JsonNode leafNode, string paramName)
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
            else if (child is not JsonObject parent || parent.ContainsKey("@kv"))
            {
                throw Conflict(key, paramName, KeyMapper.FromLocalJsonPath(segments.Take(i + 1).ToList()));
            }

            current = (JsonObject)child;
        }

        var leaf = segments[^1];
        if (current.TryGetPropertyValue(leaf, out var existing))
        {
            throw existing is JsonObject
                ? Conflict(key, paramName, key)
                : new ArgumentException($"Duplicate key '{key}'.", paramName);
        }

        current[leaf] = leafNode;
    }

    private static ArgumentException Conflict(string key, string paramName, string valueKey) =>
        new($"Key '{key}' conflicts with '{valueKey}': a key cannot be both a value and a parent of other keys.",
            paramName);

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
