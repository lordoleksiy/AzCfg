using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Features.Workspace;

public static class WorkspaceFeature
{
    /// <summary>The WORKSPACE sidebar section, Open folder, the bind wizard (<see cref="IBindingFlow"/>) and the workspace commands.</summary>
    public static IServiceCollection AddWorkspaceFeature(this IServiceCollection services) =>
        services
            .AddScoped<WorkspaceActions>()
            .AddScoped<IBindingFlow, BindingFlow>()
            .AddSidebarSection("workspace", "Workspace", typeof(WorkspaceSection), 10)
            .AddCommands<WorkspaceCommands>();
}
