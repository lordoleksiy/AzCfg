using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Features.Sync;

/// <summary>Labels and CSS classes of the Sync rows.</summary>
public static class SyncText
{
    public static string Kind(ChangeKind kind) => kind switch
    {
        ChangeKind.Add => "Add",
        ChangeKind.Update => "Update",
        ChangeKind.Delete => "Delete",
        _ => "Secret",
    };

    public static string KindClass(ChangeKind kind) => kind switch
    {
        ChangeKind.Add => "added",
        ChangeKind.Delete => "deleted",
        _ => "modified",
    };

    public static string Kind(DiffKind kind) => kind switch
    {
        DiffKind.RemoteAdded => "Add",
        DiffKind.RemoteDeleted => "Delete",
        DiffKind.Conflict => "Conflict",
        _ => "Update",
    };

    public static string Block(ApplyBlock block) => block switch
    {
        ApplyBlock.NothingSelected => "Nothing selected",
        ApplyBlock.Conflicts => "Resolve the conflicts of the selected keys first",
        ApplyBlock.VaultChoiceRequired => "New secrets need a vault: Choose vault…",
        ApplyBlock.EnvironmentNotConfirmed => "Confirmation needed: type the environment name",
        _ => "Confirmation needed: global keys",
    };
}
