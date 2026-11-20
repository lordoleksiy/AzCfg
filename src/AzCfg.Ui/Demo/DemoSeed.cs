using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Ui.Application.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AzCfg.Ui.Demo;

/// <summary>
/// Fills the demo world once at startup (AD-12): writes the <see cref="SampleWorkspace"/>, populates <see cref="DemoAzure"/>
/// and, for <see cref="DemoVariant.Full"/>, saves the profile, binds <c>Orders.Api</c>, pulls <c>dev</c>, applies two local
/// edits, one remote change and one conflict, and registers the workspace. Nothing leaves the in-memory world and the demo folder.
/// </summary>
public sealed class DemoSeed(InMemoryWorld world, DemoLocation location, DemoVariant variant, IServiceProvider services, ILogger<DemoSeed> logger)
{
    private readonly object _gate = new();
    private Task? _seeded;

    /// <summary>Seeds on the first call; later calls return the same task (the first call's token governs the run).</summary>
    public Task RunAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return _seeded ??= SeedAsync(ct);
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        SampleWorkspace.Write(location.Workspace);
        DemoAzure.Populate(world);
        if (variant == DemoVariant.Full)
        {
            await SeedFullAsync(ct);
        }

        logger.LogInformation("Demo workspace ({Variant}) seeded at {Path}", variant, location.Workspace);
    }

    private async Task SeedFullAsync(CancellationToken ct)
    {
        var profile = new UserProfile(DemoAzure.Environments, EnvironmentMode.StorePerEnvironment);
        await world.State.SaveProfileAsync(profile, ct);
        var dev = services.GetRequiredService<IEnvironmentResolver>().Resolve(profile).Single(e => e.Name == "dev");
        var csproj = SampleWorkspace.OrdersApiCsproj(location.Workspace);
        var values = new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, csproj, LocalFileFormat.NestedJson);
        var secrets = new LocalTargetBinding(LocalTargetKind.UserSecrets, csproj, LocalFileFormat.NestedJson);
        var vaults = DemoAzure.Environments.ToDictionary(e => e.Name, e => DemoAzure.VaultUri(e.Name));
        var binding = new ProjectBinding(Path.GetDirectoryName(csproj)!, DemoAzure.Service, values, secrets, vaults);
        await world.State.SaveBindingAsync(binding, ct);
        await services.GetRequiredService<ISyncService>().PullAsync(binding, dev, null, false, ct);
        await EditLocalValuesAsync(values, ct);
        var store = DemoAzure.StoreUri("dev").ToString();
        world.AppConfig.Set(store, "orders:Payments:Timeout", "45");
        world.AppConfig.Set(store, "orders:Orders:MaxItemsPerOrder", "150");
        if (services.GetService<IWorkspaceRegistry>() is { } registry)
        {
            await registry.AddOrTouchAsync(location.Workspace, ct);
        }
    }

    private async Task EditLocalValuesAsync(LocalTargetBinding values, CancellationToken ct)
    {
        var target = services.GetServices<ILocalTarget>().First(t => t.Kind == values.Kind);
        var local = (await target.ReadAsync(values, ct)).ToList();
        Set(local, "Cache:Ttl", "60");
        Set(local, "Orders:MaxItemsPerOrder", "250");
        Set(local, "Features:LocalOnly:Enabled", "true");
        await target.WriteAsync(values, local, ct);
    }

    private static void Set(List<LocalValue> values, string key, string value)
    {
        var index = values.FindIndex(v => v.Key == key);
        if (index >= 0)
        {
            values[index] = values[index] with { Value = value };
        }
        else
        {
            values.Add(new LocalValue(key, value));
        }
    }
}
