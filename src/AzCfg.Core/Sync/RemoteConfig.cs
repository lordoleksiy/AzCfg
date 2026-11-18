using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>
/// Effective remote configuration of a service in one environment, without secret values: <see cref="Entries"/> = after
/// <c>LabelSelector</c> (ADR 0008) and <c>ScopeMerger</c> (one entry per key, service overrides global), labels and ETags
/// kept; <see cref="Shadowed"/> = global entries hidden by a service override. <see cref="Warnings"/> carries label
/// duplicates (<see cref="SyncWarningKind.DuplicateLabels"/>).
/// </summary>
public sealed record RemoteConfig(ConfigEnvironment Environment, IReadOnlyList<ConfigEntry> Entries, IReadOnlyList<ConfigEntry> Shadowed)
{
    public IReadOnlyList<SyncWarning> Warnings { get; init; } = [];
}
