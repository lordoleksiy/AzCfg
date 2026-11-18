using System.Text.Json;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>JSON shown in the editors: never contains a secret value (AD-6 (7)).</summary>
internal static class ConfigJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Edit JSON of remote entries: secrets only as <c>{"@kv": name}</c>.</summary>
    public static string Remote(IEnumerable<ConfigEntry> entries) =>
        JsonProjection.ToEditJson(entries.Select(e => e.Value is ConfigValue.ResolvedSecret r ? e with { Value = new ConfigValue.KeyVaultRef(r.SecretUri) } : e))
            .ToJsonString(Indented);

    /// <summary>Nested JSON of local values; a masked value becomes <c>***</c>.</summary>
    public static string Local(IEnumerable<(LocalValue Value, bool Masked)> values) =>
        JsonProjection.ToNestedJson(values.Select(v => v.Masked ? v.Value with { Value = "***" } : v.Value)).ToJsonString(Indented);
}
