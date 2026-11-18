using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Configuration;

public sealed record ConfigurationView(ProjectBinding Binding, ConfigEnvironment Environment, bool IsRemoteOnly,
    IReadOnlyList<ConfigRow> Rows, IReadOnlyList<SyncWarning> Warnings, DateTimeOffset LoadedAt);
