using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;

namespace AzCfg.Ui.Tests.Features.Environments.Fakes;

internal sealed class FakeAzureStatusService(ConnectionState state = ConnectionState.Connected) : IAzureStatusService
{
    private readonly Dictionary<string, EnvironmentHealth> _environments = new(StringComparer.OrdinalIgnoreCase);

    public AzureAccountStatus Account { get; private set; } = new(state, state == ConnectionState.Connected ? "dev@contoso.com" : null, null, null);

    public IReadOnlyDictionary<string, EnvironmentHealth> Environments => _environments;

    public List<(string Environment, string? Service)> EnvironmentChecks { get; } = [];

    public int AccountChecks { get; private set; }

    public ConnectionState NextEnvironmentState { get; set; } = ConnectionState.Connected;

    public event Action? Changed;

    public void SetAccount(ConnectionState newState)
    {
        Account = new AzureAccountStatus(newState, newState == ConnectionState.Connected ? "dev@contoso.com" : null, null, null);
        Changed?.Invoke();
    }

    public Task<AzureAccountStatus> CheckAccountAsync(CancellationToken ct)
    {
        AccountChecks++;
        return Task.FromResult(Account);
    }

    public Task<EnvironmentHealth> CheckEnvironmentAsync(ConfigEnvironment env, string? service, CancellationToken ct)
    {
        EnvironmentChecks.Add((env.Name, service));
        var health = new EnvironmentHealth(env.Name, NextEnvironmentState, null, DateTimeOffset.UnixEpoch);
        _environments[env.Name] = health;
        Changed?.Invoke();
        return Task.FromResult(health);
    }
}
