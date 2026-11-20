using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Components.Editors;
using AzCfg.Ui.Components.KeyTree;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Features.Sync;
using AzCfg.Ui.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AzCfg.Ui.Features.Browse;

/// <summary>
/// Browse mode of a project tab (§2.5, FR-B1…B7): toolbar (search, Changed / Secrets / Ignored, scope, Tree │ JSON, expand / collapse,
/// Add key), the key tree with Local, Azure and Status columns (inline edit, context menu), the Azure draft / Local JSON views and
/// <see cref="BrowseInspector"/>. The view is loaded through the operation runner into <see cref="ProjectSession.SetView"/> and
/// reloaded whenever the session drops it. Azure edits go into the draft (<see cref="BrowseEditor"/>); a protected, locked
/// environment disables them. Secret values are never rendered here — only masks; Reveal lives in the inspector.
/// </summary>
public sealed partial class BrowseView : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly List<IDisposable> _targets = [];
    private ElementReference _search;
    private ElementReference _editInput;
    private CodeEditor? _editor;
    private string? _loadingFor;
    private string? _failedFor;
    private OperationResult<ConfigurationView>.Failed? _error;
    private string? _editingKey;
    private string _editText = string.Empty;
    private bool _focusEdit;
    private bool _adding;
    private string _addKey = string.Empty;
    private string _addValue = string.Empty;
    private bool _addSecret;
    private (double X, double Y, ConfigRow Row)? _menu;
    private InspectorRequest? _request;
    private bool _jsonLocal;
    private bool _disposed;

    private ConfigurationView? _rowsView;
    private RemoteDraft? _rowsDraft;
    private IReadOnlyList<ConfigRow> _rows = [];
    private IReadOnlyDictionary<DraftKey, PendingEdit?> _pending = new Dictionary<DraftKey, PendingEdit?>();
    private KeyTreeModel<ConfigRow>? _model;
    private (IReadOnlyList<ConfigRow> Rows, ConfigFilter Filter)? _modelSource;
    private ConfigurationView? _jsonView;
    private string _remoteJson = string.Empty;
    private string _localJson = string.Empty;
    private string _jsonText = string.Empty;
    private string? _jsonSource;

    [Inject] private IConfigurationService Config { get; set; } = default!;

    [Inject] private IDraftService Drafts { get; set; } = default!;

    [Inject] private BrowseEditor Editor { get; set; } = default!;

    [Inject] private IOperationRunner Runner { get; set; } = default!;

    [Inject] private WorkspaceState Workspace { get; set; } = default!;

    [Inject] private ProtectedUnlockState Unlock { get; set; } = default!;

    [Inject] private ProtectionFlow Protection { get; set; } = default!;

    [Inject] private ProjectCommandTargets Targets { get; set; } = default!;

    [Inject] private IBindingFlow BindingFlow { get; set; } = default!;

    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectSession Session { get; set; } = default!;

    private ProjectBinding? Binding => Workspace.FindProject(Session.ProjectId)?.Binding;

    private ConfigEnvironment? Environment => SyncSelection.EnvironmentOf(Workspace, Session, Binding);

    /// <summary>Azure edits are possible unless the environment is protected and still locked (AD-7).</summary>
    private bool Editable => Environment is { } env && !Protection.IsLocked(env);

    private RemoteDraft? Draft => Environment is { } env ? Session.DraftFor(env.Name) : null;

    private ConfigRow? SelectedRow => Session.SelectedKey is { } key ? _rows.FirstOrDefault(r => r.Key == key) : null;

    protected override async Task OnInitializedAsync()
    {
        Session.Changed += OnChanged;
        Workspace.Changed += OnChanged;
        Unlock.Changed += OnChanged;
        RegisterCommands();
        await LoadIfNeededAsync();
    }

    private void RegisterCommands()
    {
        var id = Session.ProjectId;
        _targets.Add(Targets.Register(id, new UiCommand(CommandIds.ProjectRefresh, "Refresh", "Project", null, () => Run(Reload))));
        _targets.Add(Targets.Register(id, new UiCommand(CommandIds.BrowseFind, "Find keys", "Browse", null, () => Run(() => _search.FocusAsync().AsTask()))));
        _targets.Add(Targets.Register(id, new UiCommand(CommandIds.BrowseAddKey, "Add key", "Browse", null, () => Run(StartAdd), () => Session.View is not null && Editable)));
        _targets.Add(Targets.Register(id, new UiCommand(CommandIds.BrowseEdit, "Edit selected value", "Browse", null,
            () => Run(() => SelectedRow is { } row ? StartEditAsync(row) : Task.CompletedTask), () => Editable && SelectedRow is not null)));
        _targets.Add(Targets.Register(id, new UiCommand(CommandIds.BrowseDelete, "Delete selected key", "Browse", null,
            () => Run(() => SelectedRow is { } row ? DeleteAsync(row) : Task.CompletedTask), () => Editable && SelectedRow is not null)));
    }

    /// <summary>Runs a command handler on the renderer's context and re-renders.</summary>
    private Task Run(Func<Task> action) => InvokeAsync(async () =>
    {
        await action();
        StateHasChanged();
    });

    private void OnChanged() => _ = InvokeAsync(async () =>
    {
        if (_disposed)
        {
            return;
        }

        await LoadIfNeededAsync();
        StateHasChanged();
    });

    private async Task Reload()
    {
        _failedFor = null;
        _error = null;
        Session.Invalidate();
        await Task.CompletedTask;
    }

    private async Task LoadIfNeededAsync()
    {
        if (Session.View is not null || Binding is not { } binding || Environment is not { } env || _loadingFor is not null
            || string.Equals(_failedFor, env.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _loadingFor = env.Name;
        _error = null;
        StateHasChanged();
        var result = await Runner.RunAsync($"Load {env.Name}", OperationKind.Read, ct => Config.LoadAsync(binding, env, ct), _cts.Token);
        _loadingFor = null;
        if (_disposed)
        {
            return;
        }

        switch (result)
        {
            case OperationResult<ConfigurationView>.Success success when string.Equals(Environment?.Name, success.Value.Environment.Name, StringComparison.OrdinalIgnoreCase):
                Session.SetView(success.Value);
                break;
            case OperationResult<ConfigurationView>.Success:
                await LoadIfNeededAsync();
                break;
            case OperationResult<ConfigurationView>.Failed failed:
                _error = failed;
                _failedFor = env.Name;
                break;
        }

        StateHasChanged();
    }

    private Task RetryAsync()
    {
        _failedFor = null;
        return LoadIfNeededAsync();
    }

    /// <summary>Rows of the view plus a row per key added in the draft; recomputed when the view or the draft changes.</summary>
    private void Prepare(ConfigurationView view)
    {
        var draft = Draft;
        if (ReferenceEquals(view, _rowsView) && ReferenceEquals(draft, _rowsDraft))
        {
            return;
        }

        var pendingKeys = draft is null ? [] : Drafts.PendingKeys(draft);
        _pending = pendingKeys.ToDictionary(k => k, k => draft is null ? null : PendingEdit.Describe(draft, k));
        var existing = view.Rows.Select(BrowseRows.DraftKeyOf).ToHashSet();
        var added = pendingKeys.Where(k => !existing.Contains(k) && _pending[k] is { Removed: false })
            .Select(k => new ConfigRow(k.Key, null, _pending[k]!.AfterIsSecret, false, null,
                new RemoteCell(null, _pending[k]!.AfterIsSecret, null, null, null, k.Scope, null, null)));
        var rows = view.Rows.Concat(added).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        var structureChanged = !ReferenceEquals(view, _rowsView) || rows.Count != _rows.Count;
        _rowsView = view;
        _rowsDraft = draft;
        _rows = rows;
        if (structureChanged)
        {
            _model = null;
        }
    }

    /// <summary>The tree of the filtered rows (rebuilt only when rows or filter change); null when nothing matches.</summary>
    private KeyTreeModel<ConfigRow>? Model()
    {
        var source = (_rows, Session.Filter);
        if (_model is null || !source.Equals(_modelSource))
        {
            var filtered = ConfigFilters.Apply(_rows, Session.Filter);
            _model = filtered.Count == 0
                ? null
                : new KeyTreeModel<ConfigRow>(filtered, r => r.Key, r => r.Status is not null and not DiffKind.Unchanged, initiallyExpanded: true);
            _modelSource = source;
        }

        return _model;
    }

    private PendingEdit? PendingOf(ConfigRow row) => _pending.GetValueOrDefault(BrowseRows.DraftKeyOf(row));

    private bool HasDraftEdit(ConfigRow row) => _pending.ContainsKey(BrowseRows.DraftKeyOf(row));

    private static bool ExistsRemotely(ConfigRow row) => row.Remote is { IsSecret: true } or { Value: not null };

    private string RemoteJson(ConfigurationView view)
    {
        SyncJson(view);
        return _remoteJson;
    }

    private string LocalJson(ConfigurationView view)
    {
        SyncJson(view);
        return _localJson;
    }

    private void SyncJson(ConfigurationView view)
    {
        if (ReferenceEquals(view, _jsonView))
        {
            return;
        }

        _jsonView = view;
        _remoteJson = Config.ToJson(view, ConfigSide.Remote);
        _localJson = view.IsRemoteOnly ? string.Empty : Config.ToJson(view, ConfigSide.Local);
    }

    /// <summary>The text of the Azure editor: the draft's JSON, else the remote JSON; replaced only when that source changes.</summary>
    private string AzureJsonText(ConfigurationView view)
    {
        var source = Draft?.Json ?? RemoteJson(view);
        if (source != _jsonSource)
        {
            _jsonSource = source;
            _jsonText = source;
        }

        return _jsonText;
    }

    private async Task OnJsonChangedAsync(string json)
    {
        if (Session.View is not { } view)
        {
            return;
        }

        var diagnostics = await Editor.ReplaceJsonAsync(Session, view, json, _cts.Token);
        if (_editor is not null)
        {
            await _editor.SetMarkersAsync(diagnostics);
        }

        if (diagnostics.Count == 0)
        {
            var applied = Draft?.Json ?? RemoteJson(view);
            _jsonSource = applied;
            if (Draft is not null && applied != json)
            {
                _jsonText = applied;
            }
        }
    }

    private void Search(ChangeEventArgs e) => Session.SetFilter(Session.Filter with { Text = e.Value as string });

    private void ClearSearchOnEscape(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            Session.SetFilter(Session.Filter with { Text = null });
        }
    }

    private void SetScope(ChangeEventArgs e) =>
        Session.SetFilter(Session.Filter with { Scope = ScopeFrom(e.Value as string) });

    private static ConfigScope? ScopeFrom(string? value) => value switch
    {
        "service" => ConfigScope.Service,
        "global" => ConfigScope.Global,
        _ => null,
    };

    private void ToggleChanged() => Session.SetFilter(Session.Filter with { ChangedOnly = !Session.Filter.ChangedOnly });

    private void ToggleSecrets() => Session.SetFilter(Session.Filter with { SecretsOnly = !Session.Filter.SecretsOnly });

    private void ToggleIgnored() => Session.SetFilter(Session.Filter with { IgnoredOnly = !Session.Filter.IgnoredOnly });

    private void ExpandAll() => _model?.ExpandAll();

    private void CollapseAll() => _model?.CollapseAll();

    private Task ActivateAsync(KeyTreeRow<ConfigRow> row) => row.Item is { } item ? StartEditAsync(item) : Task.CompletedTask;

    private Task StartEditAsync(ConfigRow row)
    {
        if (!Editable)
        {
            return Task.CompletedTask;
        }

        Session.Select(row.Key);
        if (row.Remote?.IsSecret == true || PendingOf(row)?.AfterIsSecret == true)
        {
            _request = new InspectorRequest(row.Key, InspectorEdit.AzureSecret);
            return Task.CompletedTask;
        }

        _editingKey = row.Key;
        _editText = PendingOf(row) is { Removed: false, After: { } after } ? after : BrowseRows.PlainRemote(row) ?? string.Empty;
        _focusEdit = true;
        return Task.CompletedTask;
    }

    private async Task CommitEditAsync(ConfigRow row, ConfigurationView view)
    {
        var text = _editText;
        _editingKey = null;
        await Editor.SetValueAsync(Session, view, BrowseRows.DraftKeyOf(row), text, _cts.Token);
    }

    private void CancelEdit() => _editingKey = null;

    private async Task EditKeyDownAsync(KeyboardEventArgs e, ConfigRow row, ConfigurationView view)
    {
        if (e.Key == "Enter")
        {
            await CommitEditAsync(row, view);
        }
        else if (e.Key == "Escape")
        {
            CancelEdit();
        }
    }

    private async Task DeleteAsync(ConfigRow row)
    {
        if (Session.View is { } view && Editable)
        {
            await Editor.DeleteAsync(Session, view, BrowseRows.DraftKeyOf(row), _cts.Token);
        }
    }

    private Task StartAdd()
    {
        _adding = true;
        return Task.CompletedTask;
    }

    private void CancelAdd()
    {
        _adding = false;
        _addKey = string.Empty;
        _addValue = string.Empty;
        _addSecret = false;
    }

    private async Task AddAsync(ConfigurationView view)
    {
        var key = _addKey.Trim();
        if (key.Length == 0)
        {
            return;
        }

        var draftKey = new DraftKey(Session.Filter.Scope ?? ConfigScope.Service, key);
        var added = _addSecret
            ? await Editor.SetSecretAsync(Session, view, draftKey, new SecretString(_addValue), _cts.Token)
            : await Editor.SetValueAsync(Session, view, draftKey, _addValue, _cts.Token);
        if (added)
        {
            Session.Select(key);
            CancelAdd();
        }
    }

    private void OpenMenu(MouseEventArgs e, ConfigRow row)
    {
        Session.Select(row.Key);
        _menu = (e.ClientX, e.ClientY, row);
    }

    private void CloseMenu() => _menu = null;

    private async Task RunMenuAsync(Func<Task> action)
    {
        _menu = null;
        await action();
    }

    private Task CopyAsync(string? text) => text is null ? Task.CompletedTask : Js.InvokeVoidAsync("navigator.clipboard.writeText", text).AsTask();

    private void CompareAcrossEnvironments(ConfigRow row)
    {
        Session.Select(row.Key);
        Session.SetMode(ProjectMode.Compare);
    }

    private void RequestSecretEdit(ConfigRow row) => _request = new InspectorRequest(row.Key, InspectorEdit.AzureSecret);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusEdit && _editingKey is not null && _editInput.Id is not null)
        {
            await _editInput.FocusAsync();
        }
    }

    /// <summary>Keeps the inline input focused after every render until the user types (a later click on the row must not steal it).</summary>
    private void OnEditInput(ChangeEventArgs e)
    {
        _focusEdit = false;
        _editText = e.Value as string ?? string.Empty;
    }

    public void Dispose()
    {
        _disposed = true;
        Session.Changed -= OnChanged;
        Workspace.Changed -= OnChanged;
        Unlock.Changed -= OnChanged;
        foreach (var target in _targets)
        {
            target.Dispose();
        }

        _cts.Cancel();
        _cts.Dispose();
    }
}
