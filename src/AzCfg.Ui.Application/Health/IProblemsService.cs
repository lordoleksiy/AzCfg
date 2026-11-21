namespace AzCfg.Ui.Application.Health;

/// <summary>Thin adapter over Core <c>IHealthService</c> (FR-H1): the findings of <c>azcfg status --check</c> as rows (singleton).</summary>
public interface IProblemsService
{
    /// <summary>Runs the checks of the project at <paramref name="repoPath"/>; rows are ordered errors first, then as reported. No rules of its own.</summary>
    Task<ProblemsReport> CheckAsync(string repoPath, CancellationToken ct);
}
