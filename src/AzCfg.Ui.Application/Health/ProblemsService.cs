using AzCfg.Core.Health;

namespace AzCfg.Ui.Application.Health;

internal sealed class ProblemsService(IHealthService health) : IProblemsService
{
    public async Task<ProblemsReport> CheckAsync(string repoPath, CancellationToken ct)
    {
        var report = await health.CheckAsync(repoPath, ct);
        return new([.. report.Findings
            .Select(f => new ProblemRow(f.Severity, f.Kind, f.Message, f.Environment, f.Key, f.Fix))
            .OrderByDescending(r => r.Severity)]);
    }
}
