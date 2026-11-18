using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>Row filters of the Browse view.</summary>
public static class ConfigFilters
{
    /// <summary>
    /// Text matches the key (ignore case) and plain unmasked values, never secret values. Changed = a status other than
    /// <see cref="DiffKind.Unchanged"/>. Scope is the remote scope, else the local one.
    /// </summary>
    public static IReadOnlyList<ConfigRow> Apply(IReadOnlyList<ConfigRow> rows, ConfigFilter filter) =>
        [.. rows.Where(r => Matches(r, filter))];

    private static bool Matches(ConfigRow row, ConfigFilter filter) =>
        (string.IsNullOrEmpty(filter.Text) || MatchesText(row, filter.Text))
        && (!filter.ChangedOnly || row.Status is not null and not DiffKind.Unchanged)
        && (!filter.SecretsOnly || row.IsSecret)
        && (!filter.IgnoredOnly || row.IsIgnored)
        && (filter.Scope is null || (row.Remote?.Scope ?? row.Local?.Scope) == filter.Scope);

    private static bool MatchesText(ConfigRow row, string text) =>
        Contains(row.Key, text)
        || (row.Local is { Masked: false, Value: { } local } && Contains(local, text))
        || (row.Remote is { IsSecret: false, Value: { } remote } && Contains(remote, text));

    private static bool Contains(string value, string text) => value.Contains(text, StringComparison.OrdinalIgnoreCase);
}
