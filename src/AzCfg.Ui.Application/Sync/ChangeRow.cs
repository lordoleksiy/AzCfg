using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Sync;

/// <summary>
/// One planned change. <see cref="Key"/> = <c>(After ?? Before)?.Key</c>, or the <see cref="SecretName"/> for a
/// CreateSecret awaiting a vault (<see cref="AwaitsVault"/>). <see cref="Before"/>/<see cref="After"/> only for plain values.
/// </summary>
public sealed record ChangeRow(string Key, ChangeKind Kind, ConfigScope Scope, bool IsSecret, string? Before, string? After, string? SecretName, bool AwaitsVault);
