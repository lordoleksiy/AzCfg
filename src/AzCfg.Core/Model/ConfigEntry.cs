namespace AzCfg.Core.Model;

/// <summary>
/// A single configuration entry. <see cref="Key"/> is the configuration path without the scope prefix
/// (e.g. <c>ConnectionStrings:Crm</c>); the App Configuration store key is built via
/// <see cref="Mapping.KeyMapper.ToStoreKey"/>.
/// </summary>
public sealed record ConfigEntry(string Key, ConfigScope Scope, ConfigValue Value, string? ETag = null);
