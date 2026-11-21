using System.ComponentModel;
using AzCfg.Ui.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Options of <c>azcfg ui</c> (ADR 0016): the UI host options plus the global <c>--json</c>.</summary>
public sealed class UiSettings : GlobalSettings
{
    [CommandOption("--port <PORT>")]
    [Description("Loopback port to listen on (default: a free port chosen by the OS).")]
    public int Port { get; init; }

    [CommandOption("--no-browser")]
    [Description("Only print the login URL; do not open the browser.")]
    public bool NoBrowser { get; init; }

    [CommandOption("--demo [MODE]")]
    [Description("Run on an in-memory demo world instead of Azure: 'full' (default) or 'empty'.")]
    public FlagValue<string>? Demo { get; init; }

    /// <summary>The demo variant, or null when <c>--demo</c> is not given; a bare <c>--demo</c> means <c>full</c>.</summary>
    public string? DemoMode => Demo is { IsSet: true } ? Demo.Value ?? "full" : null;

    public override ValidationResult Validate()
    {
        if (Port is < 0 or > 65535)
            return ValidationResult.Error("--port must be between 0 and 65535.");
        if (DemoMode is not (null or "full" or "empty"))
            return ValidationResult.Error("--demo must be 'full' or 'empty'.");
        return ValidationResult.Success();
    }

    public UiHostOptions ToHostOptions() => new() { Port = Port, NoBrowser = NoBrowser, Json = Json, Demo = DemoMode };
}
