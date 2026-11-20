using System.Diagnostics.CodeAnalysis;

namespace AzCfg.Ui.Application.State;

/// <summary>The <see cref="ProjectSession"/> of every open project tab of one circuit, by project id (ordinal).</summary>
public sealed class ProjectSessions
{
    private readonly Dictionary<string, ProjectSession> _sessions = new(StringComparer.Ordinal);

    public event Action? Changed;

    public ProjectSession GetOrCreate(string projectId)
    {
        if (_sessions.TryGetValue(projectId, out var session))
        {
            return session;
        }

        session = new ProjectSession(projectId);
        _sessions[projectId] = session;
        Changed?.Invoke();
        return session;
    }

    public bool TryGet(string projectId, [MaybeNullWhen(false)] out ProjectSession session) =>
        _sessions.TryGetValue(projectId, out session);

    public void Close(string projectId)
    {
        if (_sessions.Remove(projectId))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Clears the cached views of every session (after a profile change); drafts are kept.</summary>
    public void InvalidateAll()
    {
        foreach (var session in _sessions.Values)
        {
            session.Invalidate();
        }

        Changed?.Invoke();
    }
}
