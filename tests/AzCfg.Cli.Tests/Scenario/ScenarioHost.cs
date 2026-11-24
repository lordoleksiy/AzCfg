using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Health;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure.Azure;
using AzCfg.Infrastructure.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// Runs <c>azcfg</c> commands through the production command tree (<see cref="CliApp.Configure"/>) and the real Core services,
/// wired like <c>AddAzCfg()</c> but with in-memory edges: App Configuration, Key Vault, user state, the local user-secrets file,
/// project detection (one project <c>Orders</c> at <c>&lt;repo&gt;/Orders/Orders.csproj</c>) and resource discovery. State lives in
/// the fakes, so consecutive <see cref="RunAsync"/> calls form one user session. Every command gets <c>--project &lt;repo&gt;</c>;
/// exceptions propagate.
/// </summary>
public sealed class ScenarioHost
{
    public ScenarioHost()
    {
        Repo = Path.Combine(Path.GetTempPath(), "azcfg-scenario", "repo");
        CsprojPath = Path.Combine(Repo, "Orders", "Orders.csproj");
        Detector = new FixedProjectDetector(new ProjectInfo(Repo, CsprojPath, "Orders", false, "orders-user-secrets"));
    }

    /// <summary>A temp-like absolute path; never touched on disk.</summary>
    public string Repo { get; }

    public string CsprojPath { get; }

    public InMemoryAppConfig AppConfig { get; } = new();

    public InMemorySecretStore Secrets { get; } = new();

    public InMemoryUserStateStore State { get; } = new();

    public InMemoryLocalTarget Files { get; } = new();

    public FixedResourceDiscovery Discovery { get; } = new();

    public FixedProjectDetector Detector { get; }

    /// <summary>The signed-in Azure account reported by <c>status --check</c>.</summary>
    public FixedAzureAccountProbe Account { get; } = new();

    public ProjectBinding? Binding => State.Binding(Repo);

    public async Task<(int Exit, string Output)> RunAsync(params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
            Enrichment = new() { UseDefaultEnrichers = false },
        });
        console.Profile.Width = 200;

        var services = new ServiceCollection()
            .AddSingleton<IConfigStore>(AppConfig)
            .AddSingleton<ISecretStore>(Secrets)
            .AddSingleton<IUserStateStore>(State)
            .AddSingleton<ILocalTarget>(Files)
            .AddSingleton<IProjectDetector>(Detector)
            .AddSingleton<IResourceDiscovery>(Discovery)
            .AddSingleton<IEditorLauncher>(new NoOpEditorLauncher())
            .AddSingleton<ISecretFingerprinter>(sp =>
                new HmacSecretFingerprinter(sp.GetRequiredService<IUserStateStore>().GetOrCreateFingerprintKeyAsync(CancellationToken.None).GetAwaiter().GetResult()))
            .AddSingleton<IEnvironmentResolver, ProfileEnvironmentResolver>()
            .AddSingleton<ISetupService, SetupService>()
            .AddSingleton<IUserSettingsService, UserSettingsService>()
            .AddSingleton<IProjectSettingsService, ProjectSettingsService>()
            .AddSingleton<IProjectService, ProjectService>()
            .AddSingleton<IAzureAccountProbe>(Account)
            .AddSingleton<ISyncService, SyncService>()
            .AddSingleton<IAccessErrorClassifier, AzureAccessErrorClassifier>()
            .AddSingleton<ITrackedSettingsReader>(new FixedTrackedSettingsReader())
            .AddSingleton<IProjectManifestStore>(new InMemoryProjectManifestStore())
            .AddSingleton<IHealthService, HealthService>();

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            CliApp.Configure(c);
            c.PropagateExceptions();
            c.ConfigureConsole(console);
        });

        var exit = await app.RunAsync([.. args, "--project", Repo]);
        return (exit, writer.ToString());
    }
}
