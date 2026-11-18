using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Environments;

/// <summary>Azure sign-in and environment reachability (singleton; results cached 5 minutes).</summary>
public interface IAzureStatusService
{
    AzureAccountStatus Account { get; }

    /// <summary>By environment name, OrdinalIgnoreCase.</summary>
    IReadOnlyDictionary<string, EnvironmentHealth> Environments { get; }

    event Action? Changed;

    /// <summary>
    /// Token for <c>https://management.azure.com/.default</c>; account/tenant from JWT claims (upn | unique_name |
    /// preferred_username, tid; base64url decode without validation, display only). The token is never stored or logged.
    /// </summary>
    Task<AzureAccountStatus> CheckAccountAsync(CancellationToken ct);

    /// <summary>Probe: first item of <c>IConfigStore.ReadAsync(env, service)</c>, or of <c>ListServicePrefixesAsync</c> when <paramref name="service"/> is null.</summary>
    Task<EnvironmentHealth> CheckEnvironmentAsync(ConfigEnvironment env, string? service, CancellationToken ct);
}
