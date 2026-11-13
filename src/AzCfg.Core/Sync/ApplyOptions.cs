namespace AzCfg.Core.Sync;

public sealed record ApplyOptions(string? ConfirmedEnvironment = null, bool ConfirmGlobal = false);
