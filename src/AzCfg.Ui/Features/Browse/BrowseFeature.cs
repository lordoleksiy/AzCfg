using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Features.Browse;

public static class BrowseFeature
{
    /// <summary>Browse mode of the project tab (§2.5), its editor (drafts, local writes) and the <c>browse.*</c> commands.</summary>
    public static IServiceCollection AddBrowseFeature(this IServiceCollection services)
    {
        services.TryAddScoped<ProjectCommandTargets>();
        services.TryAddScoped<ActiveProjectSession>();
        return services
            .AddScoped<BrowseEditor>()
            .AddProjectMode(ProjectMode.Browse, typeof(BrowseView))
            .AddCommands<BrowseCommands>();
    }
}
