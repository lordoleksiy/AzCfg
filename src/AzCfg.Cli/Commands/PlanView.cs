using AzCfg.Core.Sync;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Renders a plan as tables of changes and conflicts; secrets appear by key and secret name only.</summary>
public static class PlanView
{
    public static void Render(IAnsiConsole console, SyncPlan plan)
    {
        if (plan.Changes.Count > 0)
        {
            var changes = new Table().AddColumns("Change", "Scope", "Key", "Secret");
            foreach (var c in plan.Changes)
            {
                var entry = (c.After ?? c.Before)!;
                changes.AddRow(c.Kind.ToString(), entry.Scope.ToString(), Markup.Escape(entry.Key), Markup.Escape(c.SecretName ?? string.Empty));
            }

            console.Write(changes);
        }

        if (plan.Conflicts.Count > 0)
        {
            var conflicts = new Table().Title("[yellow]Conflicts[/]").AddColumns("Scope", "Key");
            plan.Conflicts.ToList().ForEach(c => conflicts.AddRow((c.Local ?? c.Remote ?? c.Base)!.Scope.ToString(), Markup.Escape(c.Key)));
            console.Write(conflicts);
        }
    }
}
