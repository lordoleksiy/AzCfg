using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>
/// Selects keys by repeatable patterns (<c>--key</c>) with the <see cref="IgnoreRules"/> syntax: <c>*</c> matches any
/// characters, an optional <c>global:</c> prefix restricts the match to <see cref="ConfigScope.Global"/>, case is ignored.
/// Several patterns combine with OR. No patterns (<c>null</c> or empty) means no selection filter: every key is selected.
/// </summary>
public static class KeySelector
{
    public static bool IsSelected(IReadOnlyList<string>? patterns, string key, ConfigScope scope) =>
        patterns is not { Count: > 0 } || IgnoreRules.IsIgnored(patterns, key, scope);
}
