using AzCfg.Core.Health;

namespace AzCfg.Ui.Application.Health;

/// <summary>One finding of the health report. Keys and names only, never values. <see cref="Fix"/> is a ready-to-run <c>azcfg …</c> command.</summary>
public sealed record ProblemRow(HealthSeverity Severity, HealthCheckKind Kind, string Message, string? Environment, string? Key, string? Fix);
