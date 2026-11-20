using AzCfg.Core.Health;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Renders a <see cref="HealthReport"/> for humans: sign-in line, one line per environment, findings grouped by severity with their fix command. Names only, never values.</summary>
public static class HealthView
{
    public static void Render(IAnsiConsole console, HealthReport report)
    {
        var account = report.Account;
        console.MarkupLine(account.SignedIn
            ? $"[green]✓[/] Azure: signed in{(account.Account is null ? "" : $" as {Markup.Escape(account.Account)}")}"
            : $"[red]✗[/] Azure: not signed in{(account.Error is null ? "" : $" ({Markup.Escape(account.Error)})")}");

        foreach (var env in report.Environments)
        {
            console.MarkupLine(env.Reachable
                ? $"[green]✓[/] {Markup.Escape(env.Name)}: reachable"
                : $"[red]✗[/] {Markup.Escape(env.Name)}: not reachable{(env.Error is null ? "" : $" ({Markup.Escape(env.Error)})")}");
        }

        if (report.Findings.Count == 0)
        {
            console.MarkupLine("No problems found.");
            return;
        }

        Group(console, "Errors", "red", report.Findings.Where(f => f.Severity == HealthSeverity.Error));
        Group(console, "Warnings", "yellow", report.Findings.Where(f => f.Severity == HealthSeverity.Warning));
    }

    private static void Group(IAnsiConsole console, string title, string color, IEnumerable<HealthFinding> findings)
    {
        var list = findings.ToList();
        if (list.Count == 0)
        {
            return;
        }

        console.MarkupLine($"[{color}]{title}:[/]");
        foreach (var f in list)
        {
            console.MarkupLine($"  {Markup.Escape(f.Message)}");
            if (f.Fix is not null)
            {
                console.MarkupLine($"    fix: {Markup.Escape(f.Fix)}");
            }
        }
    }
}
