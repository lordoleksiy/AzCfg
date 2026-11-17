namespace AzCfg.Core.Model;

/// <summary>
/// A single configuration entry. <see cref="Key"/> is the configuration path without the scope prefix
/// (e.g. <c>ConnectionStrings:Crm</c>); the App Configuration store key is built via
/// <see cref="Mapping.KeyMapper.ToStoreKey"/>. <see cref="Label"/> is the App Configuration label of the setting
/// (<c>null</c> = no label); writes target exactly this label (ADR 0008).
/// </summary>
public sealed record ConfigEntry(string Key, ConfigScope Scope, ConfigValue Value, string? ETag = null, string? Label = null);
