using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Common;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Project;

/// <summary>Review and discard of the pending Azure edits of an environment (draft bar, <c>draft.review</c>, <c>draft.discard</c>).</summary>
public sealed class DraftFlow(IDialogService dialogs)
{
    /// <summary>Switches to Sync › Pending edits.</summary>
    public static void Review(ProjectSession session)
    {
        session.SetSyncView(SyncView.Draft);
        session.SetMode(ProjectMode.Sync);
    }

    /// <summary>Asks "Discard N pending edits?", then drops the draft; <c>true</c> when discarded.</summary>
    public async Task<bool> DiscardAsync(ProjectSession session, string environment)
    {
        if (session.DraftFor(environment) is null
            || !await dialogs.ConfirmAsync("Discard edits", $"Discard {session.PendingCount(environment)} pending edits to {environment}?", "Discard"))
        {
            return false;
        }

        session.SetDraft(environment, null, 0);
        return true;
    }
}
