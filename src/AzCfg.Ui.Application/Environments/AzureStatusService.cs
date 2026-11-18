using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Infrastructure.Azure;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace AzCfg.Ui.Application.Environments;

/// <summary>
/// <see cref="IAzureStatusService"/>. Successful checks are cached for 5 minutes (a failure is always retried, so a sign-in
/// is noticed at once). The access token is read for its claims only and is never stored or logged.
/// </summary>
public sealed class AzureStatusService(TokenCredential credential, IConfigStore store, TimeProvider time, ILogger<AzureStatusService> logger) : IAzureStatusService
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
        EnvironmentHealth result;
        try
        {
            await FirstAsync(env, service, ct);
            result = new EnvironmentHealth(env.Name, ConnectionState.Connected, null, time.GetUtcNow());
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
        catch (Exception ex)
        {
            logger.LogWarning("Environment '{Environment}' check failed: {ExceptionType}", env.Name, ex.GetType().Name);
            result = new EnvironmentHealth(env.Name, StateOf(ex), AzureErrors.Describe(ex), time.GetUtcNow());
        }

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

    private async Task FirstAsync(ConfigEnvironment env, string? service, CancellationToken ct)
    {
        if (service is null)
        {
            await using var prefixes = store.ListServicePrefixesAsync(env, ct).GetAsyncEnumerator(ct);
            await prefixes.MoveNextAsync();
            return;
        }

        await using var entries = store.ReadAsync(env, service, ct).GetAsyncEnumerator(ct);
        await entries.MoveNextAsync();
    }

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
