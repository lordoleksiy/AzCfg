using AzCfg.Ui.Application.State;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Features.Environments;

public static class EnvironmentsFeature
{
    /// <summary>
    /// ENVIRONMENTS and RESOURCES sidebar sections (§2.3), the Settings tab, the account Retry item of the status bar and the
    /// <c>env.setup</c> / <c>env.manage</c> commands.
    /// </summary>
    public static IServiceCollection AddEnvironmentsFeature(this IServiceCollection services) =>
        services
            .AddScoped<EnvironmentLoader>()
            .AddSidebarSection("environments", "Environments", typeof(EnvironmentsSection), 20)
            .AddSidebarSection("resources", "Resources", typeof(ResourcesSection), 30)
            .AddTab(TabKind.Settings, typeof(SettingsTab))
            .AddStatusBarItem(typeof(AccountStatusItem), StatusBarAlignment.Left, 5)
            .AddCommands<EnvironmentCommands>();
}
