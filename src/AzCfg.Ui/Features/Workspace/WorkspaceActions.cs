using AzCfg.Core.Abstractions;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Components.Common;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Workspace;

/// <summary>
/// Opening and refreshing the workspace (scoped): the Open folder dialog scans through <see cref="IOperationRunner"/> itself and
/// hands the tree back; refreshing re-scans the open root. A tree replaces <see cref="WorkspaceState.Tree"/>; the selection is
/// dropped when its project no longer exists. Share with team (FR-W7) writes <c>azcfg.json</c> after a confirmation and refreshes
/// the tree, so the node shows its manifest.
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

    /// <summary>
    /// Confirms, then shares the bound project via <c>azcfg.json</c> (a Write) and refreshes the tree; <c>false</c> when the project
    /// is not shareable (unbound, library, already shared), cancelled or failed (a toast says why).
    /// </summary>
    public async Task<bool> ShareAsync(string projectId)
    {
        if (workspace.FindProject(projectId) is not { Binding: { } binding, IsShared: false, Project.Project.IsLibrary: false } node)
        {
            return false;
        }

        var name = node.Project.Project.Name;
        var file = Path.Combine(binding.RepoPath, IProjectManifestStore.FileName);
        if (!await dialogs.ConfirmAsync(
                $"Share {name} with the team?",
                $"Writes the service ‘{binding.Service}’, the local targets and the vaults (never a value) to {file}. Commit it: teammates then open the project bound, without the wizard.",
                "Share",
                "Cancel"))
        {
            return false;
        }

        switch (await runner.RunAsync($"Share {name}", OperationKind.Write, ct => service.ShareAsync(node, ct), CancellationToken.None))
        {
            case OperationResult<string>.Success success:
                await notifications.ToastAsync($"{name} is shared via {success.Value}. {ProjectSettingKeys.SharedHint}", ToastIntent.Success);
                await RefreshAsync();
                return true;
            case OperationResult<string>.Failed failed:
                await notifications.ToastAsync($"Could not share {name}: {failed.Message}", ToastIntent.Error);
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
