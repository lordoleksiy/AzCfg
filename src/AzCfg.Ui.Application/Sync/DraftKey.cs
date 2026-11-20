using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Sync;

public sealed record DraftKey(ConfigScope Scope, string Key);
