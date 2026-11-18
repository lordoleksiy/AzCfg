using AzCfg.Infrastructure.InMemory;
using AzCfg.Infrastructure.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Hosting;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Demo;

/// <summary>Sample workspace and in-memory Azure for <c>--demo</c> (AD-12).</summary>
public static class DemoServiceCollectionExtensions
{
    /// <summary>
    /// Call after <c>AddAzCfg()</c>. Reserves <c>&lt;temp&gt;/azcfg-demo/&lt;timestamp&gt;</c>, replaces the Azure and user-state adapters with
    /// an <see cref="InMemoryWorld"/>, moves the UI state root into the demo folder unless <c>--state-root</c> was given, registers
    /// <see cref="DemoSeed"/> (run once at startup) and the <c>DEMO</c> status bar item. Throws <see cref="ArgumentException"/> when
    /// <see cref="UiHostOptions.Demo"/> is neither <c>full</c> nor <c>empty</c>.
    /// </summary>
    public static IServiceCollection AddAzCfgDemo(this IServiceCollection services, UiHostOptions options)
    {
        if (!Enum.TryParse<DemoVariant>(options.Demo, ignoreCase: true, out var variant) || !Enum.IsDefined(variant))
        {
            throw new ArgumentException($"Unknown demo variant '{options.Demo}'; use 'full' or 'empty'.", nameof(options));
        }

        var location = DemoLocation.Reserve(Path.GetTempPath(), TimeProvider.System.GetLocalNow());
        if (options.StateRoot == JsonUserStateStore.DefaultRoot)
        {
            options.StateRoot = location.StateRoot;
        }

        var world = new InMemoryWorld();
        services.AddSingleton(world);
        services.AddSingleton(location);
        services.AddSingleton(new UiStatePaths(options.StateRoot));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<DemoSeed>(sp, variant));
        services.AddHostedService<DemoSeedHostedService>();
        services.AddStatusBarItem(typeof(DemoBadge), StatusBarAlignment.Right, 100);
        return services.AddAzCfgInMemory(world);
    }
}
