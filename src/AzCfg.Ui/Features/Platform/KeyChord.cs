using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Platform;

/// <summary>A key plus modifiers. <see cref="Key"/> is the DOM key name; single characters are upper-case ("P", "1").</summary>
public sealed record KeyChord(string Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    /// <summary>Parses "Ctrl+Shift+P", "Alt+PageDown", "F2".</summary>
    public static KeyChord Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new KeyChord(
            Normalize(parts[^1]),
            parts.Contains("Ctrl", StringComparer.OrdinalIgnoreCase),
            parts.Contains("Alt", StringComparer.OrdinalIgnoreCase),
            parts.Contains("Shift", StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The chord of a Fluent key event; the Meta key counts as Ctrl.</summary>
    public static KeyChord From(FluentKeyCodeEventArgs e) =>
        new(Normalize(e.Value), e.CtrlKey || e.MetaKey, e.AltKey, e.ShiftKey);

    public override string ToString() =>
        string.Concat(Ctrl ? "Ctrl+" : "", Alt ? "Alt+" : "", Shift ? "Shift+" : "", Key);

    private static string Normalize(string key) => key.Length == 1 ? key.ToUpperInvariant() : key;
}
