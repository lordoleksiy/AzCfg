using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.UseCases;

public sealed class ConfigComparisonService(ISyncService sync) : IConfigComparisonService
{
    public async Task<ConfigComparison> CompareAsync(ProjectBinding binding, CompareSide left, CompareSide right, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        switch (left, right)
        {
            case (CompareSide.Local, CompareSide.Local):
                throw new ArgumentException("Cannot compare local with local; one side must be an environment.", nameof(right));
            case (CompareSide.Local, CompareSide.Environment r):
                return await CompareLocalAsync(binding, r.Env, localIsLeft: true, ct);
            case (CompareSide.Environment l, CompareSide.Local):
                return await CompareLocalAsync(binding, l.Env, localIsLeft: false, ct);
            case (CompareSide.Environment l, CompareSide.Environment r):
                return await CompareEnvironmentsAsync(binding, l.Env, r.Env, ct);
            default:
                throw new ArgumentException("Unknown comparison side.", nameof(left));
        }
    }

    private async Task<ConfigComparison> CompareLocalAsync(ProjectBinding binding, ConfigEnvironment env, bool localIsLeft, CancellationToken ct)
    {
        var diff = await sync.DiffAsync(binding, env, ct);
        // A key only in the base (deleted on both sides, or from an old snapshot) has nothing to compare.
        var items = diff.Items.Where(d => d.Local is not null || d.Remote is not null).Select(d =>
        {
            var local = d.Local is null ? null : Show(d.Local.Value);
            var remote = d.Remote is null ? null : Show(d.Remote.Value);
            var kind = (d.Local, d.Remote) switch
            {
                (null, _) => ComparisonKind.OnlyRight,
                (_, null) => ComparisonKind.OnlyLeft,
                _ => Equal(d.Local.Value, d.Remote.Value) ? ComparisonKind.Same : ComparisonKind.Different,
            };
            return localIsLeft
                ? new ConfigComparisonItem(d.Key, kind, local, remote)
                : new ConfigComparisonItem(d.Key, Flip(kind), remote, local);
        });

        // DiffAsync already drops ignored keys, so the count comes from the remote side (local-only ignored keys are not counted).
        var remoteConfig = await sync.ReadRemoteConfigAsync(binding, env, ct);
        var ignored = remoteConfig.Entries.Count(e => IgnoreRules.IsIgnored(binding.Ignore, e.Key, e.Scope));
        return new([.. items.OrderBy(i => i.Key, StringComparer.Ordinal)], ignored);
    }

    private async Task<ConfigComparison> CompareEnvironmentsAsync(ProjectBinding binding, ConfigEnvironment left, ConfigEnvironment right, CancellationToken ct)
    {
        var l = await sync.ReadRemoteConfigAsync(binding, left, ct);
        var r = await sync.ReadRemoteConfigAsync(binding, right, ct);
        bool Keep(ConfigEntry e) => !IgnoreRules.IsIgnored(binding.Ignore, e.Key, e.Scope);
        var items = EnvironmentComparer.Compare([.. l.Entries.Where(Keep)], [.. r.Entries.Where(Keep)])
            .Select(i => new ConfigComparisonItem(i.Key, i.Kind, i.Left is null ? null : Show(i.Left.Value), i.Right is null ? null : Show(i.Right.Value)));
        var ignored = l.Entries.Concat(r.Entries).Where(e => !Keep(e)).Select(e => e.Key).Distinct(StringComparer.Ordinal).Count();
        return new([.. items], ignored);
    }

    private static ComparisonKind Flip(ComparisonKind kind) => kind switch
    {
        ComparisonKind.OnlyLeft => ComparisonKind.OnlyRight,
        ComparisonKind.OnlyRight => ComparisonKind.OnlyLeft,
        _ => kind,
    };

    private static bool Equal(StateValue a, StateValue b) => (a, b) switch
    {
        (StateValue.Plain x, StateValue.Plain y) => string.Equals(x.Value, y.Value, StringComparison.Ordinal),
        (StateValue.Secret x, StateValue.Secret y) => string.Equals(x.Fingerprint, y.Fingerprint, StringComparison.Ordinal),
        _ => false,
    };

    private static ComparisonValue Show(StateValue value) => value is StateValue.Plain p ? new(false, p.Value) : new(true, null);

    private static ComparisonValue Show(ConfigValue value) => value is ConfigValue.Plain p ? new(false, p.Value) : new(true, null);
}
