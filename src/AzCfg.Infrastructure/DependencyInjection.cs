using AzCfg.Core.Abstractions;
using AzCfg.Infrastructure.Local;
using AzCfg.Infrastructure.State;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAzCfg(this IServiceCollection services)
    {
        services.AddSingleton<IProjectDetector, CsprojDetector>();
        services.AddSingleton<ILocalTarget, AppSettingsLocalTarget>();
        services.AddSingleton<IUserStateStore>(_ => new JsonUserStateStore(JsonUserStateStore.DefaultRoot));
        return services;
    }
}
