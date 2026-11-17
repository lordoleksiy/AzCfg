using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Options of <c>azcfg config get &lt;KEY&gt;</c>; the base of <see cref="ConfigSetSettings"/>.</summary>
public class ConfigGetSettings : GlobalSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("Setting key: mode, defaultLabel, env.<name>.endpoint, env.<name>.label or env.<name>.protected.")]
    public string Key { get; init; } = "";
}
