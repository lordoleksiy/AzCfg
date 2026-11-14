using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Resolves the project binding and target environment (<c>--env</c>, else the binding's active one, else <c>dev</c>).
/// Throws <see cref="InvalidOperationException"/> without a profile, without a binding (unless overridden) or for an environment outside the profile.
/// </summary>
public abstract class EnvironmentCommandBase<TSettings>(IProjectService projects, IUserStateStore state, IEnvironmentResolver resolver)
    : AsyncCommand<TSettings> where TSettings : GlobalSettings
{
    protected IProjectService Projects { get; } = projects;

    protected IUserStateStore State { get; } = state;

    protected virtual string? RequestedEnv(TSettings settings) => settings.Env;

    protected virtual Task<ProjectBinding> GetBindingAsync(string repo, ProjectResolution resolution, TSettings settings, CancellationToken ct) =>
        Task.FromResult(resolution.Existing ?? throw new InvalidOperationException("This project is not bound yet. Run 'azcfg pull' first."));

    protected async Task<(ProjectBinding Binding, ConfigEnvironment Env)> ResolveAsync(TSettings settings, CancellationToken ct)
    {
        var profile = await State.LoadProfileAsync(ct) ?? throw new InvalidOperationException("No profile found. Run 'azcfg init' first.");
        var repo = Path.GetFullPath(settings.Project);
        var binding = await GetBindingAsync(repo, await Projects.ResolveAsync(repo, ct), settings, ct);
        var name = RequestedEnv(settings) ?? binding.ActiveEnvironment ?? "dev";
        var env = resolver.Resolve(profile).FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Environment '{name}' is not in the profile.");
        return (binding, env);
    }
}
