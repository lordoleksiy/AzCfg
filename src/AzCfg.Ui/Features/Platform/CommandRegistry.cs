using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Platform;

/// <summary>
/// Commands of one circuit: the contributors' commands plus dynamically registered ones. A dynamic command replaces a
/// contributed one with the same id.
/// </summary>
public sealed class CommandRegistry(IEnumerable<ICommandContributor> contributors) : ICommandRegistry
{
    private readonly List<UiCommand> _dynamic = [];

    public IReadOnlyList<UiCommand> All =>
        [.. contributors.SelectMany(c => c.Commands).Concat(_dynamic).GroupBy(c => c.Id).Select(g => g.Last())];

    public event Action? Changed;

    public IDisposable Register(UiCommand command)
    {
        _dynamic.Add(command);
        Changed?.Invoke();
        return new Registration(() =>
        {
            if (_dynamic.Remove(command))
            {
                Changed?.Invoke();
            }
        });
    }

    /// <summary>Runs the command; <c>false</c> when the id is unknown or <see cref="UiCommand.IsEnabled"/> says no.</summary>
    public async Task<bool> ExecuteAsync(string id)
    {
        if (All.FirstOrDefault(c => c.Id == id) is not { } command || command.IsEnabled?.Invoke() == false)
        {
            return false;
        }

        await command.Execute();
        return true;
    }

    private sealed class Registration(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
