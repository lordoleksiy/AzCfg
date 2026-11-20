using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Settings of <c>status</c>: the global options (<c>--env</c> picks another environment than the active one) and <c>--check</c>.</summary>
public sealed class StatusSettings : GlobalSettings
{
    [CommandOption("--check")]
    [Description("Check configuration health instead (sign-in, environment access, local targets, Key Vault references, missing keys); ignores --env, exits 1 on errors.")]
    public bool Check { get; init; }
}
