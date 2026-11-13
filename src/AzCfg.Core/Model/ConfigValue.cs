namespace AzCfg.Core.Model;

public abstract record ConfigValue
{
    public sealed record Plain(string Value) : ConfigValue;
    public sealed record KeyVaultRef(Uri SecretUri) : ConfigValue;
    public sealed record ResolvedSecret(Uri SecretUri, string Value) : ConfigValue;
}
