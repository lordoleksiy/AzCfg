namespace AzCfg.Core.Health;

/// <summary>Outcome of reading one environment's store: readable, sign-in needed, access denied, or anything else (network, not found, …).</summary>
public enum EnvironmentAccess
{
    Ok,
    AuthRequired,
    Forbidden,
    Unreachable,
}
