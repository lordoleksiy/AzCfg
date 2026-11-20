using AzCfg.Core.Model;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Renders a <see cref="StatusReport"/> for humans: binding facts, counters (⇡ local, ⇣ remote), conflicts, blocked keys and warnings. Keys only, never values.</summary>
public static class StatusView
{
    public static void Render(IAnsiConsole console, StatusReport report)
    {
        var s = report.Summary;
        console.MarkupLine($"Project: {Markup.Escape(report.Project)}");
        console.MarkupLine($"Service: {Markup.Escape(report.Service)}");
        console.MarkupLine($"Values target: {Describe(report.ValuesTarget)}");
        console.MarkupLine($"Secrets target: {Describe(report.SecretsTarget)}");
        console.MarkupLine($"Active environment: {Markup.Escape(report.ActiveEnvironment ?? "(none)")}");

        if (!s.IsActive)
        {
            console.MarkupLine($"[yellow]Environment '{Markup.Escape(s.Environment)}' is not the active one: the local files reflect '{Markup.Escape(report.ActiveEnvironment ?? "(none)")}', so nothing is compared. Run 'azcfg use {Markup.Escape(s.Environment)}' to switch.[/]");
            return;
        }

        console.MarkupLine($"Environment: {Markup.Escape(s.Environment)} (active)");
        console.MarkupLine($"⇡ {s.LocalChanges} local changes, ⇣ {s.RemoteChanges} remote changes, {s.Conflicts.Count} conflicts");
        console.MarkupLine($"Ignored keys: {s.IgnoredCount}");
        if (s.LocalChanges == 0 && s.RemoteChanges == 0 && s.Conflicts.Count == 0)
        {
            console.MarkupLine("Up to date.");
        }

        List("Conflicts", s.Conflicts);
        List("Blocked keys (a pull would not overwrite them)", s.BlockedKeys);
        WarningsView.Render(console, s.Warnings);

        void List(string title, IReadOnlyList<string> keys)
        {
            if (keys.Count == 0)
            {
                return;
            }

            console.MarkupLine($"[yellow]{Markup.Escape(title)}:[/]");
            keys.ToList().ForEach(k => console.MarkupLine($"  {Markup.Escape(k)}"));
        }
    }

    private static string Describe(LocalTargetBinding t) => Markup.Escape($"{t.Kind} ({t.Format}) {t.ProjectPath}");
}
