using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Application.Health;

public static class HealthServices
{
    public static IServiceCollection AddHealthServices(this IServiceCollection services) => services
        .AddSingleton<IProblemsService, ProblemsService>();
}
