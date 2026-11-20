using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Applies repeatable <c>--key</c> patterns (<see cref="KeySelector"/> syntax) to a push plan.</summary>
public static class KeyFilter
{
    /// <summary>The plan restricted to the keys matching <paramref name="patterns"/>; no patterns leave it unchanged.</summary>
    public static SyncPlan Restrict(SyncPlan plan, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return plan;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in plan.Changes.Select(c => c.After ?? c.Before).OfType<ConfigEntry>())
        {
            if (KeySelector.IsSelected(patterns, entry.Key, entry.Scope))
            {
                keys.Add(entry.Key);
            }
        }

        foreach (var conflict in plan.Conflicts)
        {
            if (KeySelector.IsSelected(patterns, conflict.Key, ScopeOf(conflict)))
            {
                keys.Add(conflict.Key);
            }
        }

        foreach (var warning in plan.Warnings)
        {
            if (KeySelector.IsSelected(patterns, warning.Key, ConfigScope.Service) || KeySelector.IsSelected(patterns, warning.Key, ConfigScope.Global))
            {
                keys.Add(warning.Key);
            }
        }

        return PlanSelection.Select(plan, keys);
    }

    /// <summary>Validates <c>--key</c> patterns with <see cref="IgnoreRules.Normalize"/>: blank and <c>global:</c> without a key path are errors.</summary>
    public static ValidationResult Validate(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                IgnoreRules.Normalize(pattern);
            }
            catch (ArgumentException)
            {
                return ValidationResult.Error($"--key '{pattern}' is not a key pattern: give a key path (* wildcard, optional global: prefix, e.g. 'global:*').");
            }
        }

        return ValidationResult.Success();
    }

    /// <summary>Scope of the key as conflicts see it (local, else remote, else base; service when unknown).</summary>
    public static ConfigScope ScopeOf(Conflict conflict) => (conflict.Local ?? conflict.Remote ?? conflict.Base)?.Scope ?? ConfigScope.Service;
}
