using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Sync;

/// <summary>One conflict of a push plan; <see cref="Base"/>/<see cref="Local"/>/<see cref="Remote"/> only for plain values, else null.</summary>
public sealed record ConflictRow(string Key, ConfigScope Scope, bool IsSecret, string? Base, string? Local, string? Remote);
