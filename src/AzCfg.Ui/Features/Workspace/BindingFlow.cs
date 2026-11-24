using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Shared;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Workspace;

/// <summary>
/// Bind / re-bind wizard (FR-W5). Proposes the setup (candidates, targets with their safety for secrets), asks for the service and
/// the two targets, binds through <see cref="IWorkspaceService.BindAsync"/> (a Write: a re-bind keeps vaults and ignore patterns, a
/// new service clears the active environment — Core rules) and updates the tree. When the binding has no active environment
/// afterwards, offers "Pull ‹env› now?" which runs the make-active flow (§2.8 step 3). A project named by a shared <c>azcfg.json</c>
/// is bound from it by Core while preparing (ADR 0012 amendment 2): no dialog, the tree is refreshed to show it shared. Changing
/// the service or targets of a shared project writes <c>azcfg.json</c> too, so the toast says to commit it.
/// </summary>
public sealed class BindingFlow(
    WorkspaceState workspace,
    ProjectSessions sessions,
    IWorkspaceService service,
    IOperationRunner runner,
    IDialogService dialogs,
    INotificationService notifications,
    IActivationFlow activation,
    WorkspaceActions actions) : IBindingFlow
{
    /// <summary><c>true</c> when the project is bound (or re-bound); <c>false</c> when cancelled or failed (a toast says why).</summary>
    public async Task<bool> BindAsync(string projectId)
    {
        if (workspace.FindProject(projectId) is not { } node)
        {
            await notifications.ToastAsync("The project is no longer in the workspace", ToastIntent.Warning);
            return false;
        }

        var proposal = await runner.RunAsync($"Prepare binding of {node.Project.Project.Name}", OperationKind.Read, ct => service.ProposeSetupAsync(node, ct), CancellationToken.None);
        if (proposal is not OperationResult<ProjectSetupProposal>.Success { Value: var setup })
        {
            if (proposal is OperationResult<ProjectSetupProposal>.Failed failed)
            {
                await notifications.ToastAsync($"Could not prepare the binding: {failed.Message}", ToastIntent.Error);
            }

            return false;
        }

        if (node.Binding is null && setup.Resolution is { Existing: { } fromManifest, ManifestPath: { } manifest })
        {
            workspace.UpdateBinding(projectId, fromManifest);
            if (sessions.TryGet(projectId, out var boundSession))
            {
                boundSession.Invalidate();
            }

            await notifications.ToastAsync($"{node.Project.Project.Name} is bound to ‘{fromManifest.Service}’ from {manifest}", ToastIntent.Success);
            await actions.RefreshAsync();
            await OfferPullAsync(projectId, fromManifest);
            return true;
        }

        var result = await dialogs.ShowDialogAsync<BindDialog>(o =>
        {
            o.Header.Title = node.Binding is null ? $"Bind {node.Project.Project.Name}" : $"Binding of {node.Project.Project.Name}";
            o.Width = "560px";
            o.Parameters.Add(nameof(BindDialog.Proposal), setup);
        });
        if (result.Cancelled || result.GetValue<BindChoice>() is not { } choice)
        {
            return false;
        }

        var bound = await runner.RunAsync($"Bind {node.Project.Project.Name}", OperationKind.Write, ct => service.BindAsync(node, choice.Service, choice.Values, choice.Secrets, ct), CancellationToken.None);
        switch (bound)
        {
            case OperationResult<ProjectBinding>.Success success:
                workspace.UpdateBinding(projectId, success.Value);
                if (sessions.TryGet(projectId, out var session))
                {
                    session.Invalidate();
                }

                var shared = setup.Resolution.ManifestPath is not null && node.Binding is { } old
                    && (old.Service != choice.Service || old.ValuesTarget != choice.Values || old.SecretsTarget != choice.Secrets);
                await notifications.ToastAsync(
                    $"{node.Project.Project.Name} is bound to ‘{success.Value.Service}’{(shared ? $". {ProjectSettingKeys.SharedHint}" : string.Empty)}",
                    ToastIntent.Success);
                await OfferPullAsync(projectId, success.Value);
                return true;
            case OperationResult<ProjectBinding>.Failed failed:
                await notifications.ToastAsync($"Binding failed: {failed.Message}", ToastIntent.Error);
                return false;
            default:
                return false;
        }
    }

    private async Task OfferPullAsync(string projectId, ProjectBinding binding)
    {
        if (binding.ActiveEnvironment is not null
            || workspace.Environments is not { Environments: { Count: > 0 } environments }
            || EnvironmentRules.SelectDefault(environments, binding) is not { } env)
        {
            return;
        }

        if (await dialogs.ConfirmAsync($"Pull {env.Name} now?", $"Make {env.Name} the active environment and write its configuration to the local files of this project?", "Pull", "Not now"))
        {
            await activation.MakeActiveAsync(projectId, env);
        }
    }
}
