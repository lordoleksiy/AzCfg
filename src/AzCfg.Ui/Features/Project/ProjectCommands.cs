using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Project;

/// <summary>
/// Mode switch, refresh, unlock / lock and the draft commands of the active project tab. Refresh runs the handler the mode's
/// view registered (so a failed load is retried), else drops the session's caches.
/// </summary>
public sealed class ProjectCommands(ActiveProjectSession active, ProjectCommandTargets targets, ProtectionFlow protection, DraftFlow drafts) : ICommandContributor
{
    private const string Category = "Project";

    public IEnumerable<UiCommand> Commands =>
    [
        Mode(CommandIds.ModeBrowse, "Show Browse", ProjectMode.Browse),
        Mode(CommandIds.ModeCompare, "Show Compare", ProjectMode.Compare),
        Mode(CommandIds.ModeSync, "Show Sync", ProjectMode.Sync),
        Command(CommandIds.ProjectRefresh, "Refresh project", RefreshAsync, () => active.Session is not null),
        Command(CommandIds.ProjectUnlock, "Unlock editing", UnlockAsync, () => active.Environment is { } env && protection.IsLocked(env)),
        Command(CommandIds.ProjectLock, "Lock editing", Lock, () => active.Environment is { IsProtected: true } env && !protection.IsLocked(env)),
        Command(CommandIds.DraftReview, "Review pending edits", Review, HasPending),
        Command(CommandIds.DraftDiscard, "Discard pending edits", DiscardAsync, HasPending),
    ];

    private static UiCommand Command(string id, string title, Func<Task> execute, Func<bool>? enabled = null) =>
        new(id, title, Category, KeyBindings.ShortcutOf(id), execute, enabled);

    private UiCommand Mode(string id, string title, ProjectMode mode) =>
        Command(id, title, () =>
        {
            active.Session?.SetMode(mode);
            return Task.CompletedTask;
        }, () => active.Session is not null);

    private Task RefreshAsync()
    {
        if (active.Session is not { } session)
        {
            return Task.CompletedTask;
        }

        if (targets.Find(session.ProjectId, CommandIds.ProjectRefresh) is { } target)
        {
            return target.Execute();
        }

        session.Invalidate();
        return Task.CompletedTask;
    }

    private async Task UnlockAsync()
    {
        if (active.Environment is { } env)
        {
            await protection.UnlockAsync(env);
        }
    }

    private Task Lock()
    {
        if (active.Environment is { } env)
        {
            protection.Lock(env);
        }

        return Task.CompletedTask;
    }

    private bool HasPending() => active is { Session: { } session, Environment: { } env } && session.PendingCount(env.Name) > 0;

    private Task Review()
    {
        if (active.Session is { } session)
        {
            DraftFlow.Review(session);
        }

        return Task.CompletedTask;
    }

    private Task DiscardAsync() =>
        active is { Session: { } session, Environment: { } env } ? drafts.DiscardAsync(session, env.Name) : Task.CompletedTask;
}
