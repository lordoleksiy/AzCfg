using AzCfg.Ui.Application.Health;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Workspace;

namespace AzCfg.Ui.Features.Problems;

/// <summary>
/// The last health report of the active project (one per circuit, shared by the PROBLEMS section and the status bar item). The
/// check runs through the operation runner as a Read; it re-runs when the active project changes and when its session's loaded
/// view is replaced (every write invalidates it), never on its own otherwise.
/// </summary>
public sealed class ProblemsState : IDisposable
{
    private readonly IProblemsService _service;
    private readonly IOperationRunner _runner;
    private readonly WorkspaceState _workspace;
    private readonly TabsState _tabs;
    private readonly ProjectSessions _sessions;
    private CancellationTokenSource? _cts;
    private string? _projectId;
    private object? _lastView;
    private ProjectSession? _session;
    private bool _started;

    public ProblemsState(IProblemsService service, IOperationRunner runner, WorkspaceState workspace, TabsState tabs, ProjectSessions sessions)
    {
        _service = service;
        _runner = runner;
        _workspace = workspace;
        _tabs = tabs;
        _sessions = sessions;
    }

    /// <summary>The project the report is about; null when no bound project is active.</summary>
    public string? ProjectId { get; private set; }

    public ProblemsReport? Report { get; private set; }

    public string? Error { get; private set; }

    public bool Loading { get; private set; }

    public event Action? Changed;

    /// <summary>Hooks the change notifications and runs the first check; later calls do nothing.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _workspace.Changed += OnChanged;
        _tabs.Changed += OnChanged;
        _sessions.Changed += OnChanged;
        OnChanged();
    }

    /// <summary>Runs the check again now (the Refresh command).</summary>
    public Task RefreshAsync() => CheckAsync(CurrentProjectId());

    private string? CurrentProjectId() =>
        ActiveProject.IdOf(_tabs, _workspace) is { } id && _workspace.FindProject(id)?.Binding is not null ? id : null;

    private void OnChanged()
    {
        var id = CurrentProjectId();
        Hook(id is not null && _sessions.TryGet(id, out var session) ? session : null);
        var view = _session?.View;
        var projectChanged = !string.Equals(id, _projectId, StringComparison.Ordinal);
        var viewReplaced = !projectChanged && _lastView is not null && view is not null && !ReferenceEquals(view, _lastView);
        _lastView = view ?? _lastView;
        if (projectChanged || viewReplaced)
        {
            _ = CheckAsync(id);
        }
    }

    private void Hook(ProjectSession? next)
    {
        if (next == _session)
        {
            return;
        }

        if (_session is not null)
        {
            _session.Changed -= OnChanged;
        }

        _session = next;
        _lastView = next?.View;
        if (_session is not null)
        {
            _session.Changed += OnChanged;
        }
    }

    private async Task CheckAsync(string? id)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = new CancellationTokenSource();
        _projectId = id;
        ProjectId = id;
        Error = null;
        if (id is null || _workspace.FindProject(id)?.Binding is not { } binding)
        {
            Report = null;
            Loading = false;
            Changed?.Invoke();
            return;
        }

        Loading = true;
        Changed?.Invoke();
        var result = await _runner.RunAsync("Check project health", OperationKind.Read, ct => _service.CheckAsync(binding.RepoPath, ct), cts.Token);
        if (cts.IsCancellationRequested)
        {
            return;
        }

        switch (result)
        {
            case OperationResult<ProblemsReport>.Success ok:
                Report = ok.Value;
                break;
            case OperationResult<ProblemsReport>.Failed failed:
                Report = null;
                Error = failed.Message;
                break;
        }

        Loading = false;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _workspace.Changed -= OnChanged;
        _tabs.Changed -= OnChanged;
        _sessions.Changed -= OnChanged;
        if (_session is not null)
        {
            _session.Changed -= OnChanged;
        }

        _cts?.Cancel();
        _cts?.Dispose();
    }
}
