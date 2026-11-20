using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Features.Compare;

/// <summary>Default sides, side names and result texts of Compare mode (§2.6).</summary>
public static class CompareSides
{
    /// <summary>
    /// The selected environment (else <see cref="EnvironmentRules.SelectDefault"/>) on the right; on the left Local when it is the
    /// active one or no environment is active, else the active environment. Null without environments.
    /// </summary>
    public static (CompareSide Left, CompareSide Right)? Default(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> environments, string? selected)
    {
        var right = environments.FirstOrDefault(e => string.Equals(e.Name, selected, StringComparison.OrdinalIgnoreCase))
            ?? EnvironmentRules.SelectDefault(environments, binding);
        if (right is null)
        {
            return null;
        }

        var active = environments.FirstOrDefault(e => EnvironmentRules.IsActive(binding, e));
        return (active is null || active == right ? new CompareSide.Local() : new CompareSide.Environment(active), new CompareSide.Environment(right));
    }

    public static string Name(CompareSide side) => side is CompareSide.Environment e ? e.Env.Name : "Local";

    /// <summary>
    /// What the result of <paramref name="row"/> means; secrets are "same/different secret value" against local files (fingerprints)
    /// and "secret reference · values not compared" between environments (names).
    /// </summary>
    public static string Describe(CompareRow row, CompareSide left, CompareSide right)
    {
        var secret = row.Left?.IsSecret == true || row.Right?.IsSecret == true;
        var byName = left is CompareSide.Environment && right is CompareSide.Environment;
        return (row.Result, secret, byName) switch
        {
            (CompareResult.OnlyLeft, _, _) => $"Only in {Name(left)}",
            (CompareResult.OnlyRight, _, _) => $"Only in {Name(right)}",
            (CompareResult.Same, false, _) => "Same value",
            (CompareResult.Different, false, _) => "Different values",
            (CompareResult.Same, true, false) => "Same secret value",
            (CompareResult.Different, true, false) => "Different secret value",
            (CompareResult.Same, true, true) => "Same secret reference · values not compared",
            _ => "Different secret reference · values not compared",
        };
    }
}
