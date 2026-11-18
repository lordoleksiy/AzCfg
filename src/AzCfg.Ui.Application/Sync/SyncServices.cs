using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Application.Sync;

public static class SyncServices
{
    public static IServiceCollection AddSyncServices(this IServiceCollection services) => services
        .AddSingleton<IDraftService, DraftService>()
        .AddSingleton<ISyncWorkflowService, SyncWorkflowService>();
}
