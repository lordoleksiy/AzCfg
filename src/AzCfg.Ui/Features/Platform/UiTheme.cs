using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Platform;

/// <summary>The theme of §2.12: Fluent dark mode with the brand colour.</summary>
public static class UiTheme
{
    public const string BrandColor = "#5B9DFF";

    public static ThemeSettings Settings { get; } = new(BrandColor, 0, 0, ThemeMode.Dark, false);
}
