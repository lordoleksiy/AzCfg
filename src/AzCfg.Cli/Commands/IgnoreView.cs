using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Prints ignore patterns: one per line (or <c>No ignored keys.</c>), or <see cref="IgnoreList"/> as JSON with <c>--json</c>.</summary>
public static class IgnoreView
{
    public static void Render(IAnsiConsole console, bool json, IReadOnlyList<string>? ignore)
    {
        ignore ??= [];
        if (json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(new IgnoreList(ignore)));
        }
        else if (ignore.Count == 0)
        {
            console.WriteLine("No ignored keys.");
        }
        else
        {
            foreach (var pattern in ignore)
            {
                console.WriteLine(pattern);
            }
        }
    }
}
