using AzCfg.Core.Abstractions;
using AzCfg.Infrastructure.Local;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Infrastructure.InMemory;

public static class InMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Call after <c>AddAzCfg()</c>: replaces <see cref="IConfigStore"/>, <see cref="ISecretStore"/>, <see cref="IUserStateStore"/>,
    /// <see cref="IResourceDiscovery"/>, <see cref="ITrackedSettingsReader"/> and <see cref="TokenCredential"/> with the <paramref name="world"/>'s instances,
    /// <see cref="IProjectDetector"/> when <see cref="InMemoryWorld.Detector"/> is set, and the <see cref="UserSecretsTarget"/>
    /// registration of <see cref="ILocalTarget"/> (in place, keeping the order) with <see cref="InMemoryWorld.UserSecrets"/>;
    /// <see cref="AppSettingsLocalTarget"/> stays real. Throws <see cref="InvalidOperationException"/> when no
    /// <see cref="UserSecretsTarget"/> is registered (<c>AddAzCfg()</c> not called first).
    /// </summary>
    public static IServiceCollection AddAzCfgInMemory(this IServiceCollection services, InMemoryWorld world)
    {
        services.Replace(ServiceDescriptor.Singleton<IConfigStore>(world.AppConfig));
        services.Replace(ServiceDescriptor.Singleton<ISecretStore>(world.Secrets));
        services.Replace(ServiceDescriptor.Singleton<IUserStateStore>(world.State));
        services.Replace(ServiceDescriptor.Singleton<IResourceDiscovery>(world.Discovery));
        services.Replace(ServiceDescriptor.Singleton<TokenCredential>(world.Credential));
        services.Replace(ServiceDescriptor.Singleton<ITrackedSettingsReader>(world.TrackedSettings));
        if (world.Detector is { } detector)
        {
            services.Replace(ServiceDescriptor.Singleton<IProjectDetector>(detector));
        }

        var userSecrets = services.SingleOrDefault(d => d.ServiceType == typeof(ILocalTarget) && d.ImplementationType == typeof(UserSecretsTarget))
            ?? throw new InvalidOperationException("Call AddAzCfg() before AddAzCfgInMemory().");
        services[services.IndexOf(userSecrets)] = ServiceDescriptor.Singleton<ILocalTarget>(world.UserSecrets);
        return services;
    }
}
