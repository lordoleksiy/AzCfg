using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Lists the local ignore patterns of the bound project (ADR 0008); an unbound project is an error.</summary>
public sealed class IgnoreListCommand(IProjectService projects, IAnsiConsole console) : AsyncCommand<GlobalSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken ct)
    {
        var binding = (await projects.ResolveAsync(Path.GetFullPath(settings.Project), ct)).Existing
            ?? throw new InvalidOperationException("This project is not bound yet. Run 'azcfg pull' first.");
        IgnoreView.Render(console, settings.Json, binding.Ignore);
        return 0;
    }
}
