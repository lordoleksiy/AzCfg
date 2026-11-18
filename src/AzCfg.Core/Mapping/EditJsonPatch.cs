using System.Text.Json;
using System.Text.Json.Nodes;
using AzCfg.Core.Model;

namespace AzCfg.Core.Mapping;

/// <summary>
/// Pure edits of edit-JSON text (format of <see cref="JsonProjection.ToEditJson"/>: nesting by <c>:</c>, global keys under
/// <c>"@global"</c>; an object with <c>"@kv"</c> is a leaf). Returns indented JSON (2 spaces); existing properties keep their
/// order, new ones are appended. Invalid JSON → <see cref="JsonException"/>; a non-object root, an invalid key, a path through
/// a leaf, or a leaf where an object is (and vice versa) → <see cref="ArgumentException"/> naming the key. Messages never
/// contain values.
/// </summary>
public static class EditJsonPatch
{
    private const string GlobalSection = "@global";
    private const string KeyVaultProperty = "@kv";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Sets <paramref name="key"/> to the plain string <paramref name="value"/>, creating parent objects as needed.</summary>
    public static string SetPlain(string editJson, ScopedKey key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Patch(editJson, key, (root, segments) => Set(root, key, segments, JsonValue.Create(value)));
    }

    /// <summary>Sets <paramref name="key"/> to the secret reference <c>{"@kv": secretName}</c>.</summary>
    public static string SetSecretReference(string editJson, ScopedKey key, string secretName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        return Patch(editJson, key, (root, segments) => Set(root, key, segments, new JsonObject { [KeyVaultProperty] = secretName }));
    }

    /// <summary>
    /// Sets <paramref name="key"/> to <c>{"@kv": secretName, "value": value}</c>. The result contains the secret: pass it
    /// straight to Core; never store, log or render it.
    /// </summary>
    public static string SetSecretValue(string editJson, ScopedKey key, string secretName, SecretString value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ArgumentNullException.ThrowIfNull(value);
        return Patch(editJson, key, (root, segments) =>
            Set(root, key, segments, new JsonObject { [KeyVaultProperty] = secretName, ["value"] = value.Reveal() }));
    }

    /// <summary>Removes <paramref name="key"/>; no-op when missing. Removes parents that become empty and an empty <c>"@global"</c>.</summary>
    public static string Remove(string editJson, ScopedKey key) => Patch(editJson, key, (root, segments) => Remove(root, key, segments));

    /// <summary>
    /// <paramref name="target"/> with the node of <paramref name="key"/> taken from <paramref name="source"/> (removed from
    /// the target when the source has none).
    /// </summary>
    public static string CopyKey(string source, string target, ScopedKey key) => Get(source, key) is { } node
        ? Patch(target, key, (root, segments) => Set(root, key, segments, node))
        : Remove(target, key);

    /// <summary>Deep clone of the node of <paramref name="key"/>, or <c>null</c> when missing.</summary>
    public static JsonNode? Get(string editJson, ScopedKey key)
    {
        var segments = Segments(key);
        var parent = Chain(Parse(editJson), key, segments, create: false)?[^1];
        if (parent is null || !parent.TryGetPropertyValue(segments[^1], out var node))
        {
            return null;
        }

        return IsBranch(node) ? throw NotALeaf(key) : node?.DeepClone();
    }

    /// <summary>
    /// Leaves that differ between two texts (added, removed or changed; a secret reference compares as its JSON), ordered by
    /// scope (<see cref="ConfigScope.Global"/> first) then key, ordinal.
    /// </summary>
    public static IReadOnlyList<ScopedKey> ChangedKeys(string before, string after)
    {
        var old = Leaves(Parse(before));
        var current = Leaves(Parse(after));
        return [.. old.Keys.Union(current.Keys)
            .Where(k => !old.TryGetValue(k, out var x) || !current.TryGetValue(k, out var y) || !JsonNode.DeepEquals(x, y))
            .OrderBy(k => k.Scope).ThenBy(k => k.Key, StringComparer.Ordinal)];
    }

    private static string Patch(string editJson, ScopedKey key, Action<JsonObject, IReadOnlyList<string>> edit)
    {
        var segments = Segments(key);
        var root = Parse(editJson);
        edit(root, segments);
        return root.ToJsonString(Indented);
    }

    private static void Set(JsonObject root, ScopedKey key, IReadOnlyList<string> segments, JsonNode? node)
    {
        var parent = Chain(root, key, segments, create: true)![^1];
        if (parent.TryGetPropertyValue(segments[^1], out var existing) && IsBranch(existing))
        {
            throw NotALeaf(key);
        }

        parent[segments[^1]] = node;
    }

    private static void Remove(JsonObject root, ScopedKey key, IReadOnlyList<string> segments)
    {
        var chain = Chain(root, key, segments, create: false);
        if (chain is null || !chain[^1].TryGetPropertyValue(segments[^1], out var existing))
        {
            return;
        }

        if (IsBranch(existing))
        {
            throw NotALeaf(key);
        }

        chain[^1].Remove(segments[^1]);
        for (var i = chain.Count - 1; i > 0 && chain[i].Count == 0; i--)
        {
            chain[i - 1].Remove(segments[i - 1]);
        }

        if (key.Scope == ConfigScope.Global && chain[0].Count == 0)
        {
            root.Remove(GlobalSection);
        }
    }

    /// <summary>The scope container followed by the parent objects of the leaf; <c>null</c> when missing and not created.</summary>
    private static List<JsonObject>? Chain(JsonObject root, ScopedKey key, IReadOnlyList<string> segments, bool create)
    {
        if (Container(root, key.Scope, create) is not { } current)
        {
            return null;
        }

        List<JsonObject> chain = [current];
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (!current.TryGetPropertyValue(segments[i], out var child))
            {
                if (!create)
                {
                    return null;
                }

                current[segments[i]] = child = new JsonObject();
            }

            current = IsBranch(child)
                ? (JsonObject)child!
                : throw new ArgumentException(
                    $"Key '{key.Key}' goes through '{KeyMapper.FromLocalJsonPath([.. segments.Take(i + 1)])}', which is a value, not a parent of other keys.",
                    nameof(key));
            chain.Add(current);
        }

        return chain;
    }

    private static JsonObject? Container(JsonObject root, ConfigScope scope, bool create) => scope switch
    {
        ConfigScope.Service => root,
        ConfigScope.Global when root.TryGetPropertyValue(GlobalSection, out var global) =>
            global as JsonObject ?? throw new ArgumentException($"'{GlobalSection}' must be an object."),
        ConfigScope.Global => create ? (JsonObject)(root[GlobalSection] = new JsonObject()) : null,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown config scope."),
    };

    private static Dictionary<ScopedKey, JsonNode?> Leaves(JsonObject root)
    {
        var leaves = new Dictionary<ScopedKey, JsonNode?>();
        Walk(root, [], ConfigScope.Service);
        return leaves;

        void Walk(JsonObject obj, IReadOnlyList<string> prefix, ConfigScope scope)
        {
            foreach (var (name, node) in obj)
            {
                if (scope == ConfigScope.Service && prefix.Count == 0 && name == GlobalSection)
                {
                    Walk(node as JsonObject ?? throw new ArgumentException($"'{GlobalSection}' must be an object."), [], ConfigScope.Global);
                    continue;
                }

                string[] path = [.. prefix, name];
                if (IsBranch(node))
                {
                    Walk((JsonObject)node!, path, scope);
                }
                else
                {
                    leaves[new(scope, KeyMapper.FromLocalJsonPath(path))] = node;
                }
            }
        }
    }

    private static IReadOnlyList<string> Segments(ScopedKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var segments = KeyMapper.ToLocalJsonPath(key.Key);
        if (segments.Contains(KeyVaultProperty) || (key.Scope == ConfigScope.Service && segments[0] == GlobalSection))
        {
            throw new ArgumentException($"Key '{key.Key}' uses a reserved name ('{KeyVaultProperty}' or a top-level '{GlobalSection}').", nameof(key));
        }

        return segments;
    }

    private static JsonObject Parse(string editJson)
    {
        ArgumentNullException.ThrowIfNull(editJson);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(editJson);
        }
        catch (JsonException e)
        {
            throw new JsonException(
                $"Edit JSON is not valid (line {e.LineNumber}, byte {e.BytePositionInLine}).", e.Path, e.LineNumber, e.BytePositionInLine);
        }

        return root as JsonObject ?? throw new ArgumentException("Edit JSON must be an object.", nameof(editJson));
    }

    private static bool IsBranch(JsonNode? node) => node is JsonObject obj && !obj.ContainsKey(KeyVaultProperty);

    private static ArgumentException NotALeaf(ScopedKey key) =>
        new($"Key '{key.Key}' is a parent of other keys, not a value.", nameof(key));
}
