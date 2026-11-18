namespace AzCfg.Ui.Shared;

/// <summary>All commands of a circuit (scoped; implemented by P1).</summary>
public interface ICommandRegistry
{
    /// <summary>Contributors' commands plus dynamically registered ones.</summary>
    IReadOnlyList<UiCommand> All { get; }

    IDisposable Register(UiCommand command);

    /// <summary><c>false</c> when the id is unknown or the command is disabled.</summary>
    Task<bool> ExecuteAsync(string id);

    event Action? Changed;
}
