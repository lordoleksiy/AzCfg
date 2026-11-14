using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>
/// A normalized configuration item used by the 3-way diff (decision 4). <see cref="Key"/> is the
/// configuration path without the scope prefix (see <see cref="ConfigEntry"/>).
/// </summary>
public sealed record StateItem(string Key, ConfigScope Scope, StateValue Value);
