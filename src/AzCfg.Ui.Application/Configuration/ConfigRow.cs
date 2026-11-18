using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>One key of a Browse view; <see cref="Status"/> is null in remote-only views and for ignored keys.</summary>
public sealed record ConfigRow(string Key, DiffKind? Status, bool IsSecret, bool IsIgnored, LocalCell? Local, RemoteCell? Remote);
