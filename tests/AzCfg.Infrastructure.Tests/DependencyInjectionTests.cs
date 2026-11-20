using AzCfg.Core.Abstractions;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Infrastructure.Local;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddAzCfg_RegistersSingleSingletonTokenCredential_BackedByDefaultAzureCredential()
    {
        var descriptor = Assert.Single(new ServiceCollection().AddAzCfg(), d => d.ServiceType == typeof(TokenCredential));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.IsType<DefaultAzureCredential>(descriptor.ImplementationFactory!(new CredentialProvider(new StaticTokenCredential())));
    }

    [Theory]
    [InlineData(typeof(IConfigStore))]
    [InlineData(typeof(ISecretStore))]
    [InlineData(typeof(ArmClient))]
    public void AddAzCfg_AzureClients_TakeCredentialFromProvider(Type serviceType)
    {
        var provider = new CredentialProvider(new StaticTokenCredential());
        var descriptor = Assert.Single(new ServiceCollection().AddAzCfg(), d => d.ServiceType == serviceType);

        Assert.NotNull(descriptor.ImplementationFactory!(provider));
        Assert.Equal(1, provider.CredentialRequests);
    }

    [Fact]
    public void AddAzCfg_RegistersWorkspaceScanner() =>
        Assert.Contains(new ServiceCollection().AddAzCfg(), d => d.ServiceType == typeof(IWorkspaceScanner) && d.ImplementationType == typeof(WorkspaceScanner));

    [Fact]
    public void AddAzCfgInMemory_StillReplacesCredential()
    {
        var world = new InMemoryWorld();
        var services = new ServiceCollection().AddAzCfg().AddAzCfgInMemory(world);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TokenCredential));

        Assert.Same(world.Credential, descriptor.ImplementationInstance);
    }

    private sealed class CredentialProvider(TokenCredential credential) : IServiceProvider
    {
        public int CredentialRequests { get; private set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType != typeof(TokenCredential))
            {
                return null;
            }

            CredentialRequests++;
            return credential;
        }
    }
}
