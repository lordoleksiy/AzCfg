using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Shared;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Sync;

/// <summary>
/// Make active (= <c>azcfg use</c>, FR-S6) and Pull all. Before anything is written: a first pull with local keys asks
/// "N local keys will be replaced" (G20), a protected environment warns that its secrets go to local files (AD-7). A pull
/// blocked by local changes writes nothing and asks: Push first (opens Sync › Push), Discard local changes (force) or Cancel.
/// After a pull the binding's active environment is updated in <see cref="WorkspaceState"/> and the project's views are invalidated.
/// </summary>
public sealed class ActivationFlow(
    WorkspaceState workspace,
    ProjectSessions sessions,
    WorkbenchNavigator navigator,
    ISyncWorkflowService sync,
    IOperationRunner runner,
    IDialogService dialogs,
    INotificationService notifications) : IActivationFlow
{
    public Task<bool> MakeActiveAsync(string projectId, ConfigEnvironment environment) => PullAsync(projectId, environment, true);

    /// <summary>Pulls every key of the (active) environment with the guards of <see cref="MakeActiveAsync"/>.</summary>
    public Task<bool> PullAllAsync(string projectId, ConfigEnvironment environment) => PullAsync(projectId, environment, false);

    /// <summary>The warning before pulling from a protected environment (AD-7); <c>true</c> for any other environment.</summary>
    public async Task<bool> ConfirmProtectedPullAsync(ConfigEnvironment environment) =>
        EnvironmentRules.KindOf(environment) != EnvironmentKind.Production
        || await dialogs.ConfirmAsync($"Pull {environment.Name}", $"Secrets of {environment.Name} will be written to local files.", "Continue");

    private async Task<bool> PullAsync(string projectId, ConfigEnvironment env, bool activate)
    {
        if (workspace.FindProject(projectId)?.Binding is not { } binding)
        {
            await notifications.ToastAsync("Bind this project to a service first", ToastIntent.Warning);
            return false;
        }

        if (!await ConfirmUnmanagedAsync(binding, env) || !await ConfirmProtectedPullAsync(env))
        {
            return false;
        }

        var force = false;
        while (true)
        {
            var result = await runner.RunAsync($"Pull {env.Name}", OperationKind.Write, ct => sync.PullAsync(binding, env, force, ct), CancellationToken.None);
            switch (result)
            {
                case OperationResult<PullResult>.Success { Value.Blocked: true } blocked:
                    var choice = await AskBlockedAsync(blocked.Value.LocalChanges);
                    if (choice == BlockedPullChoice.PushFirst)
                    {
                        OpenPush(projectId, binding);
                    }

                    if (choice != BlockedPullChoice.Discard)
                    {
                        return false;
                    }

                    force = true;
                    continue;
                case OperationResult<PullResult>.Success { Value: var pulled }:
                    workspace.UpdateBinding(projectId, binding with { ActiveEnvironment = env.Name });
                    if (sessions.TryGet(projectId, out var session))
                    {
                        session.Invalidate();
                    }

                    var written = $"{pulled.ValuesWritten} values, {pulled.SecretsWritten} secrets written";
                    await notifications.ToastAsync(activate ? $"{env.Name} is active: {written}" : $"Pulled {env.Name}: {written}", ToastIntent.Success);
                    return true;
                case OperationResult<PullResult>.Failed failed:
                    await notifications.ToastAsync($"Pull of {env.Name} failed: {failed.Message}", ToastIntent.Error);
                    return false;
                default:
                    return false;
            }
        }
    }

    private async Task<bool> ConfirmUnmanagedAsync(ProjectBinding binding, ConfigEnvironment env)
    {
        if (binding.ActiveEnvironment is not null)
        {
            return true;
        }

        var result = await runner.RunAsync("Read local keys", OperationKind.Read, ct => sync.UnmanagedLocalKeysAsync(binding, ct), CancellationToken.None);
        switch (result)
        {
            case OperationResult<IReadOnlyList<string>>.Success { Value.Count: 0 }:
                return true;
            case OperationResult<IReadOnlyList<string>>.Success { Value: var keys }:
                return await dialogs.ConfirmAsync($"Make {env.Name} active", $"{keys.Count} local {(keys.Count == 1 ? "key" : "keys")} will be replaced by {env.Name}.", "Replace");
            case OperationResult<IReadOnlyList<string>>.Failed failed:
                await notifications.ToastAsync($"Could not read the local keys: {failed.Message}", ToastIntent.Error);
                return false;
            default:
                return false;
        }
    }

    private async Task<BlockedPullChoice> AskBlockedAsync(IReadOnlyList<string> keys)
    {
        var result = await dialogs.ShowDialogAsync<BlockedPullDialog>(o =>
        {
            o.Header.Title = "Local changes would be overwritten";
            o.Parameters.Add(nameof(BlockedPullDialog.Keys), keys);
        });
        return result.Cancelled ? BlockedPullChoice.Cancel : result.GetValue<BlockedPullChoice>();
    }

    private void OpenPush(string projectId, ProjectBinding binding)
    {
        navigator.OpenProject(projectId, binding.ActiveEnvironment, ProjectMode.Sync);
        sessions.GetOrCreate(projectId).SetSyncView(SyncView.Push);
    }
}
