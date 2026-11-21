namespace AzCfg.Ui.Application.Configuration;

/// <summary>A key present in one environment; <see cref="Value"/> only for plain values, never for secrets.</summary>
public sealed record MatrixCell(string? Value, bool IsSecret, string? SecretName);
