using AzCfg.Core.Model;
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
/// dropped when its project no longer exists, and the session of every project whose binding changed (e.g. bound from
/// <c>azcfg.json</c>) is invalidated. Share with team (FR-W7) and Apply azcfg.json are Writes after a confirmation; both refresh.
/// </summary>
public sealed class WorkspaceActions(
    WorkspaceState workspace,
    ProjectSessions sessions,
    IWorkspaceService service,
    IOperationRunner runner,
    IDialogService dialogs,
    INotificationService notifications)
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
    /// Asks Core which <c>azcfg.json</c> sharing writes, confirms with that path, shares the bound project (a Write) and refreshes;
    /// <c>false</c> when the project is not shareable (unbound, library, shared, broken manifest), cancelled or failed (a toast says why).
    /// </summary>
    public async Task<bool> ShareAsync(string projectId)
    {
        if (workspace.FindProject(projectId) is not { Binding: { } binding, IsShared: false, ManifestError: null, Project.Project.IsLibrary: false } node)
        {
            return false;
        }

        var name = node.Project.Project.Name;
        if (await TryAsync(await runner.RunAsync($"Prepare sharing of {name}", OperationKind.Read, ct => service.PlanShareAsync(node, ct), CancellationToken.None), $"Could not share {name}") is not { } file)
        {
            return false;
        }

        if (!await dialogs.ConfirmAsync(
                $"Share {name} with the team?",
                $"Writes the service ‘{binding.Service}’, the local targets and the vaults (never a value) to {file}. Commit it: teammates then open the project bound, without the wizard.",
                "Share",
                "Cancel"))
        {
            return false;
        }

        if (await TryAsync(await runner.RunAsync($"Share {name}", OperationKind.Write, ct => service.ShareAsync(node, ct), CancellationToken.None), $"Could not share {name}") is not { } path)
        {
            return false;
        }

        await notifications.ToastAsync($"{name} is shared via {path}. {ProjectSettingKeys.SharedHint}", ToastIntent.Success);
        await RefreshAsync();
        return true;
    }

    /// <summary>
    /// For a project marked with <see cref="ProjectNode.ManifestChange"/>: confirms (saying that a new service clears the active
    /// environment and resets the snapshots), applies <c>azcfg.json</c> (a Write), invalidates the project session, toasts what was
    /// reset and refreshes; <c>false</c> when there is nothing to apply, cancelled or failed.
    /// </summary>
    public async Task<bool> ApplyManifestAsync(string projectId)
    {
        if (workspace.FindProject(projectId) is not { Binding: not null, ManifestPath: { } manifest, ManifestChange: { } change } node)
        {
            return false;
        }

        var name = node.Project.Project.Name;
        var message = change.ServiceChanged
            ? $"{manifest} sets the service ‘{change.ManifestService}’ (now ‘{change.CurrentService}’): the active environment is cleared and the sync snapshots are reset; local files are not changed. Targets and vaults are taken from it too."
            : $"The local targets and vaults are taken from {manifest}. The active environment and snapshots stay.";
        if (!await dialogs.ConfirmAsync($"Apply azcfg.json to {name}?", message, "Apply", "Cancel"))
        {
            return false;
        }

        if (await TryAsync(await runner.RunAsync($"Apply azcfg.json to {name}", OperationKind.Write, ct => service.ApplyManifestAsync(node, ct), CancellationToken.None), $"Could not apply azcfg.json to {name}") is not { } resolution)
        {
            return false;
        }

        if (resolution.Existing is { } binding)
        {
            workspace.UpdateBinding(projectId, binding);
        }

        Invalidate(projectId);
        await notifications.ToastAsync(
            resolution.PreviousService is { } previous
                ? $"{name} now uses ‘{resolution.Existing?.Service}’ from azcfg.json (was ‘{previous}’): the active environment was cleared and the snapshots were reset"
                : $"{name} was updated from azcfg.json",
            ToastIntent.Success);
        await RefreshAsync();
        return true;
    }

    private async Task<T?> TryAsync<T>(OperationResult<T> result, string failure)
        where T : class
    {
        switch (result)
        {
            case OperationResult<T>.Success success:
                return success.Value;
            case OperationResult<T>.Failed failed:
                await notifications.ToastAsync($"{failure}: {failed.Message}", ToastIntent.Error);
                return null;
            default:
                return null;
        }
    }

    private void Apply(WorkspaceTree tree)
    {
        var before = AllNodes(workspace.Tree).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First().Binding);
        workspace.SetTree(tree);
        foreach (var node in AllNodes(tree).DistinctBy(n => n.Id))
        {
            if (before.TryGetValue(node.Id, out var old) && !Same(old, node.Binding))
            {
                Invalidate(node.Id);
            }
        }

        if (workspace.SelectedProjectId is { } id && workspace.FindProject(id) is null)
        {
            workspace.SelectProject(null);
        }
    }

    /// <summary>Content equality (a reloaded binding has new collection instances).</summary>
    private static bool Same(ProjectBinding? a, ProjectBinding? b) =>
        a is null || b is null
            ? a is null && b is null
            : a with { VaultByEnvironment = b.VaultByEnvironment, Ignore = b.Ignore } == b
              && a.VaultByEnvironment.Count == b.VaultByEnvironment.Count
              && a.VaultByEnvironment.All(p => b.VaultByEnvironment.TryGetValue(p.Key, out var uri) && uri == p.Value)
              && (a.Ignore ?? []).SequenceEqual(b.Ignore ?? []);

    private void Invalidate(string projectId)
    {
        if (sessions.TryGet(projectId, out var session))
        {
            session.Invalidate();
        }
    }

    private static IEnumerable<ProjectNode> AllNodes(WorkspaceTree? tree) =>
        tree is null ? [] : tree.Solutions.SelectMany(s => s.Projects).Concat(tree.LooseProjects);
}
