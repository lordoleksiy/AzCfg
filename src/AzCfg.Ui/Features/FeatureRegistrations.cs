using AzCfg.Ui.Features.Browse;
using AzCfg.Ui.Features.Compare;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Features.Sync;
using AzCfg.Ui.Features.Workspace;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Features;

public static class FeatureRegistrations
{
    /// <summary>
    /// Registers every feature module, then the "not available yet" defaults of the cross-feature flows (<c>TryAdd</c>, so a
    /// feature's own registration wins).
    /// </summary>
    public static IServiceCollection AddAzCfgUi(this IServiceCollection services)
    {
        services
            .AddPlatformFeature()
            .AddWorkspaceFeature()
            .AddEnvironmentsFeature()
            .AddProjectFeature()
            .AddBrowseFeature()
            .AddCompareFeature()
            .AddSyncFeature();
        services.TryAddScoped<IActivationFlow, NotAvailableActivationFlow>();
        services.TryAddScoped<IBindingFlow, NotAvailableBindingFlow>();
        return services;
    }
}
