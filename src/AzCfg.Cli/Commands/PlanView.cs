using AzCfg.Core.Sync;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Renders a plan as tables of changes (with each key's label, <c>(none)</c> when unlabeled) and conflicts, followed by its
/// warnings; empty tables are omitted. Secrets appear by key and secret name only.
/// </summary>
public static class PlanView
{
    public static void Render(IAnsiConsole console, SyncPlan plan)
    {
        if (plan.Changes.Count > 0)
        {
            var changes = new Table().AddColumns("Change", "Scope", "Key", "Label", "Secret");
            foreach (var c in plan.Changes)
            {
                var entry = (c.After ?? c.Before)!;
                changes.AddRow(
                    c.Kind.ToString(),
                    entry.Scope.ToString(),
                    Markup.Escape(entry.Key),
                    Markup.Escape(entry.Label ?? "(none)"),
                    Markup.Escape(c.SecretName ?? string.Empty));
            }

            console.Write(changes);
        }

        if (plan.Conflicts.Count > 0)
        {
            var conflicts = new Table().Title("[yellow]Conflicts[/]").AddColumns("Scope", "Key");
            plan.Conflicts.ToList().ForEach(c => conflicts.AddRow((c.Local ?? c.Remote ?? c.Base)!.Scope.ToString(), Markup.Escape(c.Key)));
            console.Write(conflicts);
        }

        WarningsView.Render(console, plan.Warnings);
    }
}
