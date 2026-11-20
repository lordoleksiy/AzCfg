using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Layout;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Features.Platform;

public static class PlatformFeature
{
    /// <summary>Welcome tab, the command registry, the palette search and the platform commands.</summary>
    public static IServiceCollection AddPlatformFeature(this IServiceCollection services) =>
        services
            .AddScoped<ICommandRegistry, CommandRegistry>()
            .AddScoped<LayoutState>()
            .AddScoped<PaletteState>()
            .AddScoped<PaletteSearch>()
            .AddScoped<TabCloser>()
            .AddTab(TabKind.Welcome, typeof(WelcomeTab))
            .AddCommands<PlatformCommands>();
}
