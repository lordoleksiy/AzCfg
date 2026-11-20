using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Sync;

/// <summary>
/// Handlers of the Sync commands per project (scoped): the Sync views of each open project tab register what their
/// command does (e.g. <c>sync.apply</c>), and <see cref="SyncCommands"/> runs the handler of the active project tab.
/// </summary>
public sealed class SyncCommandTargets
{
    private readonly List<(string ProjectId, UiCommand Command)> _targets = [];

    /// <summary>Disposing the result removes the handler.</summary>
    public IDisposable Register(string projectId, UiCommand command)
    {
        var target = (projectId, command);
        _targets.Add(target);
        return new Registration(() => _targets.Remove(target));
    }

    /// <summary>The latest handler of <paramref name="commandId"/> registered for <paramref name="projectId"/>; null when none.</summary>
    public UiCommand? Find(string projectId, string commandId) =>
        _targets.LastOrDefault(t => t.ProjectId == projectId && t.Command.Id == commandId).Command;

    private sealed class Registration(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
