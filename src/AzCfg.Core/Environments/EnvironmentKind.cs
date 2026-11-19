namespace AzCfg.Core.Environments;

/// <summary>How a client treats an environment, derived by <see cref="EnvironmentRules.KindOf"/>.</summary>
public enum EnvironmentKind
{
    Development,
    Staging,
    Production,
    Custom,
}
