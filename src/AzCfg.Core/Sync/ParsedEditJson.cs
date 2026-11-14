namespace AzCfg.Core.Sync;

/// <summary>Result of parsing edit JSON: the local <see cref="ConfigState"/> and the secrets that carry a value.</summary>
public sealed record ParsedEditJson(ConfigState State, IReadOnlyList<NewSecret> NewSecrets);
