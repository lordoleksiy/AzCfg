using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>Local side of a row; <see cref="Value"/> is null when <see cref="Masked"/>.</summary>
public sealed record LocalCell(string? Value, bool Masked, LocalTargetBinding Source, ConfigScope Scope);
