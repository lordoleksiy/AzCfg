using AzCfg.Core.Abstractions;
using AzCfg.Core.UseCases;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Pulls <c>--env</c>, else the active environment, else <c>dev</c>.</summary>
public sealed class PullCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
    : PullCommandBase<PullSettings>(projects, sync, state, resolver, console);
