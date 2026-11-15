using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>A secret to create or update in Key Vault, taken from <c>{"@kv":"name","value":"..."}</c> in edit JSON.</summary>
public sealed record NewSecret(string Key, ConfigScope Scope, string Name, SecretString Value);
