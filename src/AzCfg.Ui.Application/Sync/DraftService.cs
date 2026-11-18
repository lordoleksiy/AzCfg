using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Sync;

/// <summary>
/// Pending Azure edits as pure operations on <see cref="RemoteDraft"/> (AD-5). Secret values live only in
/// <see cref="RemoteDraft.Secrets"/>; <c>Json</c>/<c>BaseJson</c> hold references. Messages and diagnostics name keys, never values.
/// </summary>
public sealed partial class DraftService(ISyncService sync, ISecretFingerprinter fingerprinter) : IDraftService
{
    private static readonly IReadOnlyDictionary<string, StateValue.Secret> NoSecrets = new Dictionary<string, StateValue.Secret>();

    public async Task<RemoteDraft> StartAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        var json = await sync.ExportJsonAsync(binding, env, ct);
        return new(binding, env, json, json, new Dictionary<DraftKey, DraftSecret>());
    }

    public RemoteDraft SetValue(RemoteDraft draft, DraftKey key, string value) =>
        draft with { Json = EditJsonPatch.SetPlain(draft.Json, Scoped(key), value), Secrets = Without(draft, key) };

    public RemoteDraft SetSecretValue(RemoteDraft draft, DraftKey key, SecretString value)
    {
        var name = ReferenceName(draft.Json, key) ?? ReferenceName(draft.BaseJson, key)
            ?? KeyMapper.ToKeyVaultSecretName(KeyMapper.ToStoreKey(key.Scope, draft.Binding.Service, key.Key));
        return draft with
        {
            Json = EditJsonPatch.SetSecretReference(draft.Json, Scoped(key), name),
            Secrets = new Dictionary<DraftKey, DraftSecret>(draft.Secrets) { [key] = new(name, value) },
        };
    }

    public RemoteDraft MoveToKeyVault(RemoteDraft draft, DraftKey key) =>
        EditJsonPatch.Get(draft.Json, Scoped(key)) is JsonValue node
            ? SetSecretValue(draft, key, new SecretString(node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString()))
            : throw new ArgumentException($"Key '{key.Key}' is not a plain value.", nameof(key));

    public RemoteDraft Remove(RemoteDraft draft, DraftKey key) =>
        draft with { Json = EditJsonPatch.Remove(draft.Json, Scoped(key)), Secrets = Without(draft, key) };

    public RemoteDraft Revert(RemoteDraft draft, DraftKey key) =>
        draft with { Json = EditJsonPatch.CopyKey(draft.BaseJson, draft.Json, Scoped(key)), Secrets = Without(draft, key) };

    /// <summary>
    /// Replaces the text. A <c>"value"</c> typed next to an <c>"@kv"</c> is moved into <see cref="RemoteDraft.Secrets"/> and
    /// removed from the text. Pending secrets whose key no longer holds the same reference are dropped; invalid JSON keeps them
    /// (the text is shown as is until it is valid again).
    /// </summary>
    public RemoteDraft ReplaceJson(RemoteDraft draft, string json)
    {
        List<(DraftKey Key, string Name, string? Value)> typed;
        try
        {
            typed = JsonNode.Parse(json) is JsonObject obj
                ? [.. TypedSecrets(obj, [], ConfigScope.Service)]
                : throw new ArgumentException("Edit JSON must be an object.", nameof(json));
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return draft with { Json = json };
        }

        var secrets = new Dictionary<DraftKey, DraftSecret>(draft.Secrets);
        var text = json;
        foreach (var (key, name, value) in typed)
        {
            text = EditJsonPatch.SetSecretReference(text, Scoped(key), name);
            if (value is not null)
            {
                secrets[key] = new(name, new SecretString(value));
            }
        }

        foreach (var (key, secret) in secrets.ToList())
        {
            if (ReferenceName(text, key) != secret.SecretName)
            {
                secrets.Remove(key);
            }
        }

        return draft with { Json = text, Secrets = secrets };
    }

    public IReadOnlyList<JsonDiagnostic> Validate(RemoteDraft draft)
    {
        try
        {
            JsonProjection.FromEditJson(draft.Json, fingerprinter, NoSecrets);
            return [];
        }
        catch (JsonException e)
        {
            return [new("Invalid JSON syntax.", null, e.LineNumber is { } line ? (int)line + 1 : null, e.BytePositionInLine is { } column ? (int)column + 1 : null)];
        }
        catch (ArgumentException e)
        {
            var message = e.Message.Split(" (Parameter", 2)[0];
            return [new(message, KeyIn(message), null, null)];
        }
    }

    public IReadOnlyList<DraftKey> PendingKeys(RemoteDraft draft)
    {
        IEnumerable<DraftKey> changed;
        try
        {
            changed = EditJsonPatch.ChangedKeys(draft.BaseJson, draft.Json).Select(k => new DraftKey(k.Scope, k.Key));
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            changed = [];
        }

        return [.. changed.Concat(draft.Secrets.Keys).Distinct().OrderBy(k => k.Scope).ThenBy(k => k.Key, StringComparer.Ordinal)];
    }

    public PushSource ToPushSource(RemoteDraft draft) => new PushSource.EditedJson(
        draft.Secrets.Aggregate(draft.Json, (json, s) => EditJsonPatch.SetSecretValue(json, Scoped(s.Key), s.Value.SecretName, s.Value.Value)),
        draft.BaseJson);

    public async Task<RemoteDraft> ResolveConflictAsync(RemoteDraft draft, string key, ConflictChoice choice, CancellationToken ct)
    {
        var fresh = await sync.ExportJsonAsync(draft.Binding, draft.Environment, ct);
        var result = draft;
        foreach (var scope in new[] { ConfigScope.Service, ConfigScope.Global })
        {
            var scoped = new ScopedKey(scope, key);
            if (new[] { fresh, draft.Json, draft.BaseJson }.All(t => EditJsonPatch.Get(t, scoped) is null))
            {
                continue;
            }

            result = choice == ConflictChoice.KeepRemote
                ? result with { Json = EditJsonPatch.CopyKey(fresh, result.Json, scoped), Secrets = Without(result, new(scope, key)) }
                : result with { BaseJson = EditJsonPatch.CopyKey(fresh, result.BaseJson, scoped) };
        }

        return result;
    }

    private static ScopedKey Scoped(DraftKey key) => new(key.Scope, key.Key);

    private static IReadOnlyDictionary<DraftKey, DraftSecret> Without(RemoteDraft draft, DraftKey key) =>
        draft.Secrets.ContainsKey(key)
            ? draft.Secrets.Where(s => s.Key != key).ToDictionary(s => s.Key, s => s.Value)
            : draft.Secrets;

    private static string? ReferenceName(string json, DraftKey key) =>
        EditJsonPatch.Get(json, Scoped(key)) is JsonObject o && o["@kv"] is JsonValue v && v.TryGetValue(out string? name) ? name : null;

    private static string? KeyIn(string message) =>
        (TrailingKey().Match(message) is { Success: true } trailing ? trailing : QuotedKey().Match(message)) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>Every <c>{"@kv": name, "value": ...}</c> leaf: key, secret name and the typed value (<c>null</c> for a missing or null one).</summary>
    private static IEnumerable<(DraftKey Key, string Name, string? Value)> TypedSecrets(JsonObject obj, IReadOnlyList<string> prefix, ConfigScope scope)
    {
        foreach (var (property, node) in obj)
        {
            if (scope == ConfigScope.Service && prefix.Count == 0 && property == "@global" && node is JsonObject global)
            {
                foreach (var found in TypedSecrets(global, [], ConfigScope.Global))
                {
                    yield return found;
                }

                continue;
            }

            if (node is not JsonObject child)
            {
                continue;
            }

            if (child["@kv"] is JsonValue kv && kv.TryGetValue(out string? name))
            {
                if (child.ContainsKey("value") && ToKey(prefix, property) is { } key)
                {
                    yield return (new(scope, key), name, child["value"] is JsonValue v ? v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString() : null);
                }

                continue;
            }

            foreach (var found in TypedSecrets(child, [.. prefix, property], scope))
            {
                yield return found;
            }
        }
    }

    private static string? ToKey(IReadOnlyList<string> prefix, string property)
    {
        try
        {
            return KeyMapper.FromLocalJsonPath([.. prefix, property]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"'([^'\s]*[\p{L}\p{N}_@][^'\s]*)'")]
    private static partial Regex QuotedKey();

    [GeneratedRegex(@"Key: (\S+)")]
    private static partial Regex TrailingKey();
}
