using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Settings of <c>plan</c>: the global options plus the push mode for local files (ADR 0008).</summary>
public sealed class PlanSettings : GlobalSettings
{
    [CommandOption("--delete")]
    [Description("Also delete remote keys that were deleted locally (default: add and update only).")]
    public bool Delete { get; init; }

    [CommandOption("--key <PATTERN>")]
    [Description("Plan only the keys matching the pattern (repeatable; * wildcard, optional global: prefix).")]
    public string[] Key { get; init; } = [];
}
