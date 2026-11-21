using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Problems;

/// <summary><c>problems.refresh</c> runs the health check of the active project again.</summary>
public sealed class ProblemsCommands(ProblemsState state) : ICommandContributor
{
    public const string Refresh = "problems.refresh";

    public IEnumerable<UiCommand> Commands =>
    [
        new(Refresh, "Refresh problems", "Problems", null, state.RefreshAsync, () => state.ProjectId is not null),
    ];
}
