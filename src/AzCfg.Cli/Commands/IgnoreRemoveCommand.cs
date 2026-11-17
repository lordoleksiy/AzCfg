using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Removes a local ignore pattern from the bound project (ADR 0008; a missing pattern is a no-op) and prints the remaining
/// patterns. Never touches local files or Azure; an unbound project or an invalid pattern is an error.
/// </summary>
public sealed class IgnoreRemoveCommand(IProjectService projects, IAnsiConsole console) : AsyncCommand<IgnorePatternSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IgnorePatternSettings settings, CancellationToken ct)
    {
        var binding = await projects.RemoveIgnoreAsync(Path.GetFullPath(settings.Project), settings.Pattern, ct);
        IgnoreView.Render(console, settings.Json, binding.Ignore);
        return 0;
    }
}
