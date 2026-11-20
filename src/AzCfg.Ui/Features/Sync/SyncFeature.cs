using AzCfg.Ui.Application.State;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Features.Sync;

public static class SyncFeature
{
    /// <summary>Sync mode, the make-active flow (<see cref="IActivationFlow"/>), the status bar summary and the Sync commands.</summary>
    public static IServiceCollection AddSyncFeature(this IServiceCollection services) =>
        services
            .AddScoped<ActivationFlow>()
            .AddScoped<IActivationFlow>(sp => sp.GetRequiredService<ActivationFlow>())
            .AddScoped<SyncCommandTargets>()
            .AddProjectMode(ProjectMode.Sync, typeof(SyncMode))
            .AddStatusBarItem(typeof(SyncStatusItem), StatusBarAlignment.Left, 10)
            .AddCommands<SyncCommands>();
}
