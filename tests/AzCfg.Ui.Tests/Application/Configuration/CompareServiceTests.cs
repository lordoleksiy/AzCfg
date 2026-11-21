using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Tests.Application.Configuration;

public sealed class CompareServiceTests
{
    private static readonly CompareSide.Local Local = new();
    private static readonly CompareSide.Environment Dev = new(ConfigurationWorld.Dev);
    private static readonly CompareSide.Environment Stg = new(ConfigurationWorld.Stg);

    private static CompareRow Row(ComparisonView view, string key) => view.Rows.Single(r => r.Key == key);

    private static void AssertNoSentinel(ComparisonView view)
    {
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, JsonSerializer.Serialize(view));
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, view.LeftJson);
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, view.RightJson);
    }

    [Fact]
    public async Task CompareAsync_RemoteRemote_KindsAndSecretsByName()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Same:Key", "v");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Diff:Key", "a");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Left:Only", "l");
        world.SeedRemote(ConfigurationWorld.Stg, "crm:Same:Key", "v");
        world.SeedRemote(ConfigurationWorld.Stg, "crm:Diff:Key", "b");
        world.SeedRemote(ConfigurationWorld.Stg, "crm:Right:Only", "r");
        world.World.AppConfig.SetSecretReference(ConfigurationWorld.Dev.StoreEndpoint.ToString(), "crm:Api:Key", world.World.Secrets.Add("https://kv-dev.vault.azure.net", "crm--Api--Key", ConfigurationWorld.Sentinel));
        world.World.AppConfig.SetSecretReference(ConfigurationWorld.Stg.StoreEndpoint.ToString(), "crm:Api:Key", world.World.Secrets.Add("https://kv-stg.vault.azure.net", "crm--Api--Key", ConfigurationWorld.Sentinel + "2"));

        var view = await world.Compare.CompareAsync(world.Binding, Dev, Stg, default);

        Assert.Equal(CompareResult.Same, Row(view, "Same:Key").Result);
        Assert.Equal(CompareResult.Different, Row(view, "Diff:Key").Result);
        Assert.Equal(CompareResult.OnlyLeft, Row(view, "Left:Only").Result);
        Assert.Equal(CompareResult.OnlyRight, Row(view, "Right:Only").Result);
        var secret = Row(view, "Api:Key");
        Assert.Equal(CompareResult.Same, secret.Result);
        Assert.True(secret.Left!.IsSecret);
        Assert.Equal("crm--Api--Key", secret.Right!.SecretName);
        Assert.Null(secret.Left.Value);
        Assert.Equal(0, view.IgnoredCount);
        Assert.Equal(0, world.SecretStore.Gets);
        AssertNoSentinel(view);
    }

    [Fact]
    public async Task CompareAsync_LocalRemote_SecretsComparedByFingerprint()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemoteSecret(ConfigurationWorld.Dev);
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Other:Secret", "plain");
        world.World.AppConfig.SetSecretReference(ConfigurationWorld.Dev.StoreEndpoint.ToString(), "crm:Db:Pass", world.World.Secrets.Add(ConfigurationWorld.Vault, "crm--Db--Pass", "remote-pass"));
        await world.WriteSecretsAsync(new LocalValue("Api:Key", ConfigurationWorld.Sentinel), new LocalValue("Db:Pass", "different-pass"));

        var view = await world.Compare.CompareAsync(world.Binding, Local, Dev, default);

        Assert.Equal(CompareResult.Same, Row(view, "Api:Key").Result);
        Assert.Equal(CompareResult.Different, Row(view, "Db:Pass").Result);
        Assert.Equal(CompareResult.OnlyRight, Row(view, "Other:Secret").Result);
        var local = Row(view, "Api:Key").Left!;
        Assert.True(local.IsSecret);
        Assert.Null(local.Value);
        Assert.Equal("crm--Api--Key", Row(view, "Api:Key").Right!.SecretName);
        AssertNoSentinel(view);
        Assert.Equal("***", JsonDocument.Parse(view.LeftJson).RootElement.GetProperty("Api").GetProperty("Key").GetString());
        Assert.Equal("crm--Api--Key", JsonDocument.Parse(view.RightJson).RootElement.GetProperty("Api").GetProperty("Key").GetProperty("@kv").GetString());
    }

    [Fact]
    public async Task CompareAsync_LocalRemote_PlainValuesAndOnlySides()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Same:Key", "v");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Diff:Key", "r");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Remote:Only", "x");
        await world.WriteValuesAsync(new LocalValue("Same:Key", "v"), new LocalValue("Diff:Key", "l"), new LocalValue("Local:Only", "y"));

        var view = await world.Compare.CompareAsync(world.Binding, Local, Dev, default);

        Assert.Equal(CompareResult.Same, Row(view, "Same:Key").Result);
        Assert.Equal(CompareResult.Different, Row(view, "Diff:Key").Result);
        Assert.Equal("l", Row(view, "Diff:Key").Left!.Value);
        Assert.Equal("r", Row(view, "Diff:Key").Right!.Value);
        Assert.Equal(CompareResult.OnlyLeft, Row(view, "Local:Only").Result);
        Assert.Equal(CompareResult.OnlyRight, Row(view, "Remote:Only").Result);
    }

    [Fact]
    public async Task CompareAsync_RemoteLocal_ReversesSidesAndResults()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Diff:Key", "r");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Remote:Only", "x");
        await world.WriteValuesAsync(new LocalValue("Diff:Key", "l"), new LocalValue("Local:Only", "y"));

        var view = await world.Compare.CompareAsync(world.Binding, Dev, Local, default);

        Assert.Equal("r", Row(view, "Diff:Key").Left!.Value);
        Assert.Equal("l", Row(view, "Diff:Key").Right!.Value);
        Assert.Equal(CompareResult.OnlyLeft, Row(view, "Remote:Only").Result);
        Assert.Equal(CompareResult.OnlyRight, Row(view, "Local:Only").Result);
        Assert.Contains("\"Remote\"", view.LeftJson);
        Assert.Contains("\"Local\"", view.RightJson);
    }

    [Fact]
    public async Task CompareAsync_LocalLocal_Throws()
    {
        using var world = new ConfigurationWorld();

        await Assert.ThrowsAsync<ArgumentException>(() => world.Compare.CompareAsync(world.Binding, Local, Local, default));
    }

    [Fact]
    public async Task CompareAsync_IgnoredKeys_ExcludedAndCounted()
    {
        using var world = new ConfigurationWorld(ignore: ["Db:Host", "Only:Local"]);
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Host", "h");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Port", "1");
        await world.WriteValuesAsync(new LocalValue("Db:Host", "mine"), new LocalValue("Only:Local", "x"), new LocalValue("Db:Port", "1"));

        var view = await world.Compare.CompareAsync(world.Binding, Local, Dev, default);

        Assert.Equal(["Db:Port"], view.Rows.Select(r => r.Key));
        // Core counts ignored keys of the remote side only (local-only "Only:Local" is not counted).
        Assert.Equal(1, view.IgnoredCount);
        Assert.DoesNotContain("Host", view.LeftJson);
        Assert.DoesNotContain("Only", view.LeftJson);
    }

    [Fact]
    public async Task MissingKeysAsync_ThreeEnvironments_MatchesTheCoreReportAndNeverReadsSecrets()
    {
        using var world = new ConfigurationWorld(ignore: ["Ignored:*"]);
        var prod = new ConfigEnvironment("prod", new Uri("https://prod.azconfig.io"), null, true);
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Same:Key", "v");
        world.SeedRemote(ConfigurationWorld.Stg, "crm:Same:Key", "v");
        world.SeedRemote(prod, "crm:Same:Key", "v");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Only:Dev", "d");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Dev:AndStg", "x");
        world.SeedRemote(ConfigurationWorld.Stg, "crm:Dev:AndStg", "x");
        world.SeedRemote(ConfigurationWorld.Dev, "global:Log:Level", "Info");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Ignored:Key", "i");
        world.SeedRemoteSecret(ConfigurationWorld.Dev);
        ConfigEnvironment[] envs = [ConfigurationWorld.Dev, ConfigurationWorld.Stg, prod];

        var view = await world.Compare.MissingKeysAsync(world.Binding, envs, default);

        Assert.Equal(envs, view.Environments);
        Assert.Equal(["Api:Key", "Dev:AndStg", "Log:Level", "Only:Dev", "Same:Key"], view.Rows.Select(r => r.Key));
        var report = MissingKeysReport.Compute([.. await Task.WhenAll(envs.Select(e => world.Sync.ReadRemoteConfigAsync(world.Binding, e, default)))]);
        Assert.Equal(
            [.. report.Where(m => m.Key != "Ignored:Key").Select(m => m.Key)],
            [.. view.Rows.Where(r => r.HasGap).Select(r => r.Key)]);
        Assert.Equal(4, view.GapCount);
        Assert.Equal(1, view.IgnoredCount);
        Assert.Equal(ConfigScope.Global, Row(view, "Log:Level").Scope);
        Assert.Equal([true, true, false], Row(view, "Dev:AndStg").Cells.Select(c => c is not null));
        Assert.Equal("d", Row(view, "Only:Dev").Cells[0]!.Value);
        Assert.Null(Row(view, "Only:Dev").Cells[2]);
        var secret = Row(view, "Api:Key").Cells[0]!;
        Assert.True(secret.IsSecret);
        Assert.Null(secret.Value);
        Assert.Equal("crm--Api--Key", secret.SecretName);
        Assert.Equal(0, world.SecretStore.Gets);
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, JsonSerializer.Serialize(view));
    }

    private static MatrixRow Row(MissingKeysView view, string key) => view.Rows.Single(r => r.Key == key);
}
