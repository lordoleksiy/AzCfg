namespace AzCfg.Ui.Application.Environments;

public sealed record EnvironmentHealth(string Environment, ConnectionState State, string? Message, DateTimeOffset CheckedAt);
