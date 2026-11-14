using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>
/// Computes a 3-way diff (ADR 0007), ordered by key (ordinal). Local items are normalized first: scope comes
/// from remote, then base, else <see cref="ConfigScope.Service"/>; a plain local value becomes a secret only if
/// remote or base knows the key as one (URI from remote, else base). Secrets compare by fingerprint.
/// </summary>
public static class ThreeWayDiff
{
    public static ConfigDiff Compute(ConfigState @base, ConfigState local, ConfigState remote, ISecretFingerprinter fingerprinter)
    {
        var keys = @base.Items.Keys.Concat(local.Items.Keys).Concat(remote.Items.Keys)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        return new([.. keys.Select(key =>
        {
            var b = @base.Items.GetValueOrDefault(key);
            var r = remote.Items.GetValueOrDefault(key);
            var l = local.Items.GetValueOrDefault(key) is { } item ? Normalize(item, b, r, fingerprinter) : null;
            return new DiffItem(key, Classify(b, l, r), b, l, r);
        })]);
    }

    private static StateItem Normalize(StateItem item, StateItem? @base, StateItem? remote, ISecretFingerprinter fingerprinter)
    {
        var scope = (remote ?? @base)?.Scope ?? ConfigScope.Service;
        var known = remote?.Value as StateValue.Secret ?? @base?.Value as StateValue.Secret;
        return item.Value is StateValue.Plain plain && known is not null
            ? item with { Scope = scope, Value = new StateValue.Secret(known.SecretUri, fingerprinter.Fingerprint(new SecretString(plain.Value))) }
            : item with { Scope = scope };
    }

    private static DiffKind Classify(StateItem? b, StateItem? l, StateItem? r) => (Same(b, l), Same(b, r), Same(l, r)) switch
    {
        (_, _, true) => DiffKind.Unchanged,
        (true, _, _) => b is null ? DiffKind.RemoteAdded : r is null ? DiffKind.RemoteDeleted : DiffKind.RemoteModified,
        (_, true, _) => b is null ? DiffKind.LocalAdded : l is null ? DiffKind.LocalDeleted : DiffKind.LocalModified,
        _ => DiffKind.Conflict,
    };

    private static bool Same(StateItem? a, StateItem? b) => (a?.Value, b?.Value) switch
    {
        (null, null) => true,
        (StateValue.Secret x, StateValue.Secret y) => x.Fingerprint == y.Fingerprint,
        var (x, y) => x is not null && x.Equals(y),
    };
}
