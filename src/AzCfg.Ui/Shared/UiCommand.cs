namespace AzCfg.Ui.Shared;

/// <summary>A command of the palette and the keymap; <see cref="IsEnabled"/> null means always enabled.</summary>
public sealed record UiCommand(string Id, string Title, string Category, string? Shortcut, Func<Task> Execute, Func<bool>? IsEnabled = null);
