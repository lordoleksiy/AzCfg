using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Features.Problems;

public static class ProblemsFeature
{
    /// <summary>The PROBLEMS sidebar section, the errors/warnings item of the status bar and the Refresh command (FR-H1).</summary>
    public static IServiceCollection AddProblemsFeature(this IServiceCollection services) =>
        services
            .AddScoped<ProblemsState>()
            .AddScoped<ProblemActions>()
            .AddSidebarSection("problems", "Problems", typeof(ProblemsSection), 40)
            .AddStatusBarItem(typeof(ProblemsStatusItem), StatusBarAlignment.Left, 20)
            .AddCommands<ProblemsCommands>();
}
