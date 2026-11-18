using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>One side of a compared key; <see cref="Value"/> only for plain values.</summary>
public sealed record CompareCell(string? Value, bool IsSecret, string? SecretName, ConfigScope Scope, string? Label);
