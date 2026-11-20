namespace AzCfg.Core.Health;

/// <summary>A problem found by a health check. Keys and names only, never values. <see cref="Fix"/> is a ready-to-run <c>azcfg …</c> command when one exists.</summary>
public sealed record HealthFinding(
    HealthCheckKind Kind,
    HealthSeverity Severity,
    string Message,
    string? Environment = null,
    string? Key = null,
    string? Fix = null);
