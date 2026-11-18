using AzCfg.Core.Model;

namespace AzCfg.Core.Mapping;

/// <summary>A configuration path (no scope prefix, e.g. <c>Db:Host</c>) together with its <see cref="ConfigScope"/>.</summary>
public sealed record ScopedKey(ConfigScope Scope, string Key);
