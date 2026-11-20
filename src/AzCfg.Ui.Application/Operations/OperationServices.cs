using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Application.Operations;

public static class OperationServices
{
    public static IServiceCollection AddOperationServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services.AddSingleton<IOperationRunner, OperationRunner>();
    }
}
