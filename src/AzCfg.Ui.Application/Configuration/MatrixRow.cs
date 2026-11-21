using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>One key of the consistency matrix; <see cref="Cells"/> follow <see cref="MissingKeysView.Environments"/>, null = missing there.</summary>
public sealed record MatrixRow(string Key, ConfigScope Scope, bool HasGap, IReadOnlyList<MatrixCell?> Cells);
