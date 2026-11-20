using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

public class PullSettings : GlobalSettings
{
    [CommandOption("--force")]
    [Description("Overwrite local changes that have not been pushed.")]
    public bool Force { get; init; }

    [CommandOption("--service <NAME>")]
    [Description("Bind a not-yet-bound project to this service without detecting it. A bound project keeps its service (change it with: config set project.service).")]
    public string? Service { get; init; }
}
