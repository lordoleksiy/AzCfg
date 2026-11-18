using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Environments;

/// <summary>Pure environment rules shared with the CLI's behaviour (G18, G30).</summary>
public static class EnvironmentRules
{
    /// <summary><see cref="ProjectBinding.ActiveEnvironment"/> equals the environment name, ignoring case.</summary>
    public static bool IsActive(ProjectBinding binding, ConfigEnvironment env) =>
        string.Equals(binding.ActiveEnvironment, env.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The active environment (when still listed), else <c>dev</c>, else the first, else <c>null</c>.</summary>
    public static ConfigEnvironment? SelectDefault(IReadOnlyList<ConfigEnvironment> envs, ProjectBinding? binding) =>
        (binding?.ActiveEnvironment is { } active ? envs.FirstOrDefault(e => e.Name.Equals(active, StringComparison.OrdinalIgnoreCase)) : null)
        ?? envs.FirstOrDefault(e => e.Name.Equals("dev", StringComparison.OrdinalIgnoreCase))
        ?? envs.FirstOrDefault();

    /// <summary>Protected → Production; dev|development → Development; stage|stg|staging|test|qa|uat → Staging; else Custom.</summary>
    public static EnvironmentKind KindOf(ConfigEnvironment env) => env switch
    {
        { IsProtected: true } => EnvironmentKind.Production,
        _ when env.Name.ToLowerInvariant() is "dev" or "development" => EnvironmentKind.Development,
        _ when env.Name.ToLowerInvariant() is "stage" or "stg" or "staging" or "test" or "qa" or "uat" => EnvironmentKind.Staging,
        _ => EnvironmentKind.Custom,
    };
}
