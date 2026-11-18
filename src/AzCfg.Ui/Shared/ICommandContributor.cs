namespace AzCfg.Ui.Shared;

/// <summary>Contributes commands of a feature (registered scoped via <c>AddCommands&lt;T&gt;</c>).</summary>
public interface ICommandContributor
{
    IEnumerable<UiCommand> Commands { get; }
}
