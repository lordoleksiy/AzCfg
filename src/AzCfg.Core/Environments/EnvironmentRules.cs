using AzCfg.Core.Model;

namespace AzCfg.Core.Environments;

/// <summary>Pure environment rules shared by every client (G18, G30).</summary>
public static class EnvironmentRules
{
    /// <summary><see cref="ProjectBinding.ActiveEnvironment"/> equals the environment name, ignoring case.</summary>
    public static bool IsActive(ProjectBinding binding, ConfigEnvironment env) =>
        string.Equals(binding.ActiveEnvironment, env.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The active environment (when still listed), else <c>dev</c>, else the first, else <c>null</c>.</summary>
    public static ConfigEnvironment? SelectDefault(IReadOnlyList<ConfigEnvironment> envs, ProjectBinding? binding) =>
        (binding?.ActiveEnvironment is { } active ? Find(envs, active) : null)
        ?? Find(envs, "dev")
        ?? envs.FirstOrDefault();

    /// <summary>Protected → Production; dev|development → Development; stage|stg|staging|test|qa|uat → Staging; else Custom.</summary>
    public static EnvironmentKind KindOf(ConfigEnvironment env) =>
        env.IsProtected ? EnvironmentKind.Production
        : env.Name.ToLowerInvariant() switch
        {
            "dev" or "development" => EnvironmentKind.Development,
            "stage" or "stg" or "staging" or "test" or "qa" or "uat" => EnvironmentKind.Staging,
            _ => EnvironmentKind.Custom,
        };

    private static ConfigEnvironment? Find(IReadOnlyList<ConfigEnvironment> envs, string name) =>
        envs.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
