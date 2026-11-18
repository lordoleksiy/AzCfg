using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Application.Configuration;

public static class ConfigurationServices
{
    public static IServiceCollection AddConfigurationServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddSingleton<LocalTargetAccess>()
            .AddSingleton<IConfigurationService, ConfigurationService>()
            .AddSingleton<ICompareService, CompareService>()
            .AddSingleton<ILocalValuesService, LocalValuesService>()
            .AddSingleton<ISecretRevealService, SecretRevealService>();
    }
}
