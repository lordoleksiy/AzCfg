using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.KeyTree;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AzCfg.Ui.Features.Compare;

/// <summary>
/// Compare mode of a project tab (§2.6, FR-C1…C4): two side pickers (Local or any environment), swap, "differences only", key
/// search, the key tree with a Result column or the JSON diff, and <see cref="CompareInspector"/>. The comparison is cached in
/// <see cref="ProjectSession.SetComparison"/> and reloaded whenever the session drops it (environment change, a write → Invalidate).
/// A change of the selected environment resets the sides to <see cref="CompareSides.Default"/>. Secret values are never shown.
/// </summary>
public sealed partial class CompareView : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private (CompareSide Left, CompareSide Right)? _loading;
    private (CompareSide Left, CompareSide Right)? _failedFor;
    private OperationResult<ComparisonView>.Failed? _error;
    private string? _environment;
    private bool _differencesOnly = true;
    private bool _matrix;
    private KeyTreeModel<CompareRow>? _model;
    private (ComparisonView View, bool DifferencesOnly, string? Text)? _modelSource;

    [Inject] private ICompareService CompareService { get; set; } = default!;

    [Inject] private CompareCopier Copier { get; set; } = default!;

    [Inject] private IOperationRunner Runner { get; set; } = default!;

    [Inject] private WorkspaceState Workspace { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectSession Session { get; set; } = default!;

    private ProjectBinding? Binding => Workspace.FindProject(Session.ProjectId)?.Binding;

    private IReadOnlyList<ConfigEnvironment> Environments => Workspace.Environments?.Environments ?? [];

    /// <summary>The session's sides, else the defaults; null when unbound or without environments.</summary>
    private (CompareSide Left, CompareSide Right)? Sides =>
        Session.CompareLeft is { } left && Session.CompareRight is { } right ? (left, right)
        : Binding is { } binding ? CompareSides.Default(binding, Environments, Session.EnvironmentName)
        : null;

    protected override async Task OnInitializedAsync()
    {
        _environment = Session.EnvironmentName;
        Session.Changed += OnStateChanged;
        Workspace.Changed += OnStateChanged;
        await LoadIfNeededAsync();
    }

    private void OnStateChanged() => _ = InvokeAsync(RefreshAsync);

    private async Task RefreshAsync()
    {
        if (!string.Equals(Session.EnvironmentName, _environment, StringComparison.OrdinalIgnoreCase))
        {
            _environment = Session.EnvironmentName;
            if (Binding is { } binding && CompareSides.Default(binding, Environments, _environment) is { } defaults)
            {
                SetSides(defaults.Left, defaults.Right);
                return;
            }
        }

        await LoadIfNeededAsync();
        StateHasChanged();
    }

    private async Task LoadIfNeededAsync()
    {
        if (_matrix || Session.Comparison is not null || Binding is not { } binding || Sides is not { } sides
            || sides is { Left: CompareSide.Local, Right: CompareSide.Local } || sides.Equals(_loading) || sides.Equals(_failedFor))
        {
            return;
        }

        _loading = sides;
        _error = null;
        StateHasChanged();
        var result = await Runner.RunAsync($"Compare {CompareSides.Name(sides.Left)} ↔ {CompareSides.Name(sides.Right)}", OperationKind.Read,
            ct => CompareService.CompareAsync(binding, sides.Left, sides.Right, ct), _cts.Token);
        if (!sides.Equals(_loading))
        {
            return;
        }

        _loading = null;
        switch (result)
        {
            case OperationResult<ComparisonView>.Success success:
                Session.SetComparison(sides.Left, sides.Right, success.Value);
                break;
            case OperationResult<ComparisonView>.Failed failed:
                _error = failed;
                _failedFor = sides;
                break;
        }

        StateHasChanged();
    }

    private Task RetryAsync()
    {
        _failedFor = null;
        return LoadIfNeededAsync();
    }

    private void SetSides(CompareSide left, CompareSide right)
    {
        _error = null;
        _failedFor = null;
        Session.SetComparison(left, right, null);
    }

    private void SetLeft(ChangeEventArgs e) => SetSides(Parse(e.Value), Sides!.Value.Right);

    private void SetRight(ChangeEventArgs e) => SetSides(Sides!.Value.Left, Parse(e.Value));

    private void Swap() => SetSides(Sides!.Value.Right, Sides!.Value.Left);

    private CompareSide Parse(object? value) =>
        Environments.FirstOrDefault(env => env.Name == value as string) is { } env ? new CompareSide.Environment(env) : new CompareSide.Local();

    private async Task SetMatrix(bool on)
    {
        _matrix = on;
        await LoadIfNeededAsync();
    }

    /// <summary>Copies the plain value of the row's left side into the draft of the right environment (FR-C5).</summary>
    private async Task CopyToRightAsync(CompareRow row)
    {
        if (Binding is { } binding && Session.Comparison is { Right: CompareSide.Environment right } && row.Left is { IsSecret: false, Value: { } value, Scope: ConfigScope.Service }
            && await Copier.CopyAsync(Session, binding, right.Env, row.Key, value, _cts.Token))
        {
            Session.Select(row.Key);
        }
    }

    private void ToggleDifferences() => _differencesOnly = !_differencesOnly;

    private void Search(ChangeEventArgs e) => Session.SetFilter(Session.Filter with { Text = e.Value as string });

    private void ClearOnEscape(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            Session.SetFilter(Session.Filter with { Text = null });
        }
    }

    private void OpenInBrowse(string key)
    {
        Session.Select(key);
        Session.SetMode(ProjectMode.Browse);
    }

    private KeyTreeModel<CompareRow> Model(ComparisonView view)
    {
        var source = (view, _differencesOnly, Session.Filter.Text);
        if (_model is null || !source.Equals(_modelSource))
        {
            var rows = view.Rows.Where(r => (!_differencesOnly || r.Result != CompareResult.Same) && Matches(r, Session.Filter.Text));
            _model = new KeyTreeModel<CompareRow>(rows, r => r.Key, r => r.Result != CompareResult.Same, initiallyExpanded: true);
            _modelSource = source;
        }

        return _model;
    }

    /// <summary>Key, or a plain value of either side; secrets never match by value.</summary>
    private static bool Matches(CompareRow row, string? text) =>
        string.IsNullOrEmpty(text)
        || row.Key.Contains(text, StringComparison.OrdinalIgnoreCase)
        || PlainValue(row.Left)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
        || PlainValue(row.Right)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

    private static string? PlainValue(CompareCell? cell) => cell is { IsSecret: false } ? cell.Value : null;

    private CompareRow? SelectedRow(ComparisonView view) => view.Rows.FirstOrDefault(r => r.Key == Session.SelectedKey);

    private static string Counts(ComparisonView view) =>
        $"{view.Rows.Count(r => r.Result == CompareResult.Different)} different · {view.Rows.Count(r => r.Result == CompareResult.OnlyLeft)} only left · "
        + $"{view.Rows.Count(r => r.Result == CompareResult.OnlyRight)} only right";

    private static string Glyph(CompareResult result) => result switch
    {
        CompareResult.Same => "=",
        CompareResult.Different => "≠",
        CompareResult.OnlyLeft => "←",
        _ => "→",
    };

    private static string ResultClass(CompareResult result) => result switch
    {
        CompareResult.Same => "same",
        CompareResult.Different => "different",
        CompareResult.OnlyLeft => "only-left",
        _ => "only-right",
    };

    public void Dispose()
    {
        Session.Changed -= OnStateChanged;
        Workspace.Changed -= OnStateChanged;
        _cts.Cancel();
        _cts.Dispose();
    }
}
