using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Health;
using AzCfg.Core.Security;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure.Azure;
using AzCfg.Infrastructure.Local;
using AzCfg.Infrastructure.State;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.Security.KeyVault.Secrets;
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
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IEnvironmentResolver, ProfileEnvironmentResolver>();
        services.AddSingleton<ISecretFingerprinter>(sp =>
            new HmacSecretFingerprinter(sp.GetRequiredService<IUserStateStore>().GetOrCreateFingerprintKeyAsync(CancellationToken.None).GetAwaiter().GetResult()));
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<IConfigComparisonService, ConfigComparisonService>();
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton<IAzureAccountProbe, AzureAccountProbe>();
        services.AddSingleton<IHealthService, HealthService>();
        return services;
    }
}
