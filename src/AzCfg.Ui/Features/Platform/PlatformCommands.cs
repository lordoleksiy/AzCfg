using AzCfg.Ui.Application.State;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Platform;

/// <summary>Palette, quick open, pane toggles and tab commands of the workbench.</summary>
public sealed class PlatformCommands(PaletteState palette, LayoutState layout, TabsState tabs, TabCloser closer) : ICommandContributor
{
    private const string Category = "Workbench";

    public IEnumerable<UiCommand> Commands =>
    [
        Command(CommandIds.Palette, "Show command palette", () => Run(() => palette.Open(PaletteMode.Commands))),
        Command(CommandIds.QuickOpen, "Go to project or key", () => Run(() => palette.Open(PaletteMode.QuickOpen))),
        Command(CommandIds.ToggleSidebar, "Toggle sidebar", () => Run(layout.ToggleSidebar)),
        Command(CommandIds.ToggleInspector, "Toggle inspector", () => Run(layout.ToggleInspector)),
        Command(CommandIds.CloseTab, "Close tab", () => tabs.ActiveId is { } id ? closer.CloseAsync(id) : Task.CompletedTask, () => tabs.ActiveId is not null),
        Command(CommandIds.NextTab, "Next tab", () => Run(() => Step(1)), () => tabs.Tabs.Count > 1),
        Command(CommandIds.PrevTab, "Previous tab", () => Run(() => Step(-1)), () => tabs.Tabs.Count > 1),
    ];

    private static UiCommand Command(string id, string title, Func<Task> execute, Func<bool>? enabled = null) =>
        new(id, title, Category, KeyBindings.ShortcutOf(id), execute, enabled);

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private void Step(int delta)
    {
        var index = tabs.Tabs.ToList().FindIndex(t => t.Id == tabs.ActiveId);
        if (index >= 0)
        {
            tabs.Activate(tabs.Tabs[(index + delta + tabs.Tabs.Count) % tabs.Tabs.Count].Id);
        }
    }
}
