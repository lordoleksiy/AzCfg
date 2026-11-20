namespace AzCfg.Core.Health;

/// <summary>The check a <see cref="HealthFinding"/> comes from (ADR 0013 amendment, CLI 0.5.0).</summary>
public enum HealthCheckKind
{
    AzureSignIn,
    EnvironmentAccess,
    SecretsTargetNotIgnored,
    ValuesTargetNotIgnored,
    DeadSecretReference,
    SecretNotVerified,
    KeyMissingInEnvironments,
    BindingMismatch,
}
