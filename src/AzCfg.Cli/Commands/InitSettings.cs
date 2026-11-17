using System.ComponentModel;
using AzCfg.Core.Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Options of <c>azcfg init</c>: how environments are separated (ADR 0008), the shared store for labels mode and the label for new keys.</summary>
public sealed class InitSettings : GlobalSettings
{
    [CommandOption("--mode <MODE>")]
    [Description("How environments are separated: 'stores' (one store per environment) or 'labels' (one store, a label per environment).")]
    public string? Mode { get; init; }

    [CommandOption("--store <ENDPOINT>")]
    [Description("Shared store endpoint for '--mode labels' (required with --yes).")]
    public string? Store { get; init; }

    [CommandOption("--label <LABEL>")]
    [Description("Label for new keys in 'stores' mode (saved as defaultLabel).")]
    public string? Label { get; init; }

    public override ValidationResult Validate()
    {
        if (Mode is not null && !EnvironmentModeNames.TryParse(Mode, out _))
            return ValidationResult.Error($"--mode must be '{EnvironmentModeNames.Stores}' or '{EnvironmentModeNames.Labels}'.");

        return Store is not null && !InitCommand.IsHttpEndpoint(Store)
            ? ValidationResult.Error("--store must be an absolute http(s) URL, e.g. https://<name>.azconfig.io.")
            : ValidationResult.Success();
    }
}
