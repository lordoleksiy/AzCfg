namespace AzCfg.Core.Model;

/// <summary>
/// How environments are separated in Azure App Configuration (ADR 0008): one store per environment
/// (labels are only read, never select the environment) or one shared store with a label per environment.
/// </summary>
public enum EnvironmentMode
{
    StorePerEnvironment,
    LabelPerEnvironment,
}
