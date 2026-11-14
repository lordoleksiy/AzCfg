using AzCfg.Core.Abstractions;
using AzCfg.Core.UseCases;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Pulls the given environment and makes it the project's active one; a blocked pull leaves the active environment unchanged.</summary>
public sealed class UseCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
    : PullCommandBase<UseSettings>(projects, sync, state, resolver, console)
{
    protected override string? RequestedEnv(UseSettings settings) => settings.Name;
}
