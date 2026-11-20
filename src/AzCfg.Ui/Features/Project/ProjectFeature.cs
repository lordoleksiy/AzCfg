using AzCfg.Ui.Application.State;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Features.Project;

public static class ProjectFeature
{
    /// <summary>The project tab (§2.4) with its header, protection and draft flows and the <c>project.*</c> / <c>draft.*</c> commands.</summary>
    public static IServiceCollection AddProjectFeature(this IServiceCollection services) =>
        services
            .AddScoped<ProtectionFlow>()
            .AddScoped<DraftFlow>()
            .AddScoped<ActiveProjectSession>()
            .AddScoped<ProjectCommandTargets>()
            .AddTab(TabKind.Project, typeof(ProjectTab))
            .AddCommands<ProjectCommands>();
}
