using System.Text.Json;
using System.Text.Json.Nodes;
using AzCfg.Core.Mapping;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Features.Browse;

/// <summary>
/// One pending Azure edit of a key as the inspector and grid show it: plain values before and after, or only the fact that a
/// side is a Key Vault secret (secret values are never shown). <see cref="Removed"/> = the key is deleted in the draft.
/// </summary>
public sealed record PendingEdit(string? Before, bool BeforeIsSecret, string? After, bool AfterIsSecret, bool Removed)
{
    /// <summary>Reads the key from the draft's base and edited JSON; null when the draft text cannot be read.</summary>
    public static PendingEdit? Describe(RemoteDraft draft, DraftKey key)
    {
        try
        {
            var scoped = new ScopedKey(key.Scope, key.Key);
            var (before, beforeSecret) = Read(EditJsonPatch.Get(draft.BaseJson, scoped));
            var (after, afterSecret) = Read(EditJsonPatch.Get(draft.Json, scoped));
            afterSecret |= draft.Secrets.ContainsKey(key);
            return new PendingEdit(before, beforeSecret, after, afterSecret, after is null && !afterSecret);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static (string? Text, bool IsSecret) Read(JsonNode? node) => node switch
    {
        JsonObject o when o.ContainsKey("@kv") => (null, true),
        JsonValue v => (v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString(), false),
        _ => (null, false),
    };
}
