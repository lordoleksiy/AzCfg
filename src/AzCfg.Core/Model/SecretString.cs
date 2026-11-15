using System.Text.Json.Serialization;

namespace AzCfg.Core.Model;

/// <summary>
/// Wraps a secret value so it is never accidentally printed, logged or serialized. The value is exposed
/// only via the explicit <see cref="Reveal"/> method. <see cref="ToString"/> and JSON serialization both
/// redact it to <c>"***"</c>; deserialization is never supported (see <see cref="SecretStringJsonConverter"/>).
/// </summary>
[JsonConverter(typeof(SecretStringJsonConverter))]
public sealed class SecretString : IEquatable<SecretString>
{
    private readonly string _value;

    public SecretString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>Returns the actual secret value. Use only where the real text is required.</summary>
    public string Reveal() => _value;

    public override string ToString() => "***";

    public bool Equals(SecretString? other) => other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as SecretString);

    public override int GetHashCode() => _value.GetHashCode(StringComparison.Ordinal);
}
