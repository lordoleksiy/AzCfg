using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Sync;

public sealed record PushPreview(ProjectBinding Binding, ConfigEnvironment Environment, PushOrigin Origin, SyncPlan Plan,
    IReadOnlyList<ChangeRow> Changes, IReadOnlyList<ConflictRow> Conflicts, IReadOnlyList<SyncWarning> Warnings);
