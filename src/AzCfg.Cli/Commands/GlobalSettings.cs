using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Base settings with the global options shared by every command.</summary>
public class GlobalSettings : CommandSettings
{
    [CommandOption("--env <ENV>")]
    [Description("Target environment (defaults to the project's active one).")]
    public string? Env { get; init; }

    [CommandOption("--json")]
    [Description("Write the result as JSON to stdout, without prompts or decoration.")]
    public bool Json { get; init; }

    [CommandOption("--yes")]
    [Description("Skip confirmation prompts.")]
    public bool Yes { get; init; }

    [CommandOption("--project <PATH>")]
    [Description("Project folder (defaults to the current directory).")]
    public string Project { get; init; } = Directory.GetCurrentDirectory();
}
