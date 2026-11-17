using System.Text.Json;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>Reads the <c>--json</c> output of scenario commands (camelCase use-case records).</summary>
public static class ScenarioJson
{
    public static JsonElement Parse(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    /// <summary><c>warnings</c> of a pull result or plan as <c>(kind, key)</c>.</summary>
    public static IReadOnlyList<(string? Kind, string? Key)> Warnings(JsonElement result) =>
        [.. result.GetProperty("warnings").EnumerateArray().Select(w => (w.GetProperty("kind").GetString(), w.GetProperty("key").GetString()))];

    /// <summary>
    /// <c>changes</c> of a plan as <c>(kind, scope, key, label)</c> of the entry written (<c>after</c>, else <c>before</c> for a delete),
    /// plus the label of <c>before</c> (<c>null</c> when there is no before).
    /// </summary>
    public static IReadOnlyList<(string? Kind, string? Scope, string? Key, string? Label, string? BeforeLabel)> Changes(JsonElement plan) =>
    [
        .. plan.GetProperty("changes").EnumerateArray().Select(c =>
        {
            var before = c.GetProperty("before");
            var after = c.GetProperty("after");
            var entry = after.ValueKind == JsonValueKind.Null ? before : after;
            return (
                c.GetProperty("kind").GetString(),
                entry.GetProperty("scope").GetString(),
                entry.GetProperty("key").GetString(),
                entry.GetProperty("label").GetString(),
                before.ValueKind == JsonValueKind.Null ? null : before.GetProperty("label").GetString());
        }),
    ];

    public static IReadOnlyList<string?> Strings(JsonElement result, string property) =>
        [.. result.GetProperty(property).EnumerateArray().Select(e => e.GetString())];

    /// <summary>Asserts that <paramref name="output"/> is an <c>ApplyResult</c> with <paramref name="applied"/> changes and no failed keys.</summary>
    public static void AssertApplied(string output, int applied)
    {
        var result = Parse(output);
        Assert.Equal(applied, result.GetProperty("applied").GetInt32());
        Assert.Empty(Strings(result, "failed"));
    }
}
