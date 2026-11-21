using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Infrastructure.Local;
using AzCfg.Infrastructure.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Demo;
using AzCfg.Ui.Hosting;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Demo;

public sealed class DemoSeedTests
{
    [Fact]
    public async Task Full_DiffDev_ShowsTwoLocalOneRemoteOneConflict()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        var sync = demo.Services.GetRequiredService<ISyncService>();
        var diff = await sync.DiffAsync(await demo.BindingAsync(), await demo.EnvironmentAsync("dev"), CancellationToken.None);
        var counts = diff.Items.Where(i => i.Kind != DiffKind.Unchanged).GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(
            new Dictionary<DiffKind, int> { [DiffKind.LocalAdded] = 1, [DiffKind.LocalModified] = 1, [DiffKind.RemoteModified] = 1, [DiffKind.Conflict] = 1 },
            counts);
    }

    [Fact]
    public async Task Full_ReadStage_WarnsAboutDuplicateLabels()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        var sync = demo.Services.GetRequiredService<ISyncService>();
        var remote = await sync.ReadRemoteConfigAsync(await demo.BindingAsync(), await demo.EnvironmentAsync("stage"), CancellationToken.None);
        var warning = Assert.Single(remote.Warnings, w => w.Kind == SyncWarningKind.DuplicateLabels);
        Assert.Equal("Timeout", warning.Key);
    }

    [Fact]
    public async Task Full_Profile_HasThreeEnvironmentsAndProtectedProd()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        var profile = (await demo.World.State.LoadProfileAsync(CancellationToken.None))!;
        Assert.Equal(
            [("dev", "https://appcs-shop-dev.azconfig.io/", false), ("stage", "https://appcs-shop-stage.azconfig.io/", false), ("prod", "https://appcs-shop-prod.azconfig.io/", true)],
            profile.Environments.Select(e => (e.Name, e.StoreEndpoint.ToString(), e.IsProtected)));
        Assert.Equal(EnvironmentMode.StorePerEnvironment, profile.Mode);
        Assert.Equal("dev", (await demo.BindingAsync()).ActiveEnvironment);
    }

    [Theory]
    [InlineData("dev", 41, 6)]
    [InlineData("stage", 41, 6)]
    [InlineData("prod", 41, 7)]
    public async Task Full_EachEnvironment_HasServiceGlobalBillingKeysAndSecrets(string env, int orders, int secretRefs)
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        var settings = demo.World.AppConfig.Settings($"https://appcs-shop-{env}.azconfig.io");
        Assert.Equal(orders, settings.Count(s => s.Key.StartsWith("orders:", StringComparison.Ordinal)));
        Assert.Equal(10, settings.Count(s => s.Key.StartsWith("global:", StringComparison.Ordinal)));
        Assert.Equal(8, settings.Count(s => s.Key.StartsWith("billing:", StringComparison.Ordinal)));
        Assert.Equal(secretRefs, settings.Count(s => s.Text is null));
    }

    [Fact]
    public async Task Full_StageAndProd_DifferInExactlyFivePlainKeys()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        Dictionary<string, string> Plain(string env) => demo.World.AppConfig.Settings($"https://appcs-shop-{env}.azconfig.io")
            .Where(s => s.Label is null && s.Text is not null)
            .ToDictionary(s => s.Key, s => s.Text!);
        var (stage, prod) = (Plain("stage"), Plain("prod"));
        Assert.Equal(stage.Keys.Order(), prod.Keys.Order());
        Assert.Equal(5, stage.Count(p => prod[p.Key] != p.Value));
    }

    [Fact]
    public async Task Full_RegistersWorkspaceOnce_AndSeedIsIdempotent()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        await demo.Services.GetRequiredService<DemoSeed>().RunAsync(CancellationToken.None);
        Assert.Equal([demo.Location.Workspace], demo.Registry.Touched);
        Assert.EndsWith("Shop", demo.Location.Workspace);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "azcfg-demo"), Path.GetDirectoryName(demo.Location.Root));
    }

    [Fact]
    public async Task Full_WritesSampleSolutionAndProjects()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        var shop = demo.Location.Workspace;
        Assert.True(File.Exists(Path.Combine(shop, "Shop.slnx")));
        Assert.True(File.Exists(Path.Combine(demo.OrdersApiFolder, "appsettings.json")));
        var projects = await new CsprojDetector().DetectAsync(shop, CancellationToken.None);
        Assert.Equal(
            [("Billing.Worker", false), ("Orders.Api", false), ("Orders.Tests", true), ("Shop.Contracts", true)],
            projects.Select(p => (p.Name, p.IsLibrary)).Order());
        Assert.NotNull(projects.Single(p => p.Name == "Orders.Api").UserSecretsId);
        Assert.Contains("tests", projects.Single(p => p.Name == "Orders.Tests").CsprojPath);
        Assert.DoesNotContain("Orders.Tests", await File.ReadAllTextAsync(Path.Combine(shop, "Shop.slnx")));
    }

    [Fact]
    public async Task Full_PullWritesValuesToSampleFolderAndSecretsOnlyToMemory()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        var local = await File.ReadAllTextAsync(Path.Combine(demo.OrdersApiFolder, "appsettings.local.json"));
        Assert.Contains("PageSize", local);
        var secrets = demo.World.UserSecrets.Values();
        Assert.Equal(6, secrets.Count);
        Assert.All(secrets.Values, v => Assert.StartsWith("demo-", v, StringComparison.Ordinal));
        foreach (var file in Directory.EnumerateFiles(demo.Location.Root, "*", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(file);
            Assert.All(secrets.Values, v => Assert.DoesNotContain(v, text));
        }
    }

    [Fact]
    public async Task Empty_NothingSaved_ButAzureAndSampleFilesExist()
    {
        await using var demo = await DemoApp.StartAsync("--demo=empty");
        Assert.Null(await demo.World.State.LoadProfileAsync(CancellationToken.None));
        Assert.Null(demo.World.State.Binding(demo.OrdersApiFolder));
        Assert.Empty(demo.Registry.Touched);
        Assert.True(File.Exists(Path.Combine(demo.Location.Workspace, "Shop.slnx")));
        Assert.False(File.Exists(Path.Combine(demo.OrdersApiFolder, "appsettings.local.json")));
        Assert.Equal(3, (await demo.World.Discovery.FindAppConfigStoresAsync(CancellationToken.None)).Count);
        Assert.Equal(3, (await demo.World.Discovery.FindKeyVaultsAsync(CancellationToken.None)).Count);
        Assert.NotEmpty(demo.World.AppConfig.Settings("https://appcs-shop-dev.azconfig.io"));
    }

    [Fact]
    public async Task Demo_UsesInMemoryAdaptersAndKeepsStateUnderDemoFolder()
    {
        await using var demo = await DemoApp.StartAsync("--demo");
        var targets = demo.Services.GetServices<ILocalTarget>().ToList();
        Assert.Same(demo.World.AppConfig, demo.Services.GetRequiredService<IConfigStore>());
        Assert.Same(demo.World.Secrets, demo.Services.GetRequiredService<ISecretStore>());
        Assert.Same(demo.World.State, demo.Services.GetRequiredService<IUserStateStore>());
        Assert.Same(demo.World.UserSecrets, targets.Single(t => t.Kind == LocalTargetKind.UserSecrets));
        Assert.IsType<AppSettingsLocalTarget>(targets.Single(t => t.Kind == LocalTargetKind.AppSettingsLocal));
        var stateRoot = demo.Services.GetRequiredService<UiStatePaths>().Root;
        Assert.Equal(Path.Combine(demo.Location.Root, "state"), stateRoot);
        Assert.Equal(stateRoot, demo.Services.GetRequiredService<UiHostOptions>().StateRoot);
    }

    [Fact]
    public async Task Demo_DoesNotTouchRealUserStateOrRealUserSecrets()
    {
        var before = Snapshot(JsonUserStateStore.DefaultRoot);
        var secretsDirectory = Path.GetDirectoryName(PathHelper.GetSecretsPathFromSecretsId(SampleWorkspace.OrdersApiUserSecretsId))!;
        Assert.False(Directory.Exists(secretsDirectory));
        await using (var demo = await DemoApp.StartAsync("--demo"))
        {
            Assert.False(Directory.Exists(secretsDirectory));
        }

        Assert.False(Directory.Exists(secretsDirectory));
        Assert.Equal(before, Snapshot(JsonUserStateStore.DefaultRoot));
    }

    [Fact]
    public async Task Demo_ExplicitStateRoot_IsKept()
    {
        var explicitRoot = Path.Combine(Path.GetTempPath(), "azcfg-ui-tests", "explicit-state");
        await using var demo = await DemoApp.StartAsync("--demo", "--state-root", explicitRoot);
        Assert.Equal(explicitRoot, demo.Services.GetRequiredService<UiStatePaths>().Root);
    }

    [Fact]
    public async Task Demo_TwoAppsStartedTogether_GetDistinctFolders()
    {
        await using var first = await DemoApp.StartAsync("--demo=empty");
        await using var second = await DemoApp.StartAsync("--demo=empty");
        Assert.NotEqual(first.Location.Root, second.Location.Root);
    }

    [Fact]
    public async Task NoDemo_RegistersNothingOfTheDemo()
    {
        await using var app = AzCfgUiApp.Build(["--state-root", Path.Combine(Path.GetTempPath(), "azcfg-ui-tests")]);
        Assert.Null(app.Services.GetService<DemoSeed>());
        Assert.IsNotType<InMemoryAppConfig>(app.Services.GetRequiredService<IConfigStore>());
    }

    [Fact]
    public void AddAzCfgDemo_UnknownVariant_Throws() =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAzCfgDemo(new UiHostOptions { Demo = "bogus" }));

    private static List<string> Snapshot(string root) =>
        Directory.Exists(root)
            ? [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().Select(p => $"{p}|{File.GetLastWriteTimeUtc(p).Ticks}")]
            : [];
}
