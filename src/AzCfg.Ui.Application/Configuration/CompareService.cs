using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>
/// Compare backend (FR-C1…C4): a row/JSON adapter over Core <see cref="IConfigComparisonService"/>, which decides
/// Same/Different (secrets by name between environments, by fingerprint against local files) and the ignored count.
/// This adapter only adds what the view shows on top: scope, label and secret name of each cell, and the JSON texts.
/// No secret value is ever read into a view or JSON.
/// </summary>
internal sealed class CompareService(IConfigComparisonService comparison, ISyncService sync, LocalTargetAccess local, TimeProvider time) : ICompareService
{
    public async Task<ComparisonView> CompareAsync(ProjectBinding binding, CompareSide left, CompareSide right, CancellationToken ct)
    {
        var result = await comparison.CompareAsync(binding, left, right, ct);
        var leftSide = await ReadSideAsync(binding, left, ct);
        var rightSide = await ReadSideAsync(binding, right, ct);
        var rows = result.Items
            .Select(i => new CompareRow(i.Key, Result(i.Kind), Cell(i.Key, i.Left, leftSide, rightSide), Cell(i.Key, i.Right, rightSide, leftSide)))
            .ToList();
        return new(binding, left, right, rows, result.IgnoredCount, leftSide.Json(rows, isLeft: true), rightSide.Json(rows, isLeft: false), time.GetUtcNow());
    }

    public async Task<MissingKeysView> MissingKeysAsync(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> environments, CancellationToken ct)
    {
        var configs = new List<RemoteConfig>();
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var env in environments)
        {
            var config = await sync.ReadRemoteConfigAsync(binding, env, ct);
            ignored.UnionWith(config.Entries.Where(e => IgnoreRules.IsIgnored(binding.Ignore, e.Key, e.Scope)).Select(e => e.Key));
            configs.Add(config with { Entries = [.. config.Entries.Where(e => !IgnoreRules.IsIgnored(binding.Ignore, e.Key, e.Scope))] });
        }

        var gaps = MissingKeysReport.Compute(configs).Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var byEnv = configs.Select(c => c.Entries.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)).ToList();
        var rows = byEnv.SelectMany(d => d.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(key => new MatrixRow(
                key,
                byEnv.Any(d => d.GetValueOrDefault(key)?.Scope == ConfigScope.Global) ? ConfigScope.Global : ConfigScope.Service,
                gaps.Contains(key),
                [.. byEnv.Select(d => d.GetValueOrDefault(key) is { } e ? MatrixCellOf(e) : null)]))
            .ToList();
        return new(binding, environments, rows, ignored.Count, time.GetUtcNow());
    }

    private static MatrixCell MatrixCellOf(ConfigEntry entry) => entry.Value is ConfigValue.Plain plain
        ? new(plain.Value, false, null)
        : new(null, true, SecretName(entry));

    private async Task<Side> ReadSideAsync(ProjectBinding binding, CompareSide side, CancellationToken ct) => side switch
    {
        CompareSide.Environment r => new Side(null, (await sync.ReadRemoteConfigAsync(binding, r.Env, ct)).Entries
            .GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)),
        _ => new Side(LocalTargetAccess.ByKey(await local.ReadAllAsync(binding, ct)), null),
    };

    private static CompareCell? Cell(string key, ComparisonValue? value, Side own, Side other)
    {
        if (value is null)
        {
            return null;
        }

        if (own.Remote?.GetValueOrDefault(key) is { } entry)
        {
            return new(value.IsSecret ? null : value.Plain, value.IsSecret, SecretName(entry), entry.Scope, entry.Label);
        }

        // Local cell: the secret name comes from the remote entry of the other side (if any).
        var local = own.Local?.GetValueOrDefault(key);
        var remote = other.Remote?.GetValueOrDefault(key);
        return new(value.IsSecret ? null : value.Plain, value.IsSecret, remote is null ? null : SecretName(remote), local?.Value.Scope ?? ConfigScope.Service, null);
    }

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

    /// <summary>What one side holds: local values or remote entries, by key.</summary>
    private sealed record Side(Dictionary<string, SourcedLocalValue>? Local, Dictionary<string, ConfigEntry>? Remote)
    {
        /// <summary>JSON of the keys present on this side in <paramref name="rows"/>; local secrets masked.</summary>
        public string Json(IReadOnlyList<CompareRow> rows, bool isLeft)
        {
            var cells = rows.Select(r => (r.Key, Cell: isLeft ? r.Left : r.Right)).Where(c => c.Cell is not null).ToList();
            return Remote is not null
                ? ConfigJson.Remote(cells.Select(c => Remote[c.Key]))
                : ConfigJson.Local(cells.Where(c => Local!.ContainsKey(c.Key)).Select(c => (Local![c.Key].Value, c.Cell!.IsSecret)));
        }
    }
}
