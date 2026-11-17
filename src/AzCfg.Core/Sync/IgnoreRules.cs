using System.Text.RegularExpressions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>
/// Local ignore patterns (ADR 0008): an optional <c>global:</c> prefix (matches only <see cref="ConfigScope.Global"/>)
/// followed by a key path where <c>*</c> matches any characters, including <c>:</c>; everything else is literal and
/// matching ignores case. Without the prefix a pattern matches any scope. The scope to match is the key's provenance
/// (remote, else base, else local <see cref="ConfigScope.Service"/>). Blank stored patterns match nothing.
/// </summary>
public static class IgnoreRules
{
    private const string GlobalPrefix = "global:";

    public static bool IsIgnored(IReadOnlyList<string>? patterns, string key, ConfigScope scope) =>
        patterns is not null && patterns.Any(p => Matches(p.Trim(), key, scope));

    /// <summary>Trims <paramref name="pattern"/>; throws <see cref="ArgumentException"/> when it is blank or has no key path after <c>global:</c>.</summary>
    public static string Normalize(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var trimmed = pattern.Trim();
        if (trimmed.StartsWith(GlobalPrefix, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(trimmed[GlobalPrefix.Length..]))
        {
            throw new ArgumentException("An ignore pattern needs a key path after 'global:' (use 'global:*' for all global keys).", nameof(pattern));
        }

        return trimmed;
    }

    private static bool Matches(string pattern, string key, ConfigScope scope)
    {
        if (pattern.StartsWith(GlobalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (scope != ConfigScope.Global)
                return false;
            pattern = pattern[GlobalPrefix.Length..];
        }

        return pattern.Length > 0
            && Regex.IsMatch(key, $"^{Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal)}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }
}
