using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Security;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>
/// Compare backend (FR-C1…C4). Remote↔Remote matches Key Vault secrets by name; Local↔Remote compares a local value with a
/// remote secret by fingerprint, so no secret value is ever read into a view or JSON.
/// </summary>
internal sealed class CompareService(ISyncService sync, LocalTargetAccess local, ISecretFingerprinter fingerprinter, TimeProvider time) : ICompareService
{
    public async Task<ComparisonView> CompareAsync(ProjectBinding binding, CompareSide left, CompareSide right, CancellationToken ct) => (left, right) switch
    {
        (CompareSide.Remote l, CompareSide.Remote r) => await RemoteRemoteAsync(binding, left, right, l, r, ct),
        (CompareSide.Local, CompareSide.Remote r) => await LocalRemoteAsync(binding, left, right, r.Environment, localIsLeft: true, ct),
        (CompareSide.Remote l, CompareSide.Local) => await LocalRemoteAsync(binding, left, right, l.Environment, localIsLeft: false, ct),
        _ => throw new ArgumentException("Comparing local with local is not supported."),
    };

    private async Task<ComparisonView> RemoteRemoteAsync(ProjectBinding binding, CompareSide left, CompareSide right, CompareSide.Remote l, CompareSide.Remote r, CancellationToken ct)
    {
        var leftConfig = await sync.ReadRemoteConfigAsync(binding, l.Environment, ct);
        var rightConfig = await sync.ReadRemoteConfigAsync(binding, r.Environment, ct);
        var rows = EnvironmentComparer.Compare(leftConfig.Entries, rightConfig.Entries)
            .Select(i => new CompareRow(i.Key, Result(i.Kind), i.Left is null ? null : Cell(i.Left), i.Right is null ? null : Cell(i.Right)))
            .ToList();
        return new(binding, left, right, rows, 0, ConfigJson.Remote(leftConfig.Entries), ConfigJson.Remote(rightConfig.Entries), time.GetUtcNow());
    }

    private async Task<ComparisonView> LocalRemoteAsync(ProjectBinding binding, CompareSide left, CompareSide right, ConfigEnvironment env, bool localIsLeft, CancellationToken ct)
    {
        var diff = (await sync.DiffAsync(binding, env, ct)).Items.ToDictionary(i => i.Key, StringComparer.Ordinal);
        var remote = (await sync.ReadRemoteConfigAsync(binding, env, ct)).Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var locals = LocalTargetAccess.ByKey(await local.ReadAllAsync(binding, ct));
        var rows = new List<CompareRow>();
        List<(LocalValue Value, bool Masked)> localJson = [];
        List<ConfigEntry> remoteJson = [];
        foreach (var (key, item) in diff.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var entry = remote.GetValueOrDefault(key);
            var value = locals.GetValueOrDefault(key);
            if (entry is null && value is null)
            {
                continue;
            }

            var secret = SecretPolicy.IsSecret(item, entry, value?.Source, binding);
            var localCell = value is null ? null : new CompareCell(secret ? null : value.Value.Value, secret, entry is null ? null : SecretName(entry), value.Value.Scope, null);
            var remoteCell = entry is null ? null : Cell(entry);
            if (value is not null)
            {
                localJson.Add((value.Value, secret));
            }

            if (entry is not null)
            {
                remoteJson.Add(entry);
            }

            var result = (value, entry) switch
            {
                (null, _) => CompareResult.OnlyRight,
                (_, null) => CompareResult.OnlyLeft,
                _ => AreSame(item, value.Value.Value) ? CompareResult.Same : CompareResult.Different,
            };
            rows.Add(localIsLeft
                ? new(key, result, localCell, remoteCell)
                : new(key, Swap(result), remoteCell, localCell));
        }

        var ignored = locals.Keys.Concat(remote.Keys).Distinct(StringComparer.Ordinal).Count(k => !diff.ContainsKey(k));
        var (localText, remoteText) = (ConfigJson.Local(localJson), ConfigJson.Remote(remoteJson));
        return localIsLeft
            ? new(binding, left, right, rows, ignored, localText, remoteText, time.GetUtcNow())
            : new(binding, left, right, rows, ignored, remoteText, localText, time.GetUtcNow());
    }

    private bool AreSame(DiffItem item, string localValue) => item.Remote?.Value switch
    {
        StateValue.Secret secret => fingerprinter.Fingerprint(new SecretString(localValue)) == secret.Fingerprint,
        StateValue.Plain plain => string.Equals(plain.Value, localValue, StringComparison.Ordinal),
        _ => false,
    };

    private static CompareResult Swap(CompareResult result) => result switch
    {
        CompareResult.OnlyLeft => CompareResult.OnlyRight,
        CompareResult.OnlyRight => CompareResult.OnlyLeft,
        _ => result,
    };

    private static CompareResult Result(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Same => CompareResult.Same,
        ComparisonKind.Different => CompareResult.Different,
        ComparisonKind.OnlyLeft => CompareResult.OnlyLeft,
        _ => CompareResult.OnlyRight,
    };

    private static string? SecretName(ConfigEntry entry) => entry.Value switch
    {
        ConfigValue.KeyVaultRef r => KeyMapper.SecretNameOf(r.SecretUri),
        ConfigValue.ResolvedSecret r => KeyMapper.SecretNameOf(r.SecretUri),
        _ => null,
    };

    private static CompareCell Cell(ConfigEntry entry) => entry.Value switch
    {
        ConfigValue.Plain p => new(p.Value, false, null, entry.Scope, entry.Label),
        _ => new(null, true, SecretName(entry), entry.Scope, entry.Label),
    };
}
