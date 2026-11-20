using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Common;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Platform;

/// <summary>Closes workbench tabs; a dirty tab asks "Discard N pending edits?" first. Closing a project tab drops its session.</summary>
public sealed class TabCloser(TabsState tabs, ProjectSessions sessions, IDialogService dialogs)
{
    /// <summary>Returns <c>true</c> when the tab was closed.</summary>
    public async Task<bool> CloseAsync(Guid id)
    {
        if (tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab)
        {
            return false;
        }

        var projectId = tab.Descriptor.ProjectId;
        if (!tabs.RequestClose(id))
        {
            var pending = projectId is not null && sessions.TryGet(projectId, out var session) ? session.TotalPending : 0;
            if (!await dialogs.ConfirmAsync("Discard changes", $"Discard {pending} pending edits?", "Discard"))
            {
                return false;
            }

            tabs.ForceClose(id);
        }

        if (projectId is not null)
        {
            sessions.Close(projectId);
        }

        return true;
    }
}
