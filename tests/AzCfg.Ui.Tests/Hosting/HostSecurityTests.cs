using System.Net;
using AzCfg.Ui.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AzCfg.Ui.Tests.Hosting;

public sealed class HostSecurityTests : IDisposable
{
    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add($"{formatter(state, exception)} {exception}");
            }
        }

        public void Dispose()
        {
        }
    }

    private readonly CapturingLoggerProvider _logs = new();
    private readonly WebApplicationFactory<Program> _factory;

    public HostSecurityTests() =>
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(_logs)));

    public void Dispose() => _factory.Dispose();

    private string Token => _factory.Services.GetRequiredService<AccessToken>().Value;

    private HttpClient NewClient(bool cookies = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = cookies });

    [Fact]
    public async Task Get_Root_WithoutCookie_Is401()
    {
        var response = await NewClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_BlazorNegotiate_WithoutCookie_Is401()
    {
        var response = await NewClient().PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/login?t=")]
    [InlineData("/login?t=wrong")]
    public async Task Login_WrongOrMissingToken_Is401WithoutCookie(string url)
    {
        var response = await NewClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_RightToken_RedirectsWithStrictHttpOnlyCookie_ThenRootIs200()
    {
        var client = NewClient();
        var login = await client.GetAsync($"/login?t={Token}");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location?.OriginalString);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("azcfg_ui=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires", cookie, StringComparison.OrdinalIgnoreCase);
        var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
    }

    [Fact]
    public async Task Get_StaticAsset_WithoutCookie_IsAnonymous()
    {
        var response = await NewClient().GetAsync("/css/app.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_ForeignHost_Is400()
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Host = "evil.example";
        var response = await client.GetAsync($"/login?t={Token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login?t=wrong")]
    public async Task Responses_CarrySecurityHeaders(string url)
    {
        var response = await NewClient().GetAsync(url);
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task FullLoginSequence_NeverLogsTheToken()
    {
        var client = NewClient();
        await client.GetAsync("/login?t=wrong");
        await client.GetAsync($"/login?t={Token}");
        await client.GetAsync("/");
        await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        lock (_logs.Lines)
        {
            Assert.DoesNotContain(_logs.Lines, l => l.Contains(Token, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AccessToken_Is32BytesBase64Url_AndNeverPrinted()
    {
        var token = new AccessToken();
        Assert.Equal(43, token.Value.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token.Value);
        Assert.NotEqual(token.Value, new AccessToken().Value);
        Assert.DoesNotContain(token.Value, token.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("short", false)]
    public void AccessToken_Matches_RejectsBadCandidates(string? candidate, bool expected) =>
        Assert.Equal(expected, new AccessToken().Matches(candidate));

    [Fact]
    public void AccessToken_Matches_AcceptsOwnValueOnly()
    {
        var token = new AccessToken();
        Assert.True(token.Matches(token.Value));
        Assert.False(token.Matches(token.Value + "x"));
        Assert.False(token.Matches(new AccessToken().Value));
    }
}
