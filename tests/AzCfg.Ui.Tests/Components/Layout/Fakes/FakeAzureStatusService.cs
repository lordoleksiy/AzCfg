using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;

namespace AzCfg.Ui.Tests.Components.Layout.Fakes;

internal sealed class FakeAzureStatusService(ConnectionState state = ConnectionState.Connected, string? account = "dev@contoso.com") : IAzureStatusService
{
    public AzureAccountStatus Account { get; private set; } = new(state, account, null, null);

    public IReadOnlyDictionary<string, EnvironmentHealth> Environments { get; } = new Dictionary<string, EnvironmentHealth>(StringComparer.OrdinalIgnoreCase);

    public int Checks { get; private set; }

    public event Action? Changed;

    public void SetState(ConnectionState newState, string? newAccount = null)
    {
        Account = new AzureAccountStatus(newState, newAccount, null, null);
        Changed?.Invoke();
    }

    public Task<AzureAccountStatus> CheckAccountAsync(CancellationToken ct)
    {
        Checks++;
        return Task.FromResult(Account);
    }

    public Task<EnvironmentHealth> CheckEnvironmentAsync(ConfigEnvironment env, string? service, CancellationToken ct) =>
        Task.FromResult(new EnvironmentHealth(env.Name, ConnectionState.Connected, null, DateTimeOffset.UnixEpoch));
}
