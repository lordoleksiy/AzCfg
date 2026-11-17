using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

public class PullSettings : GlobalSettings
{
    [CommandOption("--force")]
    [Description("Overwrite local changes that have not been pushed.")]
    public bool Force { get; init; }

    [CommandOption("--service <NAME>")]
    [Description("Bind a not-yet-bound project to this service without detecting it.")]
    public string? Service { get; init; }
}
