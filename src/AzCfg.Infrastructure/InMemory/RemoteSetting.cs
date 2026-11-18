using AzCfg.Core.Model;

namespace AzCfg.Infrastructure.InMemory;

/// <summary>One App Configuration setting held by <see cref="InMemoryAppConfig"/>: full store key, label (<c>null</c> = no label), value and ETag.</summary>
public sealed record RemoteSetting(string Key, string? Label, ConfigValue Value, string ETag)
{
    /// <summary>The plain text value, or <c>null</c> for a Key Vault reference.</summary>
    public string? Text => (Value as ConfigValue.Plain)?.Value;
}
