using AzCfg.Ui.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Hosting;

public sealed class BrowserLaunchTests
{
    private sealed class RecordingLauncher : BrowserLauncher
    {
        public List<string> Opened { get; } = [];

        public override bool TryOpen(string url)
        {
            Opened.Add(url);
            return true;
        }
    }

    private static readonly string StateRoot = Path.Combine(Path.GetTempPath(), "azcfg-ui-tests-launch");

    private static async Task<(RecordingLauncher Launcher, string Address, string Token)> RunAsync(params string[] extraArgs)
    {
        var launcher = new RecordingLauncher();
        var app = AzCfgUiApp.Build(["--state-root", StateRoot, .. extraArgs], s => s.AddSingleton<BrowserLauncher>(launcher));
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return (launcher, address, app.Services.GetRequiredService<AccessToken>().Value);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Start_WithNoBrowser_DoesNotLaunch()
    {
        var (launcher, _, _) = await RunAsync("--no-browser");
        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public async Task Start_Default_LaunchesLoginUrlOnTheRealLoopbackPort()
    {
        var (launcher, address, token) = await RunAsync();
        Assert.StartsWith("http://127.0.0.1:", address, StringComparison.Ordinal);
        Assert.DoesNotContain(":0/", address + "/", StringComparison.Ordinal);
        Assert.Equal($"{address}/login?t={token}", Assert.Single(launcher.Opened));
    }
}
