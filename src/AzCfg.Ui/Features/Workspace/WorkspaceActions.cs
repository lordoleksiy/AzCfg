using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Workspace;

/// <summary>
/// Opening and refreshing the workspace (scoped): the Open folder dialog scans through <see cref="IOperationRunner"/> itself and
/// hands the tree back; refreshing re-scans the open root. A tree replaces <see cref="WorkspaceState.Tree"/>; the selection is
/// dropped when its project no longer exists.
/// </summary>
public sealed class WorkspaceActions(WorkspaceState workspace, IWorkspaceService service, IOperationRunner runner, IDialogService dialogs, INotificationService notifications)
{
    /// <summary>Shows the Open folder dialog; <c>true</c> when a folder was opened.</summary>
    public async Task<bool> OpenFolderAsync()
    {
        var result = await dialogs.ShowDialogAsync<OpenFolderDialog>(o =>
        {
            o.Header.Title = "Open folder";
            o.Width = "640px";
        });
        if (result.Cancelled || result.GetValue<WorkspaceTree>() is not { } tree)
        {
            return false;
        }

        Apply(tree);
        return true;
    }

    /// <summary>Re-scans the open folder (Read); <c>false</c> when nothing is open or the scan failed (a toast says why).</summary>
    public async Task<bool> RefreshAsync()
    {
        if (workspace.Tree is not { } current)
        {
            return false;
        }

        switch (await runner.RunAsync($"Refresh {current.RootPath}", OperationKind.Read, ct => service.OpenAsync(current.RootPath, ct), CancellationToken.None))
        {
            case OperationResult<WorkspaceTree>.Success success:
                Apply(success.Value);
                return true;
            case OperationResult<WorkspaceTree>.Failed failed:
                await notifications.ToastAsync($"Could not refresh the workspace: {failed.Message}", ToastIntent.Error);
                return false;
            default:
                return false;
        }
    }

    private void Apply(WorkspaceTree tree)
    {
        workspace.SetTree(tree);
        if (workspace.SelectedProjectId is { } id && workspace.FindProject(id) is null)
        {
            workspace.SelectProject(null);
        }
    }
}
