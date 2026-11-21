using AzCfg.Core.Model;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Features.Sync;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Compare;

/// <summary>
/// Copy to environment (FR-C5, FR-C6): puts a plain value, or a secret value typed by the user, into the target environment's
/// <see cref="RemoteDraft"/> through <see cref="IDraftService"/> (the draft starts from a fresh export on the first edit). A protected,
/// locked target asks for the unlock first (<see cref="ProtectionFlow"/>). Secret values are never copied from another environment.
/// A failure becomes an error toast naming the key, and the method returns <c>false</c>.
/// </summary>
public sealed class CompareCopier(IDraftService drafts, ProtectionFlow protection, IOperationRunner runner, INotificationService notifications)
{
    /// <summary>Sets the plain <paramref name="value"/> of <paramref name="key"/> in the draft of <paramref name="target"/>.</summary>
    public Task<bool> CopyAsync(ProjectSession session, ProjectBinding binding, ConfigEnvironment target, string key, string value, CancellationToken ct = default) =>
        EditAsync(session, binding, target, d => drafts.SetValue(d, new DraftKey(ConfigScope.Service, key), value), ct);

    /// <summary>Sets a new secret value typed by the user (a Key Vault reference in the draft).</summary>
    public Task<bool> SetSecretAsync(ProjectSession session, ProjectBinding binding, ConfigEnvironment target, string key, SecretString value, CancellationToken ct = default) =>
        EditAsync(session, binding, target, d => drafts.SetSecretValue(d, new DraftKey(ConfigScope.Service, key), value), ct);

    private async Task<bool> EditAsync(ProjectSession session, ProjectBinding binding, ConfigEnvironment target, Func<RemoteDraft, RemoteDraft> edit, CancellationToken ct)
    {
        if (!await protection.UnlockAsync(target))
        {
            return false;
        }

        var draft = session.DraftFor(target.Name);
        if (draft is null)
        {
            var started = await runner.RunAsync($"Start edits of {target.Name}", OperationKind.Read, c => drafts.StartAsync(binding, target, c), ct);
            if (started is OperationResult<RemoteDraft>.Failed failed)
            {
                await notifications.ToastAsync($"Could not start editing {target.Name}: {failed.Message}", ToastIntent.Error);
            }

            if (started is not OperationResult<RemoteDraft>.Success success)
            {
                return false;
            }

            draft = success.Value;
        }

        try
        {
            var edited = edit(draft);
            var pending = drafts.PendingKeys(edited).Count;
            session.SetDraft(target.Name, pending == 0 ? null : edited, pending);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            await notifications.ToastAsync(e.Message, ToastIntent.Error);
            return false;
        }
    }
}
