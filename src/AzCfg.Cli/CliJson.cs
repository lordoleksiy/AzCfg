using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzCfg.Cli;

/// <summary>Serializes use-case result records for <c>--json</c> output (camelCase, secrets redacted by their converter).</summary>
public static class CliJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
