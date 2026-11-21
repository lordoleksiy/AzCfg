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
        var builder = WebApplication.CreateBuilder(CreateWebOptions(args));
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

    /// <summary>
    /// When the UI runs inside the <c>azcfg</c> tool the static assets live next to the binaries (<c>wwwroot</c> and
    /// <c>&lt;entry&gt;.staticwebassets.endpoints.json</c> in the install folder), not under the current directory:
    /// the content root and web root then point to <see cref="AppContext.BaseDirectory"/> and the application name to the
    /// assembly that owns the endpoints manifest. Otherwise (<c>dotnet run</c>, tests) the defaults apply.
    /// </summary>
    internal static WebApplicationOptions CreateWebOptions(string[] args)
    {
        var uiName = typeof(AzCfgUiApp).Assembly.GetName().Name;
        var options = new WebApplicationOptions { Args = NormalizeArgs(args), ApplicationName = uiName };
        var baseDir = AppContext.BaseDirectory;
        var hostName = new[] { uiName, System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name }
            .FirstOrDefault(n => n is not null && File.Exists(Path.Combine(baseDir, n + ".staticwebassets.endpoints.json")));
        if (hostName is null || !Directory.Exists(Path.Combine(baseDir, "wwwroot")))
        {
            return options;
        }

        return new WebApplicationOptions
        {
            Args = options.Args,
            ApplicationName = hostName,
            ContentRootPath = baseDir,
            WebRootPath = Path.Combine(baseDir, "wwwroot"),
        };
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
