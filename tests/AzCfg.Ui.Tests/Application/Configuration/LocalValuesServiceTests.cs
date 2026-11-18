using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Application.Configuration;

public sealed class LocalValuesServiceTests
{
    private static async Task<IReadOnlyList<LocalValue>> ReadValuesAsync(ConfigurationWorld world) =>
        await world.Provider.GetServices<ILocalTarget>().First(t => t.Kind == world.Values.Kind).ReadAsync(world.Values, default);

    private static Task<IReadOnlyList<LocalValue>> ReadSecretsAsync(ConfigurationWorld world) => world.World.UserSecrets.ReadAsync(world.Secrets, default);

    [Fact]
    public async Task SetPlainAsync_WritesValuesTargetAndRemovesFromSecretsTarget()
    {
        using var world = new ConfigurationWorld();
        await world.WriteSecretsAsync(new LocalValue("Api:Key", "s"), new LocalValue("Other", "o"));

        await world.LocalValues.SetPlainAsync(world.Binding, "Api:Key", "plain", default);

        Assert.Equal([("Api:Key", "plain")], (await ReadValuesAsync(world)).Select(v => (v.Key, v.Value)));
        Assert.Equal([("Other", "o")], (await ReadSecretsAsync(world)).Select(v => (v.Key, v.Value)));
    }

    [Fact]
    public async Task SetSecretAsync_WritesSecretsTargetAndRemovesFromValuesTarget()
    {
        using var world = new ConfigurationWorld();
        await world.WriteValuesAsync(new LocalValue("Api:Key", "plain"), new LocalValue("Db:Host", "h"));

        await world.LocalValues.SetSecretAsync(world.Binding, "Api:Key", new SecretString("sec"), default);

        Assert.Equal([("Db:Host", "h")], (await ReadValuesAsync(world)).Select(v => (v.Key, v.Value)));
        Assert.Equal([("Api:Key", "sec")], (await ReadSecretsAsync(world)).Select(v => (v.Key, v.Value)));
    }

    [Fact]
    public async Task SetPlainAsync_ExistingGlobalKey_KeepsScopeMarker()
    {
        using var world = new ConfigurationWorld();
        await world.WriteValuesAsync(new LocalValue("Log:Level", "Info", ConfigScope.Global), new LocalValue("Db:Host", "h"));

        await world.LocalValues.SetPlainAsync(world.Binding, "Log:Level", "Debug", default);

        var level = (await ReadValuesAsync(world)).Single(v => v.Key == "Log:Level");
        Assert.Equal(("Debug", ConfigScope.Global), (level.Value, level.Scope));
        Assert.Contains("//@global", await File.ReadAllTextAsync(world.ValuesFile));
    }

    [Fact]
    public async Task SetSecretAsync_ExistingGlobalKey_KeepsScopeAcrossTargets()
    {
        using var world = new ConfigurationWorld();
        await world.WriteValuesAsync(new LocalValue("Api:Key", "plain", ConfigScope.Global));

        await world.LocalValues.SetSecretAsync(world.Binding, "Api:Key", new SecretString("sec"), default);

        Assert.Equal(ConfigScope.Global, (await ReadSecretsAsync(world)).Single().Scope);
    }

    [Fact]
    public async Task SetScopeAsync_TogglesGlobalMarker()
    {
        using var world = new ConfigurationWorld();
        await world.WriteValuesAsync(new LocalValue("Db:Host", "h"));

        await world.LocalValues.SetScopeAsync(world.Binding, "Db:Host", ConfigScope.Global, default);
        Assert.Contains("//@global", await File.ReadAllTextAsync(world.ValuesFile));
        Assert.Equal(ConfigScope.Global, (await ReadValuesAsync(world)).Single().Scope);

        await world.LocalValues.SetScopeAsync(world.Binding, "Db:Host", ConfigScope.Service, default);
        Assert.DoesNotContain("//@global", await File.ReadAllTextAsync(world.ValuesFile));
        Assert.Equal(ConfigScope.Service, (await ReadValuesAsync(world)).Single().Scope);
    }

    [Fact]
    public async Task SetScopeAsync_MissingKey_ThrowsWithoutWriting()
    {
        using var world = new ConfigurationWorld();

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.LocalValues.SetScopeAsync(world.Binding, "Nope", ConfigScope.Global, default));
        Assert.False(File.Exists(world.ValuesFile));
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromBothTargets_AndMissingKeyIsNoOp()
    {
        using var world = new ConfigurationWorld();
        await world.WriteValuesAsync(new LocalValue("A", "1"), new LocalValue("B", "2"));
        await world.WriteSecretsAsync(new LocalValue("A", "s"), new LocalValue("C", "3"));

        await world.LocalValues.RemoveAsync(world.Binding, "A", default);
        await world.LocalValues.RemoveAsync(world.Binding, "Missing", default);

        Assert.Equal(["B"], (await ReadValuesAsync(world)).Select(v => v.Key));
        Assert.Equal(["C"], (await ReadSecretsAsync(world)).Select(v => v.Key));
    }

    [Fact]
    public async Task SetSecretAsync_UnsafeSecretsTarget_ThrowsAndWritesNothing()
    {
        using var world = new ConfigurationWorld();
        var unsafeBinding = world.Binding with { ValuesTarget = world.Secrets, SecretsTarget = world.Values };
        await world.WriteSecretsAsync(new LocalValue("Db:Host", "h"));
        var writes = world.World.UserSecrets.Writes;

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.LocalValues.SetSecretAsync(unsafeBinding, "Api:Key", new SecretString(ConfigurationWorld.Sentinel), default));

        Assert.False(File.Exists(world.ValuesFile));
        Assert.Equal(writes, world.World.UserSecrets.Writes);
    }

    [Fact]
    public async Task SetPlainAsync_SharedTarget_WritesOnce()
    {
        using var world = new ConfigurationWorld();
        var shared = world.Binding with { SecretsTarget = world.Values };
        await world.WriteValuesAsync(new LocalValue("A", "1"));

        await world.LocalValues.SetPlainAsync(shared, "B", "2", default);

        Assert.Equal(["A", "B"], (await ReadValuesAsync(world)).Select(v => v.Key).Order());
    }
}
