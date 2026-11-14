using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using AzCfg.Core.Abstractions;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure.Azure;
using AzCfg.Infrastructure.Local;
using AzCfg.Infrastructure.State;
using Azure.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAzCfg(this IServiceCollection services)
    {
        services.AddSingleton<IProjectDetector, CsprojDetector>();
        services.AddSingleton<ILocalTarget, AppSettingsLocalTarget>();
        services.AddSingleton<IUserStateStore>(_ => new JsonUserStateStore(JsonUserStateStore.DefaultRoot));
        services.AddSingleton<ISecretStore>(_ =>
        {
            var credential = new DefaultAzureCredential();
            return new KeyVaultSecretStore(vault => new SecretClient(vault, credential));
        });
        services.AddSingleton<ILocalTarget, UserSecretsTarget>();
        services.AddSingleton(_ => new ArmClient(new DefaultAzureCredential()));
        services.AddSingleton<IResourceDiscovery, ResourceGraphDiscovery>();
        services.AddSingleton<IConfigStore>(_ => new AppConfigStore(new DefaultAzureCredential()));
        services.AddSingleton<ISetupService, SetupService>();
        return services;
    }
}
