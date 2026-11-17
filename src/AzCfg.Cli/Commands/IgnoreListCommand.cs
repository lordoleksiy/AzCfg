using AzCfg.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Lists the local ignore patterns of the bound project (ADR 0008) from the saved binding only (no Azure call);
/// an unbound project is an error.
/// </summary>
public sealed class IgnoreListCommand(IUserStateStore state, IAnsiConsole console) : AsyncCommand<GlobalSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken ct)
    {
        var binding = await state.LoadBindingAsync(Path.GetFullPath(settings.Project), ct)
            ?? throw new InvalidOperationException("This project is not bound yet. Run 'azcfg pull' first.");
        IgnoreView.Render(console, settings.Json, binding.Ignore);
        return 0;
    }
}
