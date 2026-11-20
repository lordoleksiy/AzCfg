using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;

namespace AzCfg.Ui.Features.Environments;

/// <summary>
/// Loads, saves and probes the environments of the circuit (scoped). Loading and probing are Read operations; saving is a
/// Write (AD-9) and, once saved, replaces the snapshot of <see cref="WorkspaceState"/> and invalidates every project session,
/// because cached views, comparisons and keys of removed or changed environments are stale (FR-E2).
/// </summary>
public sealed class EnvironmentLoader(IEnvironmentService environments, WorkspaceState workspace, ProjectSessions sessions, IAzureStatusService status, IOperationRunner runner)
{
    /// <summary>Reads the saved profile into <see cref="WorkspaceState.Environments"/>.</summary>
    public async Task<OperationResult<EnvironmentsSnapshot>> LoadAsync(CancellationToken ct)
    {
        var result = await runner.RunAsync("Load environments", OperationKind.Read, environments.LoadAsync, ct);
        if (result is OperationResult<EnvironmentsSnapshot>.Success success)
        {
            workspace.SetEnvironments(success.Value);
        }

        return result;
    }

    /// <summary>Loads the profile unless it is already in <see cref="WorkspaceState.Environments"/>; <c>null</c> when nothing ran.</summary>
    public Task<OperationResult<EnvironmentsSnapshot>>? LoadIfNeededAsync(CancellationToken ct) =>
        workspace.Environments is null ? LoadAsync(ct) : null;

    /// <summary>Saves the whole profile (Write) and, on success, publishes the new snapshot and invalidates the project sessions.</summary>
    public async Task<OperationResult<EnvironmentsSnapshot>> SaveAsync(UserProfile profile)
    {
        var result = await runner.RunAsync("Save environments", OperationKind.Write, ct => environments.SaveAsync(profile, ct), CancellationToken.None);
        if (result is OperationResult<EnvironmentsSnapshot>.Success success)
        {
            workspace.SetEnvironments(success.Value);
            sessions.InvalidateAll();
        }

        return result;
    }

    /// <summary>Probes one environment (Read); the result lands in <see cref="IAzureStatusService.Environments"/>.</summary>
    public Task<OperationResult<EnvironmentHealth>> CheckAsync(ConfigEnvironment env, string? service, CancellationToken ct) =>
        runner.RunAsync($"Check {env.Name}", OperationKind.Read, token => status.CheckEnvironmentAsync(env, service, token), ct);
}
