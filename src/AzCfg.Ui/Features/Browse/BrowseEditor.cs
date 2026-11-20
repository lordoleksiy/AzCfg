using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Sync;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Browse;

/// <summary>
/// The write side of Browse (AD-5): Azure edits go into the environment's <see cref="RemoteDraft"/> through
/// <see cref="IDraftService"/> and <see cref="ProjectSession.SetDraft"/> (the draft starts from a fresh export on the first edit and
/// is dropped when nothing is pending any more); local edits are immediate writes through <see cref="ILocalValuesService"/> and
/// invalidate the session. Every call goes through <see cref="IOperationRunner"/>; a failure becomes an error toast (messages name keys,
/// never values) and the method returns <c>false</c>.
/// </summary>
public sealed class BrowseEditor(
    IDraftService drafts,
    ILocalValuesService local,
    ISecretRevealService reveal,
    IWorkspaceService workspaceService,
    WorkspaceState workspace,
    IOperationRunner runner,
    INotificationService notifications)
{
    public Task<bool> SetValueAsync(ProjectSession session, ConfigurationView view, DraftKey key, string value, CancellationToken ct = default) =>
        EditAsync(session, view, d => drafts.SetValue(d, key, value), ct);

    public Task<bool> SetSecretAsync(ProjectSession session, ConfigurationView view, DraftKey key, SecretString value, CancellationToken ct = default) =>
        EditAsync(session, view, d => drafts.SetSecretValue(d, key, value), ct);

    public Task<bool> MoveToKeyVaultAsync(ProjectSession session, ConfigurationView view, DraftKey key, CancellationToken ct = default) =>
        EditAsync(session, view, d => drafts.MoveToKeyVault(d, key), ct);

    public Task<bool> DeleteAsync(ProjectSession session, ConfigurationView view, DraftKey key, CancellationToken ct = default) =>
        EditAsync(session, view, d => drafts.Remove(d, key), ct);

    public Task<bool> RevertAsync(ProjectSession session, ConfigurationView view, DraftKey key, CancellationToken ct = default) =>
        EditAsync(session, view, d => drafts.Revert(d, key), ct);

    /// <summary>
    /// Monaco edits: valid JSON replaces the draft text (starting the draft when needed) and returns no diagnostics; invalid JSON
    /// leaves the draft unchanged and returns the diagnostics for the editor markers.
    /// </summary>
    public async Task<IReadOnlyList<JsonDiagnostic>> ReplaceJsonAsync(ProjectSession session, ConfigurationView view, string json, CancellationToken ct = default)
    {
        if (await DraftOrStartAsync(session, view, ct) is not { } draft)
        {
            return [];
        }

        var candidate = drafts.ReplaceJson(draft, json);
        var diagnostics = drafts.Validate(candidate);
        if (diagnostics.Count == 0)
        {
            Commit(session, view.Environment.Name, candidate);
        }

        return diagnostics;
    }

    public Task<bool> SetLocalPlainAsync(ProjectSession session, ConfigurationView view, string key, string value) =>
        WriteLocalAsync(session, $"Set {key} locally", ct => local.SetPlainAsync(view.Binding, key, value, ct));

    public Task<bool> SetLocalSecretAsync(ProjectSession session, ConfigurationView view, string key, SecretString value) =>
        WriteLocalAsync(session, $"Set {key} locally", ct => local.SetSecretAsync(view.Binding, key, value, ct));

    public Task<bool> RemoveLocalAsync(ProjectSession session, ConfigurationView view, string key) =>
        WriteLocalAsync(session, $"Remove {key} locally", ct => local.RemoveAsync(view.Binding, key, ct));

    public Task<bool> SetLocalScopeAsync(ProjectSession session, ConfigurationView view, string key, ConfigScope scope) =>
        WriteLocalAsync(session, $"Mark {key} as {(scope == ConfigScope.Global ? "global" : "service")}", ct => local.SetScopeAsync(view.Binding, key, scope, ct));

    /// <summary>Adds the key (exact path) to the project's local ignore rules.</summary>
    public Task<bool> IgnoreAsync(ProjectSession session, ConfigurationView view, string key) =>
        ChangeIgnoreAsync(session, $"Ignore {key}", ct => workspaceService.AddIgnoreAsync(view.Binding, key, ct));

    /// <summary>Removes the key's exact ignore rule.</summary>
    public Task<bool> StopIgnoringAsync(ProjectSession session, ConfigurationView view, string key) =>
        ChangeIgnoreAsync(session, $"Stop ignoring {key}", ct => workspaceService.RemoveIgnoreAsync(view.Binding, key, ct));

    /// <summary>Reveal of an Azure secret; the value is only returned to the caller's <c>SecretValue</c>.</summary>
    public async Task<SecretString?> RevealRemoteAsync(Uri secretUri, string key, CancellationToken ct) =>
        await ReadAsync($"Reveal {key}", async c => await reveal.RevealRemoteAsync(secretUri, c), ct);

    /// <summary>Reveal of a local secret.</summary>
    public async Task<SecretString?> RevealLocalAsync(ConfigurationView view, string key, CancellationToken ct) =>
        await ReadAsync($"Reveal {key} locally", c => reveal.RevealLocalAsync(view.Binding, key, c), ct);

    private async Task<SecretString?> ReadAsync(string title, Func<CancellationToken, Task<SecretString?>> work, CancellationToken ct)
    {
        switch (await runner.RunAsync(title, OperationKind.Read, work, ct))
        {
            case OperationResult<SecretString?>.Success success:
                return success.Value;
            case OperationResult<SecretString?>.Failed failed:
                await notifications.ToastAsync($"{title} failed: {failed.Message}", ToastIntent.Error);
                return null;
            default:
                return null;
        }
    }

    private async Task<bool> EditAsync(ProjectSession session, ConfigurationView view, Func<RemoteDraft, RemoteDraft> edit, CancellationToken ct)
    {
        if (await DraftOrStartAsync(session, view, ct) is not { } draft)
        {
            return false;
        }

        try
        {
            Commit(session, view.Environment.Name, edit(draft));
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            await notifications.ToastAsync(e.Message, ToastIntent.Error);
            return false;
        }
    }

    private async Task<RemoteDraft?> DraftOrStartAsync(ProjectSession session, ConfigurationView view, CancellationToken ct)
    {
        if (session.DraftFor(view.Environment.Name) is { } existing)
        {
            return existing;
        }

        var result = await runner.RunAsync($"Start edits of {view.Environment.Name}", OperationKind.Read,
            c => drafts.StartAsync(view.Binding, view.Environment, c), ct);
        switch (result)
        {
            case OperationResult<RemoteDraft>.Success success:
                return success.Value;
            case OperationResult<RemoteDraft>.Failed failed:
                await notifications.ToastAsync($"Could not start editing {view.Environment.Name}: {failed.Message}", ToastIntent.Error);
                return null;
            default:
                return null;
        }
    }

    private void Commit(ProjectSession session, string environment, RemoteDraft draft)
    {
        var pending = drafts.PendingKeys(draft).Count;
        session.SetDraft(environment, pending == 0 ? null : draft, pending);
    }

    private async Task<bool> WriteLocalAsync(ProjectSession session, string title, Func<CancellationToken, Task> work)
    {
        var result = await runner.RunAsync(title, OperationKind.Write, work, CancellationToken.None);
        if (result is OperationResult<bool>.Failed failed)
        {
            await notifications.ToastAsync($"{title} failed: {failed.Message}", ToastIntent.Error);
            return false;
        }

        session.Invalidate();
        return result is OperationResult<bool>.Success;
    }

    private async Task<bool> ChangeIgnoreAsync(ProjectSession session, string title, Func<CancellationToken, Task<ProjectBinding>> work)
    {
        var result = await runner.RunAsync(title, OperationKind.Write, work, CancellationToken.None);
        switch (result)
        {
            case OperationResult<ProjectBinding>.Success success:
                workspace.UpdateBinding(session.ProjectId, success.Value);
                session.Invalidate();
                return true;
            case OperationResult<ProjectBinding>.Failed failed:
                await notifications.ToastAsync($"{title} failed: {failed.Message}", ToastIntent.Error);
                return false;
            default:
                return false;
        }
    }
}
