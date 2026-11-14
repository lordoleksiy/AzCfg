using AzCfg.Core.Abstractions;
using AzCfg.Infrastructure.Local;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAzCfg(this IServiceCollection services) =>
        services.AddSingleton<IProjectDetector, CsprojDetector>();
}
