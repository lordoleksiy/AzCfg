using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Tests.Application.Workspace;
using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AzCfg.Ui.Tests.Application.Environments;

public sealed class AzureStatusServiceTests : IDisposable
{
    private readonly WorkspaceTestWorld _w = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    public void Dispose() => _w.Dispose();

    private AzureStatusService Service(TokenCredential? credential = null, IConfigStore? store = null) =>
        new(credential ?? _w.Get<TokenCredential>(), store ?? _w.World.AppConfig, _time, NullLogger<AzureStatusService>.Instance);

    [Fact]
    public async Task CheckAccountAsync_StaticTokenCredential_ConnectedWithAccountAndTenant()
    {
        var service = new AzureStatusService(new StaticTokenCredential("dev@corp.com", "tenant-1"), _w.World.AppConfig, _time, NullLogger<AzureStatusService>.Instance);

        var status = await service.CheckAccountAsync(default);

        Assert.Equal(ConnectionState.Connected, status.State);
        Assert.Equal("dev@corp.com", status.Account);
        Assert.Equal("tenant-1", status.TenantId);
        Assert.Equal(status, service.Account);
    }

    [Fact]
    public async Task CheckAccountAsync_AuthenticationFailed_AuthRequired()
    {
        var status = await Service(new ThrowingCredential(new AuthenticationFailedException("secret detail"))).CheckAccountAsync(default);

        Assert.Equal(ConnectionState.AuthRequired, status.State);
        Assert.Contains("az login", status.Message);
        Assert.DoesNotContain("secret detail", status.Message);
    }

    [Fact]
    public async Task CheckAccountAsync_Forbidden_Forbidden()
    {
        var status = await Service(new ThrowingCredential(new RequestFailedException(403, "no"))).CheckAccountAsync(default);

        Assert.Equal(ConnectionState.Forbidden, status.State);
    }

    [Fact]
    public async Task CheckAccountAsync_Network_Unreachable()
    {
        var status = await Service(new ThrowingCredential(new HttpRequestException("down"))).CheckAccountAsync(default);

        Assert.Equal(ConnectionState.Unreachable, status.State);
    }

    [Fact]
    public async Task CheckAccountAsync_Cancelled_ThrowsAndResetsToUnknown()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var service = Service(new ThrowingCredential(new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.CheckAccountAsync(cts.Token));

        Assert.Equal(ConnectionState.Unknown, service.Account.State);
    }

    [Fact]
    public async Task CheckAccountAsync_Connected_CachedForFiveMinutes()
    {
        var credential = new CountingCredential(new StaticTokenCredential());
        var service = Service(credential);

        await service.CheckAccountAsync(default);
        _time.Advance(TimeSpan.FromMinutes(4));
        await service.CheckAccountAsync(default);
        _time.Advance(TimeSpan.FromMinutes(2));
        await service.CheckAccountAsync(default);

        Assert.Equal(2, credential.Calls);
    }

    [Fact]
    public async Task CheckAccountAsync_Failure_NotCached()
    {
        var credential = new ThrowingCredential(new AuthenticationFailedException("x"));
        var service = Service(credential);

        await service.CheckAccountAsync(default);
        await service.CheckAccountAsync(default);

        Assert.Equal(2, credential.Calls);
    }

    [Fact]
    public async Task CheckAccountAsync_RaisesChanged()
    {
        var service = Service();
        var changes = 0;
        service.Changed += () => changes++;

        await service.CheckAccountAsync(default);

        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_ServiceKnown_ConnectedAndStoredByNameIgnoringCase()
    {
        _w.World.AppConfig.Set("https://dev.azconfig.io", "crm:A", "1");
        var service = Service();

        var health = await service.CheckEnvironmentAsync(_w.Env("dev"), "crm", default);

        Assert.Equal(ConnectionState.Connected, health.State);
        Assert.Equal(health, service.Environments["DEV"]);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_NoService_ProbesPrefixes()
    {
        var store = new RecordingStore();

        await Service(store: store).CheckEnvironmentAsync(_w.Env("dev"), null, default);

        Assert.Equal(["prefixes"], store.Calls);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_WithService_ProbesRead()
    {
        var store = new RecordingStore();

        await Service(store: store).CheckEnvironmentAsync(_w.Env("dev"), "crm", default);

        Assert.Equal(["read:crm"], store.Calls);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_Forbidden_ForbiddenWithMessage()
    {
        var service = Service(store: new RecordingStore(new RequestFailedException(403, "no")));

        var health = await service.CheckEnvironmentAsync(_w.Env("dev"), "crm", default);

        Assert.Equal(ConnectionState.Forbidden, health.State);
        Assert.Contains("403", health.Message);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_Auth_AuthRequired()
    {
        var service = Service(store: new RecordingStore(new AuthenticationFailedException("x")));

        Assert.Equal(ConnectionState.AuthRequired, (await service.CheckEnvironmentAsync(_w.Env("dev"), null, default)).State);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_Connected_CachedUntilExpiry()
    {
        var store = new RecordingStore();
        var service = Service(store: store);

        await service.CheckEnvironmentAsync(_w.Env("dev"), "crm", default);
        await service.CheckEnvironmentAsync(_w.Env("dev"), "crm", default);
        _time.Advance(TimeSpan.FromMinutes(6));
        await service.CheckEnvironmentAsync(_w.Env("dev"), "crm", default);

        Assert.Equal(2, store.Calls.Count);
    }

    [Fact]
    public async Task CheckEnvironmentAsync_DifferentService_NotServedFromCache()
    {
        var store = new RecordingStore();
        var service = Service(store: store);

        await service.CheckEnvironmentAsync(_w.Env("dev"), "crm", default);
        await service.CheckEnvironmentAsync(_w.Env("dev"), "billing", default);

        Assert.Equal(2, store.Calls.Count);
    }

    private sealed class ThrowingCredential(Exception exception) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Throw();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(Throw());

        private AccessToken Throw()
        {
            Calls++;
            throw exception;
        }
    }

    private sealed class CountingCredential(TokenCredential inner) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.GetToken(requestContext, cancellationToken);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.GetTokenAsync(requestContext, cancellationToken);
        }
    }

    private sealed class RecordingStore(Exception? failure = null) : IConfigStore
    {
        public List<string> Calls { get; } = [];

        public async IAsyncEnumerable<ConfigEntry> ReadAsync(ConfigEnvironment env, string service, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Calls.Add($"read:{service}");
            await Task.Yield();
            if (failure is not null)
            {
                throw failure;
            }

            yield return new ConfigEntry("A", ConfigScope.Service, new ConfigValue.Plain("1"));
        }

        public async IAsyncEnumerable<string> ListServicePrefixesAsync(ConfigEnvironment env, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Calls.Add("prefixes");
            await Task.Yield();
            if (failure is not null)
            {
                throw failure;
            }

            yield return "crm";
        }

        public Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListLabelsAsync(Uri storeEndpoint, CancellationToken ct) => throw new NotSupportedException();
    }
}
