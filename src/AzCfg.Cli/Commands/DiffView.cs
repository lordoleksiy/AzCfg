using AzCfg.Core.Sync;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Renders comparisons for humans. Plain values are shown; secrets appear only as a name and same/different.</summary>
public static class DiffView
{
    public static void Render(IAnsiConsole console, string left, string right, ConfigComparison comparison, int sameCount)
    {
        console.MarkupLine($"Comparing {Markup.Escape(left)} ↔ {Markup.Escape(right)}");
        if (comparison.Items.Count == 0)
        {
            console.MarkupLine("No differences.");
        }
        else
        {
            var table = new Table().AddColumn("Key").AddColumn("Status").AddColumn(Markup.Escape(left)).AddColumn(Markup.Escape(right));
            foreach (var item in comparison.Items)
            {
                table.AddRow(Markup.Escape(item.Key), Status(item.Kind, left, right), Show(item.Left), Show(item.Right));
            }

            console.Write(table);
        }

        if (sameCount > 0 && !comparison.Items.Any(i => i.Kind == ComparisonKind.Same))
        {
            console.MarkupLine($"{sameCount} equal keys hidden (use --same).");
        }

        console.MarkupLine($"Ignored keys: {comparison.IgnoredCount}");
    }

    public static void RenderMissing(IAnsiConsole console, IReadOnlyList<string> environments, IReadOnlyList<MissingKey> missing)
    {
        console.MarkupLine($"Checking {Markup.Escape(string.Join(", ", environments))}");
        if (missing.Count == 0)
        {
            console.MarkupLine("No missing keys.");
            return;
        }

        var table = new Table().AddColumn("Key").AddColumn("Present in").AddColumn("Missing in");
        missing.ToList().ForEach(m => table.AddRow(Markup.Escape(m.Key), Markup.Escape(string.Join(", ", m.PresentIn)), Markup.Escape(string.Join(", ", m.MissingIn))));
        console.Write(table);
        console.MarkupLine($"[yellow]{missing.Count} keys are missing from some environments.[/]");
    }

    private static string Status(ComparisonKind kind, string left, string right) => kind switch
    {
        ComparisonKind.Same => "same",
        ComparisonKind.Different => "different",
        ComparisonKind.OnlyLeft => $"only in {Markup.Escape(left)}",
        _ => $"only in {Markup.Escape(right)}",
    };

    private static string Show(ComparisonValue? value) => value switch
    {
        null => "-",
        { IsSecret: true } => "(secret)",
        _ => Markup.Escape(value.Plain ?? string.Empty),
    };
}
