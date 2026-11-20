using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Application.Workspace;

public static class WorkspaceServices
{
    public static IServiceCollection AddWorkspaceServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddSingleton<IWorkspaceRegistry>(sp => new WorkspaceRegistry(sp.GetRequiredService<UiStatePaths>().Root, sp.GetRequiredService<TimeProvider>()))
            .AddSingleton<BindingLocator>()
            .AddSingleton<IWorkspaceService, WorkspaceService>()
            .AddSingleton<IDirectoryBrowser, DirectoryBrowser>();
    }
}
