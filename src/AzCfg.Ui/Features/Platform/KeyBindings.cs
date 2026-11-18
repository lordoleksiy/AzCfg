using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Platform;

/// <summary>The keymap of §2.10 → command ids. Never binds browser-reserved combinations (G31).</summary>
public static class KeyBindings
{
    public static IReadOnlyList<KeyBinding> All { get; } =
    [
        new(KeyChord.Parse("Ctrl+K"), CommandIds.Palette),
        new(KeyChord.Parse("Ctrl+Shift+P"), CommandIds.Palette),
        new(KeyChord.Parse("Ctrl+P"), CommandIds.QuickOpen),
        new(KeyChord.Parse("Ctrl+B"), CommandIds.ToggleSidebar),
        new(KeyChord.Parse("Alt+I"), CommandIds.ToggleInspector),
        new(KeyChord.Parse("Alt+W"), CommandIds.CloseTab),
        new(KeyChord.Parse("Alt+PageDown"), CommandIds.NextTab),
        new(KeyChord.Parse("Alt+PageUp"), CommandIds.PrevTab),
        new(KeyChord.Parse("Alt+1"), CommandIds.ModeBrowse),
        new(KeyChord.Parse("Alt+2"), CommandIds.ModeCompare),
        new(KeyChord.Parse("Alt+3"), CommandIds.ModeSync),
        new(KeyChord.Parse("Alt+R"), CommandIds.ProjectRefresh),
        new(KeyChord.Parse("Ctrl+F"), CommandIds.BrowseFind),
        new(KeyChord.Parse("F2"), CommandIds.BrowseEdit),
        new(KeyChord.Parse("Delete"), CommandIds.BrowseDelete),
        new(KeyChord.Parse("Alt+N"), CommandIds.BrowseAddKey),
        new(KeyChord.Parse("Ctrl+S"), CommandIds.DraftReview),
        new(KeyChord.Parse("Ctrl+Enter"), CommandIds.SyncApply),
        new(KeyChord.Parse("Shift+Alt+F"), CommandIds.EditorFormat),
    ];

    /// <summary>Combinations owned by the browser; no binding may use them.</summary>
    public static IReadOnlyList<KeyChord> Reserved { get; } =
        [.. new[] { "Ctrl+W", "Ctrl+T", "Ctrl+N", "Ctrl+Tab", "Ctrl+Shift+T", "Ctrl+Shift+N", "Ctrl+Shift+W", "F5", "Ctrl+R", "Ctrl+L", "Alt+F4" }.Select(KeyChord.Parse)];

    /// <summary>The command bound to the chord, or <c>null</c>.</summary>
    public static string? Resolve(KeyChord chord) => All.FirstOrDefault(b => b.Chord == chord)?.CommandId;

    /// <summary>The first chord of the command as text ("Ctrl+K"), or <c>null</c>.</summary>
    public static string? ShortcutOf(string commandId) => All.FirstOrDefault(b => b.CommandId == commandId)?.Chord.ToString();
}
