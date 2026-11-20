namespace AzCfg.Core.Health;

/// <summary>A classified access failure; <see cref="Reason"/> is a fixed, value-free text (never an exception message).</summary>
public sealed record AccessFailure(EnvironmentAccess Access, string Reason);
