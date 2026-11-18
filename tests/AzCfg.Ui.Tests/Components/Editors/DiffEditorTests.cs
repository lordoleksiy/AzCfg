using System.Text.Json;
using AzCfg.Ui.Components.Editors;
using AzCfg.Ui.Tests.Support;
using BlazorMonaco.Editor;
using Bunit;
using DiffEditor = AzCfg.Ui.Components.Editors.DiffEditor;

namespace AzCfg.Ui.Tests.Components.Editors;

public sealed class DiffEditorTests : UiTestContext
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Render_UsesReadOnlyDarkThemeAndViewMode(bool inline, bool sideBySide)
    {
        var cut = Render<DiffEditor>(p => p.Add(c => c.Original, "{}").Add(c => c.Modified, "{\"a\":1}").Add(c => c.Inline, inline));
        var editor = cut.WaitForComponent<StandaloneDiffEditor>().Instance;
        var options = editor.ConstructionOptions(editor);
        Assert.Equal(("azcfg-dark", true, sideBySide), (options.Theme, options.ReadOnly, options.RenderSideBySide));
    }

    [Fact]
    public void EditJsonSchema_IsValidJsonAndDescribesKvAndGlobal()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "schemas", "edit-json.schema.json");
        var text = File.Exists(path) ? File.ReadAllText(path) : File.ReadAllText(FindSource());
        using var schema = JsonDocument.Parse(text);
        Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty("@global", out _));
        Assert.True(schema.RootElement.GetProperty("definitions").GetProperty("kv").GetProperty("properties").TryGetProperty("@kv", out _));
    }

    private static string FindSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "AzCfg.Ui", "wwwroot", "schemas", "edit-json.schema.json")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src", "AzCfg.Ui", "wwwroot", "schemas", "edit-json.schema.json");
    }
}
