using AzCfg.Core.Sync;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Renders sync warnings (ADR 0008) as yellow <c>Warning:</c> lines; messages are escaped and never contain secret values.</summary>
public static class WarningsView
{
    public static void Render(IAnsiConsole console, IEnumerable<SyncWarning> warnings)
    {
        foreach (var w in warnings)
        {
            console.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(w.Message)}");
        }
    }
}
