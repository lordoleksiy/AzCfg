using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Sync;

/// <summary>
/// <see cref="Incoming"/> = Remote{Added,Modified,Deleted}; <see cref="Conflicts"/> = Conflict; <see cref="LocalChanges"/> =
/// keys Local{Added,Modified,Deleted}.
/// </summary>
public sealed record PullPreview(ProjectBinding Binding, ConfigEnvironment Environment, IReadOnlyList<PullRow> Incoming,
    IReadOnlyList<PullRow> Conflicts, IReadOnlyList<string> LocalChanges);
