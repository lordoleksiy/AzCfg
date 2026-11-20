using AzCfg.Core.Model;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Common;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Project;

/// <summary>
/// The UI layer of AD-7: a protected environment is read-only until unlocked (Core still makes the final check). The unlock
/// lasts for this browser tab (circuit) only.
/// </summary>
public sealed class ProtectionFlow(ProtectedUnlockState unlock, IDialogService dialogs)
{
    /// <summary><c>true</c> when <paramref name="env"/> is protected and not unlocked.</summary>
    public bool IsLocked(ConfigEnvironment env) => env.IsProtected && !unlock.IsUnlocked(env.Name);

    /// <summary>Asks for confirmation, then unlocks; <c>true</c> when the environment is now editable.</summary>
    public async Task<bool> UnlockAsync(ConfigEnvironment env)
    {
        if (!IsLocked(env))
        {
            return true;
        }

        if (!await dialogs.ConfirmAsync("Unlock editing", $"{env.Name} is protected. Unlock editing of its Azure values in this browser tab?", "Unlock"))
        {
            return false;
        }

        unlock.Unlock(env.Name);
        return true;
    }

    public void Lock(ConfigEnvironment env) => unlock.Lock(env.Name);
}
