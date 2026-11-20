namespace AzCfg.Core.Health;

public interface IHealthService
{
    /// <summary>
    /// Runs the health checks for the project at <paramref name="repoPath"/>. Throws <see cref="InvalidOperationException"/> without a
    /// profile; otherwise never throws for a failed check (every failure becomes a finding). Without a project binding only the sign-in
    /// and environment access checks run. Keys and names only, never values.
    /// </summary>
    Task<HealthReport> CheckAsync(string repoPath, CancellationToken ct);
}
