using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>Remote side of a row; <see cref="Value"/> and <see cref="ShadowedGlobalValue"/> only for plain values.</summary>
public sealed record RemoteCell(string? Value, bool IsSecret, Uri? SecretUri, string? SecretName, string? VaultHost,
    ConfigScope Scope, string? Label, string? ShadowedGlobalValue);
