using Microsoft.AspNetCore.Builder;

namespace AzCfg.Ui.Hosting;

/// <summary>Loopback token login, cookie auth, fallback policy and security headers (AD-1). Filled by P2.</summary>
public static class HostSecurityExtensions
{
    public static WebApplication UseAzCfgHostSecurity(this WebApplication app) => app;

    public static WebApplication MapAzCfgEndpoints(this WebApplication app) => app;
}
