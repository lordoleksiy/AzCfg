using Microsoft.AspNetCore.Http;

namespace AzCfg.Ui.Hosting;

/// <summary>Adds the AD-1 security headers to every response, including 401/400 ones.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "font-src 'self' data:; worker-src 'self' blob:; connect-src 'self' ws://127.0.0.1:*; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
