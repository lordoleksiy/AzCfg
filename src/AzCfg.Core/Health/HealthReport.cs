namespace AzCfg.Core.Health;

/// <summary>Configuration health of the user's setup and the project (<c>status --check</c>). <see cref="HasErrors"/> is true when any finding is an error.</summary>
public sealed record HealthReport(AzureAccountCheck Account, IReadOnlyList<EnvironmentCheck> Environments, IReadOnlyList<HealthFinding> Findings)
{
    public bool HasErrors => Findings.Any(f => f.Severity == HealthSeverity.Error);
}
