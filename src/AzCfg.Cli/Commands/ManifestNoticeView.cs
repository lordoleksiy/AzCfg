using AzCfg.Core.UseCases;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Human notice when the shared <c>azcfg.json</c> switched the project to another service during resolution (ADR 0012 amendment 2).
/// <c>--json</c> prints nothing: the result is visible in <c>status --json</c> (<c>service</c>, <c>activeEnvironment</c> = null) and
/// <c>config list --json</c>.
/// </summary>
public static class ManifestNoticeView
{
    public static void Render(IAnsiConsole console, ProjectResolution resolution, bool json)
    {
        if (json || resolution.PreviousService is not { } previous || resolution.Existing is not { } binding)
        {
            return;
        }

        console.MarkupLineInterpolated(
            $"[yellow]{resolution.ManifestPath} binds this project to service '{binding.Service}' (was '{previous}'): the active environment was cleared.[/]");
    }
}
