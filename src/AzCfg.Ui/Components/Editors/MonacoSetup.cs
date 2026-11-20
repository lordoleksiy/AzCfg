using BlazorMonaco.Editor;
using Microsoft.JSInterop;

namespace AzCfg.Ui.Components.Editors;

/// <summary>Shared Monaco setup (AD-11): the <c>azcfg-dark</c> theme (§2.12), the schema interop module and model disposal.</summary>
public static class MonacoSetup
{
    public const string ThemeName = "azcfg-dark";
    public const string EditJsonSchemaUri = "schemas/edit-json.schema.json";
    public const string Language = "json";

    private const string ModulePath = "./Components/Editors/CodeEditor.razor.js";

    public static StandaloneThemeData DarkTheme() => new()
    {
        Base = "vs-dark",
        Inherit = true,
        Rules =
        [
            new TokenThemeRule { Token = "string.key.json", Foreground = "5B9DFF" },
            new TokenThemeRule { Token = "string.value.json", Foreground = "CE9178" },
            new TokenThemeRule { Token = "number", Foreground = "B5CEA8" },
            new TokenThemeRule { Token = "keyword.json", Foreground = "A78BFA" },
        ],
        Colors = new Dictionary<string, string>
        {
            ["editor.background"] = "#1F1F1F",
            ["editor.foreground"] = "#E6E6E6",
            ["editorCursor.foreground"] = "#5B9DFF",
            ["editor.lineHighlightBackground"] = "#2A2A2A",
            ["editorLineNumber.activeForeground"] = "#5B9DFF",
            ["editor.selectionBackground"] = "#5B9DFF44",
            ["diffEditor.insertedTextBackground"] = "#3FB68B33",
            ["diffEditor.insertedLineBackground"] = "#3FB68B1A",
            ["diffEditor.removedTextBackground"] = "#F0605D33",
            ["diffEditor.removedLineBackground"] = "#F0605D1A",
        },
    };

    public static Task DefineThemeAsync(IJSRuntime js) => Global.DefineTheme(js, ThemeName, DarkTheme());

    public static async Task<IJSObjectReference> ImportModuleAsync(IJSRuntime js) =>
        await js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public static string NewModelUri(string name) => $"inmemory://azcfg/{Guid.NewGuid():N}/{name}.json";

    /// <summary>Disposes a model, ignoring a circuit that is already gone.</summary>
    public static async Task DisposeModelAsync(TextModel? model)
    {
        if (model is null)
        {
            return;
        }

        try
        {
            await model.DisposeModel();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
