using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Options of <c>azcfg config get &lt;KEY&gt;</c>; the base of <see cref="ConfigSetSettings"/>.</summary>
public class ConfigGetSettings : GlobalSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("Setting key: mode, defaultLabel, gitignoreLocalFiles, env.<name>.endpoint, env.<name>.label, env.<name>.protected, or a project key (project.service, project.values, project.secrets, project.vault.<env>, project.active, project.ignore).")]
    public string Key { get; init; } = "";
}
