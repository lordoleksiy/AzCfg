using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Settings of <c>push</c>: the push options plus the push mode for local files (ADR 0008).</summary>
public sealed class PushFilesSettings : PushSettings
{
    [CommandOption("--delete")]
    [Description("Also delete remote keys that were deleted locally (default: add and update only).")]
    public bool Delete { get; init; }
}
