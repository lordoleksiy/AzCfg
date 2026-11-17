using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Adds a local ignore pattern to the bound project (ADR 0008) and prints the resulting patterns plus a hint that nothing was
/// deleted. Never touches local files or Azure; an unbound project or an invalid pattern is an error.
/// </summary>
public sealed class IgnoreAddCommand(IProjectService projects, IAnsiConsole console) : AsyncCommand<IgnorePatternSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IgnorePatternSettings settings, CancellationToken ct)
    {
        var binding = await projects.AddIgnoreAsync(Path.GetFullPath(settings.Project), settings.Pattern, ct);
        IgnoreView.Render(console, settings.Json, binding.Ignore);
        if (!settings.Json)
        {
            console.WriteLine("Local values of matching keys are kept; pull and push skip them. Nothing was deleted.");
        }

        return 0;
    }
}
