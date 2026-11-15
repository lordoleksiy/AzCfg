using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

public sealed class UseSettings : PullSettings
{
    [CommandArgument(0, "<env>")]
    [Description("Environment to pull and make active.")]
    public string Name { get; init; } = "";
}
