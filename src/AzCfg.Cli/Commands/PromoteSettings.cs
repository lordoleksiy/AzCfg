using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

public sealed class PromoteSettings : PushSettings
{
    [CommandArgument(0, "<FROM>")]
    [Description("Environment to take new keys from.")]
    public string From { get; init; } = "";

    [CommandArgument(1, "<TO>")]
    [Description("Environment to add the keys to.")]
    public string To { get; init; } = "";

    [CommandOption("--in <FILE>")]
    [Description("Plan and apply this edited promote JSON instead of opening the editor.")]
    public string? In { get; init; }
}
