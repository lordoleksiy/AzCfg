using AzCfg.Ui.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Demo;

/// <summary>Sample workspace and in-memory Azure for <c>--demo</c> (AD-12). Filled by DH.</summary>
public static class DemoServiceCollectionExtensions
{
    public static IServiceCollection AddAzCfgDemo(this IServiceCollection services, UiHostOptions options) => services;
}
