using Microsoft.Extensions.Hosting;

namespace AzCfg.Ui.Demo;

/// <summary>Runs <see cref="DemoSeed"/> before the server starts listening.</summary>
public sealed class DemoSeedHostedService(DemoSeed seed) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => seed.RunAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
