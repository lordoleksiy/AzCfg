namespace AzCfg.Core.Health;

/// <summary>Whether the store of one configured environment could be read; <see cref="Error"/> is a short, value-free reason.</summary>
public sealed record EnvironmentCheck(string Name, EnvironmentAccess Access, string? Error)
{
    public bool Reachable => Access == EnvironmentAccess.Ok;
}
