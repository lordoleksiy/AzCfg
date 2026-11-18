using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Components.Editors;
using AzCfg.Ui.Tests.Support;
using BlazorMonaco.Editor;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using CodeEditor = AzCfg.Ui.Components.Editors.CodeEditor;

namespace AzCfg.Ui.Tests.Components.Editors;

public sealed class CodeEditorTests : UiTestContext
{
    private const string GetValue = "blazorMonaco.editor.getValue";

    private readonly FakeTimeProvider _time = new();
    private readonly List<bool> _dirty = [];
    private readonly List<string> _changes = [];
    private string _editorText = "";

    public CodeEditorTests() => Services.AddSingleton<TimeProvider>(_time);

    private async Task<IRenderedComponent<CodeEditor>> RenderReadyAsync(string value)
    {
        _editorText = value;
        JSInterop.Setup<string>(GetValue, _ => true).SetResult(null!);
        var cut = Render<CodeEditor>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.OnDirtyChanged, EventCallback.Factory.Create<bool>(this, _dirty.Add))
            .Add(c => c.OnChange, EventCallback.Factory.Create<string>(this, _changes.Add)));
        var editor = cut.WaitForComponent<BlazorMonaco.Editor.StandaloneCodeEditor>();
        await cut.InvokeAsync(() => editor.Instance.OnDidInit.InvokeAsync());
        return cut;
    }

    private async Task TypeAsync(IRenderedComponent<CodeEditor> cut, string text)
    {
        _editorText = text;
        var editor = cut.FindComponent<BlazorMonaco.Editor.StandaloneCodeEditor>().Instance;
        var pending = cut.InvokeAsync(() => editor.OnDidChangeModelContent.InvokeAsync(new ModelContentChangedEvent()));
        _time.Advance(CodeEditor.Debounce);
        await pending;
    }

    [Fact]
    public void Render_UnderLimit_ShowsEditorWithTheAzcfgTheme()
    {
        var cut = Render<CodeEditor>(p => p.Add(c => c.Value, "{}"));
        var editor = cut.WaitForComponent<BlazorMonaco.Editor.StandaloneCodeEditor>().Instance;
        var options = editor.ConstructionOptions(editor);
        Assert.Equal(("json", "azcfg-dark", "{}"), (options.Language, options.Theme, options.Value));
        JSInterop.VerifyInvoke("blazorMonaco.editor.defineTheme");
    }

    [Fact]
    public void Render_OverOneMegabyte_ShowsMessageInsteadOfEditor()
    {
        var cut = Render<CodeEditor>(p => p.Add(c => c.Value, new string('a', CodeEditor.MaxDocumentBytes + 1)));
        cut.WaitForAssertion(() => Assert.Contains("larger than 1 MB", cut.Find(".az-code-editor-limit").TextContent));
        Assert.Empty(cut.FindComponents<BlazorMonaco.Editor.StandaloneCodeEditor>());
    }

    [Fact]
    public async Task SetValueAsync_OverOneMegabyte_SwapsEditorForMessage()
    {
        var cut = await RenderReadyAsync("{}");
        await cut.InvokeAsync(() => cut.Instance.SetValueAsync(new string('a', CodeEditor.MaxDocumentBytes + 1)));
        Assert.Contains("larger than 1 MB", cut.Find(".az-code-editor-limit").TextContent);
        Assert.Empty(cut.FindComponents<BlazorMonaco.Editor.StandaloneCodeEditor>());
    }

    [Fact]
    public async Task Edit_IsDebounced_ThenReportsChangeAndDirty()
    {
        var cut = await RenderReadyAsync("{}");
        JSInterop.Setup<string>(GetValue, _ => _editorText == "{\"a\":1}").SetResult("{\"a\":1}");
        var editor = cut.FindComponent<BlazorMonaco.Editor.StandaloneCodeEditor>().Instance;
        _editorText = "{\"a\":1}";
        var pending = cut.InvokeAsync(() => editor.OnDidChangeModelContent.InvokeAsync(new ModelContentChangedEvent()));
        _time.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Empty(_changes);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await pending;
        Assert.Equal(["{\"a\":1}"], _changes);
        Assert.Equal([true], _dirty);
    }

    [Fact]
    public async Task Edit_BackToBaseline_ClearsDirty()
    {
        var cut = await RenderReadyAsync("{}");
        JSInterop.Setup<string>(GetValue, _ => _editorText == "{\"a\":1}").SetResult("{\"a\":1}");
        JSInterop.Setup<string>(GetValue, _ => _editorText == "{}").SetResult("{}");
        await TypeAsync(cut, "{\"a\":1}");
        await TypeAsync(cut, "{}");
        Assert.Equal([true, false], _dirty);
    }

    [Fact]
    public async Task SetValueAsync_ResetsBaseline()
    {
        var cut = await RenderReadyAsync("{}");
        JSInterop.Setup<string>(GetValue, _ => _editorText == "{\"a\":1}").SetResult("{\"a\":1}");
        await TypeAsync(cut, "{\"a\":1}");
        await cut.InvokeAsync(() => cut.Instance.SetValueAsync("{\"a\":1}"));
        Assert.Equal([true, false], _dirty);
        JSInterop.VerifyInvoke("blazorMonaco.editor.setValue");
        JSInterop.Setup<string>(GetValue, _ => _editorText == "{\"a\":2}").SetResult("{\"a\":2}");
        await TypeAsync(cut, "{\"a\":2}");
        Assert.Equal([true, false, true], _dirty);
    }

    [Fact]
    public async Task ChangedValueParameter_ReplacesContentAndBaseline()
    {
        var cut = await RenderReadyAsync("{}");
        cut.Render(p => p.Add(c => c.Value, "{\"b\":2}"));
        JSInterop.VerifyInvoke("blazorMonaco.editor.setValue");
        Assert.Empty(_dirty);
    }

    [Fact]
    public async Task SetMarkersAsync_MapsDiagnosticsToMarkers()
    {
        var cut = await RenderReadyAsync("{\n  \"Db\": { \"Host\": 1 }\n}");
        JSInterop.Setup<string>(GetValue, _ => true).SetResult("{\n  \"Db\": { \"Host\": 1 }\n}");
        JSInterop.Setup<TextModel>("blazorMonaco.editor.getInstanceModel", _ => true).SetResult(new TextModel { Id = "m1", Uri = "inmemory://m1" });
        var editor = cut.FindComponent<BlazorMonaco.Editor.StandaloneCodeEditor>().Instance;
        await cut.InvokeAsync(() => editor.OnDidInit.InvokeAsync());
        await cut.InvokeAsync(() => cut.Instance.SetMarkersAsync([new JsonDiagnostic("bad host", "Db:Host", null, null), new JsonDiagnostic("oops", null, 3, 2)]));
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "blazorMonaco.editor.setModelMarkers");
        var markers = Assert.IsType<List<MarkerData>>(call.Arguments[2]);
        Assert.Equal(["bad host", "oops"], markers.Select(m => m.Message));
        Assert.Equal((2, 11), (markers[0].StartLineNumber, markers[0].StartColumn));
        Assert.Equal((3, 2), (markers[1].StartLineNumber, markers[1].StartColumn));
    }
}
