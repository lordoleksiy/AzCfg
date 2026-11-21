using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Hosting;

/// <summary>
/// Loopback token login, cookie auth, fallback policy and security headers (AD-1, G21, G22). The composition root calls
/// <see cref="AddAzCfgHostSecurity"/>, <see cref="UseAzCfgHostSecurity"/> and <see cref="MapAzCfgEndpoints"/>.
/// </summary>
public static class HostSecurityExtensions
{
    public const string CookieName = "azcfg_ui";

    /// <summary>Token, cookie authentication (<c>azcfg_ui</c>, 401 instead of redirects), the authenticated-user fallback policy and the browser launcher.</summary>
    public static IServiceCollection AddAzCfgHostSecurity(this IServiceCollection services)
    {
        services.TryAddSingleton<AccessToken>();
        services.TryAddSingleton<BrowserLauncher>();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.Path = "/";
                options.Events.OnRedirectToLogin = Unauthorized;
                options.Events.OnRedirectToAccessDenied = Unauthorized;
            });
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(context => context.User.Identity?.IsAuthenticated == true || IsStaticAsset(context.Resource))
                .Build());
        return services;
    }

    public static WebApplication UseAzCfgHostSecurity(this WebApplication app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Lifetime.ApplicationStarted.Register(() => Announce(app));
        return app;
    }

    public static WebApplication MapAzCfgEndpoints(this WebApplication app)
    {
        app.MapGet("/login", LoginAsync).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> LoginAsync(HttpContext context, AccessToken token, string? t)
    {
        if (!token.Matches(t))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "azcfg")], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/");
    }

    private static Task Unauthorized(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static bool IsStaticAsset(object? resource) =>
        resource is HttpContext context && context.GetEndpoint()?.Metadata.GetMetadata<StaticAssetDescriptor>() is not null;

    /// <summary>Prints the real login URL (G22) to the console only — never through <see cref="ILogger"/> — and opens the browser unless <c>--no-browser</c>.</summary>
    private static void Announce(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (address is null)
        {
            return;
        }

        var url = $"{address.TrimEnd('/')}/login?t={app.Services.GetRequiredService<AccessToken>().Value}";
        var options = app.Services.GetRequiredService<UiHostOptions>();
        Console.WriteLine(options.Json ? System.Text.Json.JsonSerializer.Serialize(new { url }) : url);
        if (!options.NoBrowser)
        {
            app.Services.GetRequiredService<BrowserLauncher>().TryOpen(url);
        }
    }
}
