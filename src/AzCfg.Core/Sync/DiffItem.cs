using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>
/// One row of a 3-way diff between base (last snapshot), local and remote state. <see cref="Key"/> is the
/// configuration path without the scope prefix (see <see cref="ConfigEntry"/>).
/// </summary>
public sealed record DiffItem(string Key, DiffKind Kind, StateItem? Base, StateItem? Local, StateItem? Remote);
