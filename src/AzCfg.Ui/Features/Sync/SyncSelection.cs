using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Ui.Application.State;

namespace AzCfg.Ui.Features.Sync;

public static class SyncSelection
{
    /// <summary>
    /// The environment a project tab works on: the session's environment when it is in the profile, else
    /// <see cref="EnvironmentRules.SelectDefault"/>; null without environments.
    /// </summary>
    public static ConfigEnvironment? EnvironmentOf(WorkspaceState workspace, ProjectSession session, ProjectBinding? binding)
    {
        var envs = workspace.Environments?.Environments ?? [];
        return envs.FirstOrDefault(e => string.Equals(e.Name, session.EnvironmentName, StringComparison.OrdinalIgnoreCase))
            ?? EnvironmentRules.SelectDefault(envs, binding);
    }
}
