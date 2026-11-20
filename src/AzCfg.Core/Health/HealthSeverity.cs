namespace AzCfg.Core.Health;

/// <summary>How serious a <see cref="HealthFinding"/> is: errors fail <c>status --check</c>, warnings do not.</summary>
public enum HealthSeverity
{
    Warning,
    Error,
}
