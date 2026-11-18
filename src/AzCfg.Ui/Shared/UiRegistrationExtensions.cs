using AzCfg.Ui.Application.State;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Shared;

/// <summary>
/// Feature modules register their tabs, modes, sidebar sections, status items and commands here (AD-10, G23).
/// A duplicate tab kind, project mode or sidebar id → <see cref="InvalidOperationException"/> at registration.
/// </summary>
public static class UiRegistrationExtensions
{
    public static IServiceCollection AddTab(this IServiceCollection services, TabKind kind, Type component) =>
        services.AddUnique(new TabRegistration(kind, component), r => r.Kind == kind, $"tab kind '{kind}'");

    public static IServiceCollection AddProjectMode(this IServiceCollection services, ProjectMode mode, Type component) =>
        services.AddUnique(new ProjectModeRegistration(mode, component), r => r.Mode == mode, $"project mode '{mode}'");

    public static IServiceCollection AddSidebarSection(this IServiceCollection services, string id, string title, Type component, int order) =>
        services.AddUnique(new SidebarSectionRegistration(id, title, component, order), r => r.Id == id, $"sidebar section '{id}'");

    public static IServiceCollection AddStatusBarItem(this IServiceCollection services, Type component, StatusBarAlignment alignment, int order) =>
        services.AddSingleton(new StatusBarItemRegistration(component, alignment, order));

    /// <summary>Registers a scoped <see cref="ICommandContributor"/>.</summary>
    public static IServiceCollection AddCommands<TContributor>(this IServiceCollection services)
        where TContributor : class, ICommandContributor =>
        services.AddScoped<ICommandContributor, TContributor>();

    private static IServiceCollection AddUnique<T>(this IServiceCollection services, T registration, Func<T, bool> sameKey, string name)
        where T : class
    {
        if (services.Any(d => d.ServiceType == typeof(T) && d.ImplementationInstance is T existing && sameKey(existing)))
        {
            throw new InvalidOperationException($"Duplicate registration of {name}.");
        }

        return services.AddSingleton(registration);
    }
}
