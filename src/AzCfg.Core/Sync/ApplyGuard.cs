namespace AzCfg.Core.Sync;

/// <summary>The apply gate shared by <c>SyncService.ApplyAsync</c> and clients (no client re-implements it).</summary>
public static class ApplyGuard
{
    /// <summary>
    /// Every reason <c>ApplyAsync</c> refuses <paramref name="plan"/> with <paramref name="options"/>, in this order:
    /// conflicts, missing vault choice, unconfirmed protected environment (name compared ignoring case), unconfirmed
    /// global changes. Empty → <c>ApplyAsync</c> proceeds. <c>SyncService.ApplyAsync</c> throws
    /// <see cref="InvalidOperationException"/> with the message of the first blocker.
    /// </summary>
    public static IReadOnlyList<ApplyBlocker> Check(SyncPlan plan, ApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        List<ApplyBlocker> blockers = [];
        if (plan.Conflicts.Count > 0)
            blockers.Add(ApplyBlocker.Conflicts);
        if (plan.RequiresVaultChoice)
            blockers.Add(ApplyBlocker.VaultChoiceRequired);
        if (plan.Environment.IsProtected && !string.Equals(options.ConfirmedEnvironment, plan.Environment.Name, StringComparison.OrdinalIgnoreCase))
            blockers.Add(ApplyBlocker.EnvironmentNotConfirmed);
        if (plan.TouchesGlobal && !options.ConfirmGlobal)
            blockers.Add(ApplyBlocker.GlobalNotConfirmed);
        return blockers;
    }
}
