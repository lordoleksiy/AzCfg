namespace AzCfg.Core.UseCases;

/// <summary>One per-user global setting stored in the profile (ADR 0008), e.g. <c>mode</c> or <c>env.dev.label</c>; <see cref="Value"/> is <c>null</c> when unset.</summary>
public sealed record UserSetting(string Key, string? Value);
