namespace AzCfg.Ui.Features.Platform;

/// <summary>A palette entry. Only labels and key names are shown, never values; a disabled item must not be run.</summary>
public sealed record PaletteItem(string Label, string? Detail, string? Shortcut, bool Enabled, Func<Task> Run);
