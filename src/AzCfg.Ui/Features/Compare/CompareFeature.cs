using AzCfg.Ui.Application.State;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Features.Compare;

public static class CompareFeature
{
    /// <summary>Compare mode of the project tab (§2.6).</summary>
    public static IServiceCollection AddCompareFeature(this IServiceCollection services) =>
        services.AddProjectMode(ProjectMode.Compare, typeof(CompareView));
}
