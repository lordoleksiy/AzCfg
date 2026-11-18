using System.Text;
using AzCfg.Ui.Application.Sync;
using BlazorMonaco;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AzCfg.Ui.Components.Editors;

/// <summary>
/// Monaco JSON editor (AD-11). Dirty is tracked against a baseline: <see cref="SetValueAsync"/> (and a changed
/// <see cref="Value"/>) resets it, editing back to it clears it. <see cref="OnChange"/> and <see cref="OnDirtyChanged"/>
/// fire debounced (250 ms). A document over 1 MB is replaced by a message. Format is Monaco's built-in Shift+Alt+F.
/// </summary>
public sealed partial class CodeEditor : ComponentBase, IAsyncDisposable
{
    public const int MaxDocumentBytes = 1024 * 1024;
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly string _id = $"azcfg-code-{Guid.NewGuid():N}";
    private StandaloneCodeEditor? _editor;
    private TextModel? _model;
    private IJSObjectReference? _module;
    private CancellationTokenSource? _debounce;
    private IReadOnlyList<JsonDiagnostic> _markers = [];
    private string? _lastValue;
    private string _baseline = "";
    private string _current = "";
    private string _notified = "";
    private bool _dirty;
    private bool _ready;
    private bool _initialized;
    private bool _tooLarge;
    private bool _readOnlyApplied;

    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    /// <summary>The document; a changed value replaces the editor content and the baseline.</summary>
    [Parameter]
    public string Value { get; set; } = "";

    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>Same-origin URL of the JSON schema to validate against (<see cref="MonacoSetup.EditJsonSchemaUri"/>); null = none.</summary>
    [Parameter]
    public string? SchemaUri { get; set; }

    [Parameter]
    public EventCallback<string> OnChange { get; set; }

    [Parameter]
    public EventCallback<bool> OnDirtyChanged { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (_lastValue is null)
        {
            _lastValue = Value;
            ResetBaseline(Value);
            _readOnlyApplied = ReadOnly;
        }
        else if (Value != _lastValue)
        {
            _lastValue = Value;
            await SetValueAsync(Value);
        }

        if (_initialized && _readOnlyApplied != ReadOnly)
        {
            _readOnlyApplied = ReadOnly;
            await _editor!.UpdateOptions(new EditorUpdateOptions { ReadOnly = ReadOnly });
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await MonacoSetup.DefineThemeAsync(Js);
            _module = await MonacoSetup.ImportModuleAsync(Js);
            _ready = true;
            StateHasChanged();
        }
    }

    /// <summary>Current text of the editor.</summary>
    public async Task<string> GetValueAsync() =>
        _initialized && _editor is not null ? _current = await _editor.GetValue() ?? _current : _current;

    /// <summary>Replaces the content and makes it the new baseline (dirty = false).</summary>
    public async Task SetValueAsync(string value)
    {
        var wasTooLarge = _tooLarge;
        ResetBaseline(value);
        await SetDirtyAsync(false);
        if (_tooLarge)
        {
            _initialized = false;
            _model = null;
        }
        else if (_initialized)
        {
            await _editor!.SetValue(value);
        }

        if (wasTooLarge != _tooLarge)
        {
            StateHasChanged();
        }
    }

    /// <summary>Runs Monaco's "Format Document".</summary>
    public async Task FormatAsync()
    {
        if (_initialized && !ReadOnly)
        {
            await _editor!.Trigger("azcfg", "editor.action.formatDocument", null);
        }
    }

    /// <summary>Shows the diagnostics as error markers; an empty list clears them.</summary>
    public async Task SetMarkersAsync(IReadOnlyList<JsonDiagnostic> diagnostics)
    {
        _markers = diagnostics;
        if (_initialized && _model is not null)
        {
            await Global.SetModelMarkers(Js, _model, EditorMarkers.Owner, EditorMarkers.From(diagnostics, await GetValueAsync()));
        }
    }

    /// <summary>Scrolls to and selects the property of <paramref name="key"/> (<c>A:B:C</c>); false when it is not in the text.</summary>
    public async Task<bool> RevealKeyAsync(string key)
    {
        if (!_initialized || JsonKeyLocator.Find(await GetValueAsync(), key) is not { } at)
        {
            return false;
        }

        await _editor!.RevealLineInCenter(at.Line);
        await _editor.SetPosition(new Position { LineNumber = at.Line, Column = at.Column }, "azcfg");
        await _editor.Focus();
        return true;
    }

    private StandaloneEditorConstructionOptions BuildOptions(StandaloneCodeEditor editor) => new()
    {
        AutomaticLayout = true,
        Language = MonacoSetup.Language,
        Theme = MonacoSetup.ThemeName,
        Value = _baseline,
        ReadOnly = ReadOnly,
        TabSize = 2,
        ScrollBeyondLastLine = false,
        Minimap = new EditorMinimapOptions { Enabled = false },
    };

    private async Task InitializedAsync()
    {
        _initialized = true;
        _model = await _editor!.GetModel();
        if (_model is not null && !string.IsNullOrEmpty(SchemaUri))
        {
            await _module!.InvokeVoidAsync("registerSchema", SchemaUri, _model.Uri);
        }

        if (_markers.Count > 0)
        {
            await SetMarkersAsync(_markers);
        }
    }

    private async Task ContentChangedAsync(ModelContentChangedEvent _)
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(Debounce, Time, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var value = await GetValueAsync();
        await SetDirtyAsync(value != _baseline);
        if (value != _notified)
        {
            _notified = value;
            await OnChange.InvokeAsync(value);
        }
    }

    private void ResetBaseline(string value)
    {
        _baseline = _current = _notified = value;
        _tooLarge = Encoding.UTF8.GetByteCount(value) > MaxDocumentBytes;
    }

    private async Task SetDirtyAsync(bool dirty)
    {
        if (_dirty != dirty)
        {
            _dirty = dirty;
            await OnDirtyChanged.InvokeAsync(dirty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        if (_module is null)
        {
            return;
        }

        try
        {
            if (_model is not null)
            {
                await _module.InvokeVoidAsync("unregisterSchema", _model.Uri);
            }

            await _module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
