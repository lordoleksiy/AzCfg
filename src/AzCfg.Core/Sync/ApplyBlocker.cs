namespace AzCfg.Core.Sync;

/// <summary>A reason <c>ISyncService.ApplyAsync</c> refuses a plan (see <see cref="ApplyGuard"/>).</summary>
public enum ApplyBlocker
{
    /// <summary>The plan has conflicts.</summary>
    Conflicts,

    /// <summary>A new secret has no Key Vault yet (<see cref="SyncPlan.RequiresVaultChoice"/>).</summary>
    VaultChoiceRequired,

    /// <summary>The environment is protected and its name was not typed (<see cref="ApplyOptions.ConfirmedEnvironment"/>, ignoring case).</summary>
    EnvironmentNotConfirmed,

    /// <summary>The plan changes global keys without <see cref="ApplyOptions.ConfirmGlobal"/>.</summary>
    GlobalNotConfirmed,
}
