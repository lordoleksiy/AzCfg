using System.Net;
using AzCfg.Infrastructure;
using AzCfg.Ui.Application;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Components;
using AzCfg.Ui.Demo;
using AzCfg.Ui.Features;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Hosting;

/// <summary>Composes the UI host (AD-1, AD-2); <c>Program</c> and the host tests both start from here.</summary>
public static class AzCfgUiApp
{
    /// <summary>
    /// Builds the app: Kestrel only on <c>127.0.0.1:&lt;port&gt;</c>, Core/Infrastructure/Application/UI services, the demo
    /// world when <c>--demo</c> is set, then <paramref name="overrides"/> (tests). The container is validated on build.
    /// </summary>
    public static WebApplication Build(string[] args, Action<IServiceCollection>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = NormalizeArgs(args),
            ApplicationName = typeof(AzCfgUiApp).Assembly.GetName().Name,
        });
        var options = builder.Configuration.Get<UiHostOptions>() ?? new UiHostOptions();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateOnBuild = true;
            o.ValidateScopes = true;
        });
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, options.Port));
        builder.WebHost.UseStaticWebAssets();

        var services = builder.Services;
        services.AddSingleton(options);
        services.AddAzCfg();
        services.AddAzCfgUiApplication();
        services.AddSingleton(new UiStatePaths(options.StateRoot));
        services.AddFluentUIComponents();
        services.AddSingleton(TimeProvider.System);
        services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 2 * 1024 * 1024);
        services.AddAzCfgHostSecurity();
        services.AddAzCfgUi();
        if (options.Demo is not null)
        {
            services.AddAzCfgDemo(options);
        }

        overrides?.Invoke(services);

        var app = builder.Build();
        app.UseAzCfgHostSecurity();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.MapAzCfgEndpoints();
        return app;
    }

    /// <summary>Bare switches get a value so the command-line provider can bind them: <c>--no-browser</c> → <c>=true</c>, <c>--demo</c> → <c>=full</c>.</summary>
    public static string[] NormalizeArgs(string[] args) =>
        [.. args.Select(a => a switch
        {
            "--no-browser" => "--no-browser=true",
            "--demo" => "--demo=full",
            _ => a,
        })];
}
