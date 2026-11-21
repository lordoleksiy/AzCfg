using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Application.State;

/// <summary>
/// State of one open project tab (AD-4): selected environment, mode, filter, selection, the cached Browse view and
/// comparison, and the drafts per environment (OrdinalIgnoreCase). Caches are cleared on environment change and
/// <see cref="Invalidate"/>; drafts survive both.
/// </summary>
public sealed class ProjectSession(string projectId)
{
    private readonly Dictionary<string, RemoteDraft> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pending = new(StringComparer.OrdinalIgnoreCase);

    public string ProjectId { get; } = projectId;

    public string? EnvironmentName { get; private set; }

    public ProjectMode Mode { get; private set; } = ProjectMode.Browse;

    public SyncView SyncView { get; private set; } = SyncView.Push;

    public ConfigFilter Filter { get; private set; } = new();

    public string? SelectedKey { get; private set; }

    public bool JsonView { get; private set; }

    public ConfigurationView? View { get; private set; }

    /// <summary>Cached <see cref="ISyncService.StatusAsync"/> result of the active environment; null until loaded or after <see cref="Invalidate"/>.</summary>
    public SyncSummary? Summary { get; private set; }

    public CompareSide? CompareLeft { get; private set; }

    public CompareSide? CompareRight { get; private set; }

    public ComparisonView? Comparison { get; private set; }

    public IReadOnlyDictionary<string, RemoteDraft> Drafts => _drafts;

    public int TotalPending => _pending.Values.Sum();

    public event Action? Changed;

    /// <summary>A different name (OrdinalIgnoreCase) clears <see cref="View"/> and <see cref="Comparison"/>.</summary>
    public void SetEnvironment(string name)
    {
        if (!string.Equals(EnvironmentName, name, StringComparison.OrdinalIgnoreCase))
        {
            View = null;
            Comparison = null;
        }

        EnvironmentName = name;
        Changed?.Invoke();
    }

    public void SetMode(ProjectMode mode) => Update(() => Mode = mode);

    public void SetSyncView(SyncView view) => Update(() => SyncView = view);

    public void SetFilter(ConfigFilter filter) => Update(() => Filter = filter);

    public void Select(string? key) => Update(() => SelectedKey = key);

    public void SetJsonView(bool on) => Update(() => JsonView = on);

    public void SetView(ConfigurationView? view) => Update(() => View = view);

    public void SetSummary(SyncSummary? summary) => Update(() => Summary = summary);

    public void SetComparison(CompareSide left, CompareSide right, ComparisonView? view) => Update(() =>
    {
        CompareLeft = left;
        CompareRight = right;
        Comparison = view;
    });

    /// <summary>A <c>null</c> <paramref name="draft"/> removes the environment's draft and pending count.</summary>
    public void SetDraft(string environment, RemoteDraft? draft, int pendingCount) => Update(() =>
    {
        if (draft is null)
        {
            _drafts.Remove(environment);
            _pending.Remove(environment);
            return;
        }

        _drafts[environment] = draft;
        _pending[environment] = pendingCount;
    });

    public RemoteDraft? DraftFor(string environment) => _drafts.GetValueOrDefault(environment);

    public int PendingCount(string environment) => _pending.GetValueOrDefault(environment);

    /// <summary><see cref="View"/> = <see cref="Comparison"/> = <see cref="Summary"/> = null; drafts are kept.</summary>
    public void Invalidate() => Update(() =>
    {
        View = null;
        Summary = null;
        Comparison = null;
    });

    private void Update(Action change)
    {
        change();
        Changed?.Invoke();
    }
}
