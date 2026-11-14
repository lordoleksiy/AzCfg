namespace AzCfg.Core.Sync;

/// <summary>
/// A key both local and remote changed differently since <see cref="Base"/> (the last snapshot). Carries
/// <see cref="StateItem"/> (see <see cref="DiffItem"/>) rather than <see cref="Model.ConfigEntry"/>, so a
/// secret side is never more than a fingerprint.
/// </summary>
public sealed record Conflict(string Key, StateItem? Base, StateItem? Local, StateItem? Remote);
