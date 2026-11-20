using AzCfg.Core.Model;

namespace AzCfg.Core.Health;

public interface IHealthService
{
    /// <summary>
    /// Runs the health checks for the project at <paramref name="repoPath"/>. Throws <see cref="InvalidOperationException"/> without a
    /// profile; otherwise never throws for a failed check (every failure becomes a finding). Without a project binding only the sign-in
    /// and environment access checks run. Keys and names only, never values.
    /// </summary>
    Task<HealthReport> CheckAsync(string repoPath, CancellationToken ct);

    /// <summary>
    /// Probes one environment: the first entry of <paramref name="service"/>, or the first service prefix when it is <c>null</c>.
    /// Never throws for a failed read (the failure is classified); a cancellation of <paramref name="ct"/> is rethrown.
    /// </summary>
    Task<EnvironmentCheck> CheckEnvironmentAsync(ConfigEnvironment env, string? service, CancellationToken ct);
}
