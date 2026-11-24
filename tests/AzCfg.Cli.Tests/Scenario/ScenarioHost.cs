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
/// the fakes, so consecutive <see cref="RunAsync"/> calls form one user session. Every command gets <c>--project <see cref="ProjectPath"/></c>;
/// exceptions propagate.
/// </summary>
public sealed class ScenarioHost
{
    public ScenarioHost()
        : this(null)
    {
    }

    /// <summary>
    /// A second developer on the same repository and Azure: shares <see cref="AppConfig"/>, <see cref="Secrets"/>,
    /// <see cref="Discovery"/> and the repository's <see cref="Manifests"/> with <paramref name="teammate"/>, but has their own
    /// user state and local files.
    /// </summary>
    public ScenarioHost(ScenarioHost? teammate)
    {
        AppConfig = teammate?.AppConfig ?? new();
        Secrets = teammate?.Secrets ?? new();
        Discovery = teammate?.Discovery ?? new();
        Manifests = teammate?.Manifests ?? new();
        Repo = Path.Combine(Path.GetTempPath(), "azcfg-scenario", "repo");
        CsprojPath = Path.Combine(Repo, "Orders", "Orders.csproj");
        ProjectPath = Repo;
        Detector = new FixedProjectDetector(new ProjectInfo(Repo, CsprojPath, "Orders", false, "orders-user-secrets"));
    }

    /// <summary>A temp-like absolute path; never touched on disk.</summary>
    public string Repo { get; }

    public string CsprojPath { get; }

    public InMemoryAppConfig AppConfig { get; }

    public InMemorySecretStore Secrets { get; }

    public InMemoryUserStateStore State { get; } = new();

    public InMemoryLocalTarget Files { get; } = new();

    public FixedResourceDiscovery Discovery { get; }

    /// <summary>The repository's <c>azcfg.json</c> files (in memory, serialized like on disk).</summary>
    public InMemoryProjectManifestStore Manifests { get; }

    /// <summary>Project detection; replace it to model another folder of a monorepo.</summary>
    public FixedProjectDetector Detector { get; set; }

    /// <summary>The <c>--project</c> path every command gets (default <see cref="Repo"/>).</summary>
    public string ProjectPath { get; set; }

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
            .AddSingleton<IProjectManifestStore>(Manifests)
            .AddSingleton<IHealthService, HealthService>();

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            CliApp.Configure(c);
            c.PropagateExceptions();
            c.ConfigureConsole(console);
        });

        var exit = await app.RunAsync([.. args, "--project", ProjectPath]);
        return (exit, writer.ToString());
    }
}
