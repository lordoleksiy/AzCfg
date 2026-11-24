using System.Buffers.Text;
using System.Text.Json;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Infrastructure.Local;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Infrastructure.Tests.InMemory;

public sealed class InMemoryCompositionTests
{
    private readonly InMemoryWorld _world = new();

    [Fact]
    public void AddAzCfgInMemory_AfterAddAzCfg_ResolvesSyncService() =>
        Assert.IsType<SyncService>(Provider(_world).GetService(typeof(ISyncService)));

    [Fact]
    public void AddAzCfgInMemory_AfterAddAzCfg_ResolvesWorldInstancesForPorts()
    {
        var provider = Provider(_world);

        Assert.Same(_world.AppConfig, provider.GetService(typeof(IConfigStore)));
        Assert.Same(_world.Secrets, provider.GetService(typeof(ISecretStore)));
        Assert.Same(_world.State, provider.GetService(typeof(IUserStateStore)));
        Assert.Same(_world.Manifests, provider.GetService(typeof(IProjectManifestStore)));
        Assert.Same(_world.Discovery, provider.GetService(typeof(IResourceDiscovery)));
        Assert.Same(_world.Credential, provider.GetService(typeof(TokenCredential)));
    }

    [Fact]
    public void AddAzCfgInMemory_LocalTargets_AreRealFileTargetsAndWorldUserSecrets()
    {
        var targets = (IEnumerable<ILocalTarget>)Provider(_world).GetService(typeof(IEnumerable<ILocalTarget>))!;

        Assert.Collection(
            targets,
            t => Assert.IsType<AppSettingsLocalTarget>(t),
            t => Assert.IsType<LocalSettingsTarget>(t),
            t => Assert.Same(_world.UserSecrets, t));
    }

    [Fact]
    public void AddAzCfgInMemory_WithoutDetector_KeepsCsprojDetector() =>
        Assert.IsType<CsprojDetector>(Provider(_world).GetService(typeof(IProjectDetector)));

    [Fact]
    public void AddAzCfgInMemory_WithDetector_ResolvesWorldDetector()
    {
        var world = new InMemoryWorld { Detector = new FixedProjectDetector() };

        Assert.Same(world.Detector, Provider(world).GetService(typeof(IProjectDetector)));
    }

    [Fact]
    public async Task StaticTokenCredential_GetTokenAsync_ReturnsJwtWithAccountAndTenantValidForAnHour()
    {
        var before = DateTimeOffset.UtcNow;

        var token = await new StaticTokenCredential("dev@contoso.com", "tenant-1").GetTokenAsync(new TokenRequestContext(["scope"]), CancellationToken.None);

        var parts = token.Token.Split('.');
        Assert.Equal(3, parts.Length);
        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        Assert.Equal("dev@contoso.com", payload.RootElement.GetProperty("upn").GetString());
        Assert.Equal("dev@contoso.com", payload.RootElement.GetProperty("preferred_username").GetString());
        Assert.Equal("tenant-1", payload.RootElement.GetProperty("tid").GetString());
        Assert.Equal(token.ExpiresOn.ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        Assert.InRange(token.ExpiresOn, before.AddHours(1).AddSeconds(-1), DateTimeOffset.UtcNow.AddHours(1).AddSeconds(1));
    }

    [Fact]
    public void StaticTokenCredential_Defaults_UseDemoAccountAndEmptyTenant()
    {
        var token = new InMemoryWorld().Credential.GetToken(new TokenRequestContext(["scope"]), CancellationToken.None);

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(token.Token.Split('.')[1]));
        Assert.Equal("demo@azcfg.local", payload.RootElement.GetProperty("upn").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000000", payload.RootElement.GetProperty("tid").GetString());
    }

    [Fact]
    public async Task InMemoryLocalTarget_RemoveLocalMissingKey_ThrowsInvalidOperationException()
    {
        var binding = new LocalTargetBinding(LocalTargetKind.UserSecrets, "repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
        await _world.UserSecrets.WriteAsync(binding, [new("A", "1")], CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => _world.UserSecrets.RemoveLocal("B"));
        Assert.Throws<InvalidOperationException>(() => _world.UserSecrets.Scope("B"));
    }

    [Fact]
    public void InMemoryLocalTarget_NoFileWritten_SetLocalThrowsInvalidOperationException() =>
        Assert.Throws<InvalidOperationException>(() => _world.UserSecrets.SetLocal("A", "1"));

    [Fact]
    public void InfrastructureAssembly_DoesNotReferenceXunit() =>
        Assert.DoesNotContain(
            typeof(InMemoryWorld).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("xunit", StringComparison.OrdinalIgnoreCase));

    private static DescriptorServiceProvider Provider(InMemoryWorld world) => new(new ServiceCollection().AddAzCfg().AddAzCfgInMemory(world));

    /// <summary>
    /// Minimal singleton resolver over the registrations (the test project has no DI container package): last registration
    /// wins, <see cref="IEnumerable{T}"/> yields every registration in order, types are built with
    /// <see cref="ActivatorUtilities"/>.
    /// </summary>
    private sealed class DescriptorServiceProvider(IServiceCollection services) : IServiceProvider
    {
        private readonly Dictionary<ServiceDescriptor, object> _instances = [];

        public object? GetService(Type serviceType) =>
            serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? All(serviceType.GetGenericArguments()[0])
                : services.LastOrDefault(d => d.ServiceType == serviceType) is { } descriptor ? Instance(descriptor) : null;

        private Array All(Type serviceType)
        {
            var instances = services.Where(d => d.ServiceType == serviceType).Select(Instance).ToArray();
            var array = Array.CreateInstance(serviceType, instances.Length);
            instances.CopyTo(array, 0);
            return array;
        }

        private object Instance(ServiceDescriptor descriptor) =>
            _instances.TryGetValue(descriptor, out var instance)
                ? instance
                : _instances[descriptor] = descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(this)
                    ?? ActivatorUtilities.CreateInstance(this, descriptor.ImplementationType!);
    }
}
