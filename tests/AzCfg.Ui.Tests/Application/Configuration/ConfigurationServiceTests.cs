using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Tests.Application.Configuration;

public sealed class ConfigurationServiceTests
{
    private static ConfigRow Row(ConfigurationView view, string key) => view.Rows.Single(r => r.Key == key);

    [Fact]
    public async Task LoadAsync_NonActiveEnvironment_IsRemoteOnlyWithoutSecretReads()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemoteSecret(ConfigurationWorld.Stg);
        world.SeedRemote(ConfigurationWorld.Stg, "crm:Db:Host", "stg-host");
        await world.WriteValuesAsync(new LocalValue("Db:Host", "local-host"));

        var view = await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Stg, default);

        Assert.True(view.IsRemoteOnly);
        Assert.Equal(0, world.SecretStore.Gets);
        Assert.All(view.Rows, r => Assert.Null(r.Status));
        Assert.All(view.Rows, r => Assert.Null(r.Local));
        Assert.Equal(["Api:Key", "Db:Host"], view.Rows.Select(r => r.Key));
        Assert.True(Row(view, "Api:Key").IsSecret);
        Assert.Equal("stg-host", Row(view, "Db:Host").Remote!.Value);
    }

    [Fact]
    public async Task LoadAsync_ActiveEnvironment_StatusesEqualDiff()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Host", "h");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Port", "1");
        await world.PullAsync(ConfigurationWorld.Dev);
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Port", "2");
        await world.WriteValuesAsync(new LocalValue("Db:Host", "changed"), new LocalValue("Db:Port", "1"), new LocalValue("New:Key", "x"));

        var view = await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Dev, default);
        var diff = await world.Sync.DiffAsync(world.Binding, ConfigurationWorld.Dev, default);

        Assert.False(view.IsRemoteOnly);
        Assert.Equal(diff.Items.Select(i => (i.Key, (DiffKind?)i.Kind)), view.Rows.Select(r => (r.Key, r.Status)));
        Assert.Equal(DiffKind.LocalModified, Row(view, "Db:Host").Status);
        Assert.Equal(DiffKind.RemoteModified, Row(view, "Db:Port").Status);
        Assert.Equal(DiffKind.LocalAdded, Row(view, "New:Key").Status);
        Assert.Equal("changed", Row(view, "Db:Host").Local!.Value);
        Assert.Equal(world.Values, Row(view, "Db:Host").Local!.Source);
    }

    [Fact]
    public async Task LoadAsync_IgnoredKey_IsRowWithoutStatus()
    {
        using var world = new ConfigurationWorld(ignore: ["Db:Host", "Only:Local"]);
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Host", "h");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Port", "1");
        await world.WriteValuesAsync(new LocalValue("Db:Host", "mine"), new LocalValue("Only:Local", "x"));

        var view = await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Dev, default);

        Assert.Equal(["Db:Host", "Db:Port", "Only:Local"], view.Rows.Select(r => r.Key));
        Assert.All(view.Rows.Where(r => r.IsIgnored), r => Assert.Null(r.Status));
        Assert.Equal(["Db:Host", "Only:Local"], view.Rows.Where(r => r.IsIgnored).Select(r => r.Key));
        Assert.Equal("mine", Row(view, "Db:Host").Local!.Value);
        Assert.Equal("h", Row(view, "Db:Host").Remote!.Value);
        Assert.Null(Row(view, "Only:Local").Remote);
        Assert.NotNull(Row(view, "Db:Port").Status);
    }

    [Fact]
    public async Task LoadAsync_ServiceOverridesGlobal_ShadowedGlobalValueAndLabel()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemote(ConfigurationWorld.Stg, "global:Log:Level", "Info");
        world.SeedRemote(ConfigurationWorld.Stg, "crm:Log:Level", "Debug", "blue");
        world.SeedRemote(ConfigurationWorld.Stg, "global:Only:Global", "g");

        var view = await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Stg, default);

        var level = Row(view, "Log:Level").Remote!;
        Assert.Equal("Debug", level.Value);
        Assert.Equal(ConfigScope.Service, level.Scope);
        Assert.Equal("blue", level.Label);
        Assert.Equal("Info", level.ShadowedGlobalValue);
        Assert.Equal(ConfigScope.Global, Row(view, "Only:Global").Remote!.Scope);
        Assert.Null(Row(view, "Only:Global").Remote!.ShadowedGlobalValue);
    }

    [Fact]
    public async Task LoadAsync_RemoteSecret_CellCarriesUriNameAndVaultHost()
    {
        using var world = new ConfigurationWorld();
        var uri = world.SeedRemoteSecret(ConfigurationWorld.Stg);

        var cell = Row(await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Stg, default), "Api:Key").Remote!;

        Assert.True(cell.IsSecret);
        Assert.Null(cell.Value);
        Assert.Equal(uri, cell.SecretUri);
        Assert.Equal("crm--Api--Key", cell.SecretName);
        Assert.Equal("kv.vault.azure.net", cell.VaultHost);
    }

    [Fact]
    public async Task LoadAsync_LocalSecretsTarget_IsMasked()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Host", "h");
        await world.WriteValuesAsync(new LocalValue("Db:Host", "h"));
        await world.WriteSecretsAsync(new LocalValue("Local:Secret", ConfigurationWorld.Sentinel));

        var row = Row(await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Dev, default), "Local:Secret");

        Assert.True(row.IsSecret);
        Assert.True(row.Local!.Masked);
        Assert.Null(row.Local.Value);
        Assert.Equal(world.Secrets, row.Local.Source);
    }

    [Fact]
    public async Task LoadAsync_And_ToJson_NeverContainSentinel()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemoteSecret(ConfigurationWorld.Dev);
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Db:Host", "h");
        await world.WriteValuesAsync(new LocalValue("Db:Host", "h"));
        await world.WriteSecretsAsync(new LocalValue("Api:Key", ConfigurationWorld.Sentinel), new LocalValue("Local:Secret", ConfigurationWorld.Sentinel));

        var view = await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Dev, default);
        var remote = world.Configuration.ToJson(view, ConfigSide.Remote);
        var local = world.Configuration.ToJson(view, ConfigSide.Local);

        Assert.True(world.SecretStore.Gets > 0);
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, JsonSerializer.Serialize(view));
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, view.ToString());
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, remote);
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, local);
        Assert.Equal("crm--Api--Key", JsonDocument.Parse(remote).RootElement.GetProperty("Api").GetProperty("Key").GetProperty("@kv").GetString());
        Assert.Equal("***", JsonDocument.Parse(local).RootElement.GetProperty("Local").GetProperty("Secret").GetString());
        Assert.Equal("h", JsonDocument.Parse(local).RootElement.GetProperty("Db").GetProperty("Host").GetString());
    }

    [Fact]
    public async Task LoadAsync_RemoteSecretWithLocalPlainValue_LocalValueMasked()
    {
        using var world = new ConfigurationWorld();
        world.SeedRemoteSecret(ConfigurationWorld.Dev);
        await world.WriteValuesAsync(new LocalValue("Api:Key", ConfigurationWorld.Sentinel));

        var view = await world.Configuration.LoadAsync(world.Binding, ConfigurationWorld.Dev, default);

        Assert.True(Row(view, "Api:Key").Local!.Masked);
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, JsonSerializer.Serialize(view));
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, world.Configuration.ToJson(view, ConfigSide.Local));
    }
}
