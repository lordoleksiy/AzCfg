using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

public class PushSettings : GlobalSettings
{
    [CommandOption("--confirm <ENV>")]
    [Description("Environment name confirming a push to a protected environment without prompting.")]
    public string? Confirm { get; init; }
}
