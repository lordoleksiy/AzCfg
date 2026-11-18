using AzCfg.Core.Model;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Demo;
using AzCfg.Ui.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Demo;

/// <summary>A composed UI host in <c>--demo</c> mode with a recording workspace registry; the demo folder is deleted on dispose.</summary>
public sealed class DemoApp : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DemoApp(WebApplication app, RecordingWorkspaceRegistry registry)
    {
        _app = app;
        Registry = registry;
    }

    public RecordingWorkspaceRegistry Registry { get; }

    public IServiceProvider Services => _app.Services;

    public InMemoryWorld World => Services.GetRequiredService<InMemoryWorld>();

    public DemoLocation Location => Services.GetRequiredService<DemoLocation>();

    public string OrdersApiFolder => Path.Combine(Location.Workspace, "src", "Orders.Api");

    public static async Task<DemoApp> StartAsync(params string[] args)
    {
        var registry = new RecordingWorkspaceRegistry();
        var app = AzCfgUiApp.Build(["--no-browser", .. args], s => s.AddSingleton<IWorkspaceRegistry>(registry));
        var demo = new DemoApp(app, registry);
        await demo.Services.GetRequiredService<DemoSeed>().RunAsync(CancellationToken.None);
        return demo;
    }

    public async Task<ProjectBinding> BindingAsync() =>
        (await World.State.LoadBindingAsync(OrdersApiFolder, CancellationToken.None))!;

    public async Task<ConfigEnvironment> EnvironmentAsync(string name) =>
        (await World.State.LoadProfileAsync(CancellationToken.None))!.Environments.Single(e => e.Name == name);

    public async ValueTask DisposeAsync()
    {
        var root = Location.Root;
        await _app.DisposeAsync();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
