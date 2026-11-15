using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzCfg.Core.Model;

/// <summary>
/// Serializes <see cref="SecretString"/> as the literal <c>"***"</c>; reading is never supported, so a
/// secret value can never be reconstructed from a serialized plan, snapshot or log.
/// </summary>
public sealed class SecretStringJsonConverter : JsonConverter<SecretString>
{
    private const string Redacted = "***";

    public override SecretString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException($"{nameof(SecretString)} must never be deserialized.");

    public override void Write(Utf8JsonWriter writer, SecretString value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(Redacted);
    }
}
