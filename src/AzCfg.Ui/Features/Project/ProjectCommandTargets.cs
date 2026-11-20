using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Project;

/// <summary>
/// Handlers of project-scoped commands per project (scoped): a mode's view registers what e.g. <c>browse.edit</c> or
/// <c>project.refresh</c> does for its project, and the command contributors run the handler of the active project tab.
/// </summary>
public sealed class ProjectCommandTargets
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
