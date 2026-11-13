namespace AzCfg.Core.Model;

public sealed record ConfigEntry(string Key, ConfigScope Scope, ConfigValue Value, string? ETag = null);
