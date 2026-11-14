using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>
/// Computes a 3-way diff (ADR 0007), ordered by key (ordinal). Local items are normalized first: scope is kept as
/// given; a plain local value becomes a secret only if remote or base knows the key as one (URI from remote,
/// else base). Secrets compare by fingerprint.
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

    private static StateItem Normalize(StateItem item, StateItem? @base, StateItem? remote, ISecretFingerprinter fingerprinter) =>
        item.Value is StateValue.Plain plain && (remote?.Value as StateValue.Secret ?? @base?.Value as StateValue.Secret) is { } known
            ? item with { Value = new StateValue.Secret(known.SecretUri, fingerprinter.Fingerprint(new SecretString(plain.Value))) }
            : item;

    /// <summary>
    /// Turns local changes into a <see cref="SyncPlan"/>; conflicts go to <see cref="SyncPlan.Conflicts"/>, remote
    /// changes are ignored. A change targets <see cref="ConfigScope.Global"/> only for an explicit global local item;
    /// otherwise a key that came from global becomes a service override, and deleting it does nothing. A changed
    /// secret yields <see cref="ChangeKind.CreateSecret"/> without a value (named from its URI, unless it came from
    /// another scope, else from the key). <see cref="Change.Before"/> is the matching raw remote entry.
    /// </summary>
    public static SyncPlan Plan(ConfigDiff diff, IReadOnlyList<ConfigEntry> remote, ProjectBinding binding, ConfigEnvironment env) => new(
        binding,
        env,
        [.. diff.Items.Select(i => ToChange(i, remote, binding.Service)).OfType<Change>()],
        [.. diff.Items.Where(i => i.Kind == DiffKind.Conflict).Select(i => new Conflict(i.Key, i.Base, i.Local, i.Remote))],
        false,
        diff.Items.Any(i => i.Kind is DiffKind.LocalAdded or DiffKind.LocalModified && i.Local!.Scope == ConfigScope.Global));

    private static Change? ToChange(DiffItem item, IReadOnlyList<ConfigEntry> remote, string service)
    {
        if (item.Kind == DiffKind.LocalDeleted)
            return item.Base is { Scope: ConfigScope.Service } ? new(ChangeKind.Delete, Find(item.Key, ConfigScope.Service), null) : null;
        if (item.Kind is not (DiffKind.LocalAdded or DiffKind.LocalModified))
            return null;

        var scope = item.Local!.Scope;
        var before = Find(item.Key, scope);
        return item.Local.Value switch
        {
            StateValue.Secret => new(ChangeKind.CreateSecret, before, null, SecretName(item, service)),
            StateValue.Plain plain => new(before is null ? ChangeKind.Add : ChangeKind.Update, before, new ConfigEntry(item.Key, scope, new ConfigValue.Plain(plain.Value))),
            _ => null,
        };

        ConfigEntry? Find(string key, ConfigScope s) => remote.FirstOrDefault(e => e.Key == key && e.Scope == s);
    }

    internal static string SecretName(Uri secretUri) => secretUri.Segments.SkipWhile(s => s != "secrets/").Skip(1).First().TrimEnd('/');

    internal static string SecretName(DiffItem item, string service) =>
        item.Local!.Value is StateValue.Secret { SecretUri: { } uri } && (item.Remote ?? item.Base)?.Scope == item.Local.Scope
            ? SecretName(uri)
            : KeyMapper.ToKeyVaultSecretName(KeyMapper.ToStoreKey(item.Local.Scope, service, item.Key));

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
