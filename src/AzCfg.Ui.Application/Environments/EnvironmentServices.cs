using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Application.Environments;

public static class EnvironmentServices
{
    public static IServiceCollection AddEnvironmentServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddSingleton<IEnvironmentService, EnvironmentService>()
            .AddSingleton<IAzureStatusService, AzureStatusService>();
    }
}
