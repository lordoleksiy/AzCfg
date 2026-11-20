using AzCfg.Core.Model;
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

    /// <summary>
    /// Human-mode warning for plan/push of local files when nothing has been pulled yet (for instance right after <c>config set project.service</c>):
    /// the local files may still hold another service's keys. No-op otherwise.
    /// </summary>
    public static void RenderNoActiveEnvironment(IAnsiConsole console, ProjectBinding binding)
    {
        if (binding.ActiveEnvironment is null)
        {
            console.MarkupLine($"[yellow]Warning:[/] {Markup.Escape($"no environment has been pulled for this project; local files may not belong to service {binding.Service} — run 'azcfg use <ENV>' first.")}");
        }
    }
}
