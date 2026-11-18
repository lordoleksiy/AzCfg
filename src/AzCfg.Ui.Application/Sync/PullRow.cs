using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Sync;

/// <summary>One key of a pull preview; <see cref="Local"/>/<see cref="Remote"/> only for plain values, else null.</summary>
public sealed record PullRow(string Key, DiffKind Kind, bool IsSecret, string? Local, string? Remote);
