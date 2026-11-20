using AzCfg.Core.Health;
using AzCfg.Core.Model;
using AzCfg.Infrastructure.Azure;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace AzCfg.Ui.Application.Environments;

/// <summary>
/// <see cref="IAzureStatusService"/>. Successful checks are cached for 5 minutes (a failure is always retried, so a sign-in
/// is noticed at once). The access token is read for its claims only and is never stored or logged. The environment probe and
/// its classification are Core's (<see cref="IHealthService.CheckEnvironmentAsync"/>); this service adds the cache and events.
/// </summary>
public sealed class AzureStatusService(TokenCredential credential, IHealthService health, TimeProvider time, ILogger<AzureStatusService> logger) : IAzureStatusService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private static readonly string[] ManagementScope = ["https://management.azure.com/.default"];

    private readonly object _gate = new();
    private readonly Dictionary<string, EnvironmentHealth> _environments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Uri Store, string? Label, string? Service)> _probed = new(StringComparer.OrdinalIgnoreCase);
    private AzureAccountStatus _account = new(ConnectionState.Unknown, null, null, null);
    private DateTimeOffset _accountCheckedAt;

    public AzureAccountStatus Account
    {
        get
        {
            lock (_gate)
            {
                return _account;
            }
        }
    }

    public IReadOnlyDictionary<string, EnvironmentHealth> Environments
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, EnvironmentHealth>(_environments, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public event Action? Changed;

    public async Task<AzureAccountStatus> CheckAccountAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_account.State == ConnectionState.Connected && time.GetUtcNow() - _accountCheckedAt < CacheFor)
            {
                return _account;
            }

            _account = _account with { State = ConnectionState.Checking, Message = null };
        }

        Changed?.Invoke();
        AzureAccountStatus result;
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(ManagementScope), ct);
            var (account, tenant) = JwtClaims.Read(token.Token);
            result = new AzureAccountStatus(ConnectionState.Connected, account, tenant, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Set(new AzureAccountStatus(ConnectionState.Unknown, null, null, null));
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Azure account check failed: {ExceptionType}", ex.GetType().Name);
            result = new AzureAccountStatus(StateOf(ex), null, null, AzureErrors.Describe(ex));
        }

        Set(result);
        return result;
    }

    public async Task<EnvironmentHealth> CheckEnvironmentAsync(ConfigEnvironment env, string? service, CancellationToken ct)
    {
        var key = (env.StoreEndpoint, env.Label, service);
        lock (_gate)
        {
            if (_environments.TryGetValue(env.Name, out var cached) && cached.State == ConnectionState.Connected
                && _probed.TryGetValue(env.Name, out var probed) && probed == key && time.GetUtcNow() - cached.CheckedAt < CacheFor)
            {
                return cached;
            }

            _environments[env.Name] = new EnvironmentHealth(env.Name, ConnectionState.Checking, null, time.GetUtcNow());
        }

        Changed?.Invoke();
        EnvironmentCheck check;
        try
        {
            check = await health.CheckEnvironmentAsync(env, service, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate)
            {
                _environments.Remove(env.Name);
            }

            Changed?.Invoke();
            throw;
        }

        var result = new EnvironmentHealth(env.Name, StateOf(check.Access), check.Error, time.GetUtcNow());

        lock (_gate)
        {
            _environments[env.Name] = result;
            _probed[env.Name] = key;
        }

        Changed?.Invoke();
        return result;
    }

    private static ConnectionState StateOf(Exception ex) => AzureErrors.Classify(ex) switch
    {
        AzureErrorKind.Auth => ConnectionState.AuthRequired,
        AzureErrorKind.Forbidden => ConnectionState.Forbidden,
        _ => ConnectionState.Unreachable,
    };

    private static ConnectionState StateOf(EnvironmentAccess access) => access switch
    {
        EnvironmentAccess.Ok => ConnectionState.Connected,
        EnvironmentAccess.AuthRequired => ConnectionState.AuthRequired,
        EnvironmentAccess.Forbidden => ConnectionState.Forbidden,
        _ => ConnectionState.Unreachable,
    };

    private void Set(AzureAccountStatus status)
    {
        lock (_gate)
        {
            _account = status;
            _accountCheckedAt = time.GetUtcNow();
        }

        Changed?.Invoke();
    }
}
