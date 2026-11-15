using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

public class PullSettings : GlobalSettings
{
    [CommandOption("--force")]
    [Description("Overwrite local changes that have not been pushed.")]
    public bool Force { get; init; }
}
