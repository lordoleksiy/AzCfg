using AzCfg.Core.Health;

namespace AzCfg.Ui.Application.Health;

/// <summary>The rows of one health check of a project, errors first.</summary>
public sealed record ProblemsReport(IReadOnlyList<ProblemRow> Rows)
{
    public int Errors => Rows.Count(r => r.Severity == HealthSeverity.Error);

    public int Warnings => Rows.Count(r => r.Severity == HealthSeverity.Warning);
}
