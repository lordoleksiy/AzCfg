using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Application;

public static class ApplicationServices
{
    /// <summary>Registers every Application area and the per-circuit (scoped) state.</summary>
    public static IServiceCollection AddAzCfgUiApplication(this IServiceCollection services) =>
        services
            .AddOperationServices()
            .AddWorkspaceServices()
            .AddEnvironmentServices()
            .AddConfigurationServices()
            .AddSyncServices()
            .AddScoped<WorkspaceState>()
            .AddScoped<ProjectSessions>()
            .AddScoped<ProtectedUnlockState>()
            .AddScoped<TabsState>()
            .AddScoped<WorkbenchNavigator>();
}
