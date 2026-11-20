using AzCfg.Core.Model;

namespace AzCfg.Ui.Tests.Application.Configuration;

public sealed class SecretRevealServiceTests
{
    [Fact]
    public async Task RevealRemoteAsync_ReturnsSecretValue_ButToStringIsRedacted()
    {
        using var world = new ConfigurationWorld();
        var uri = world.SeedRemoteSecret(ConfigurationWorld.Dev);

        var secret = await world.Reveal.RevealRemoteAsync(uri, default);

        Assert.Equal(ConfigurationWorld.Sentinel, secret.Reveal());
        Assert.DoesNotContain(ConfigurationWorld.Sentinel, secret.ToString());
    }

    [Fact]
    public async Task RevealLocalAsync_SecretsTarget_ReturnsValue()
    {
        using var world = new ConfigurationWorld();
        await world.WriteSecretsAsync(new LocalValue("Api:Key", ConfigurationWorld.Sentinel));

        var secret = await world.Reveal.RevealLocalAsync(world.Binding, "Api:Key", default);

        Assert.Equal(ConfigurationWorld.Sentinel, secret!.Reveal());
    }

    [Fact]
    public async Task RevealLocalAsync_ValuesTarget_ReturnsValue()
    {
        using var world = new ConfigurationWorld();
        await world.WriteValuesAsync(new LocalValue("Db:Host", "h"));

        Assert.Equal("h", (await world.Reveal.RevealLocalAsync(world.Binding, "Db:Host", default))!.Reveal());
    }

    [Fact]
    public async Task RevealLocalAsync_MissingKey_ReturnsNull()
    {
        using var world = new ConfigurationWorld();

        Assert.Null(await world.Reveal.RevealLocalAsync(world.Binding, "Nope", default));
    }
}
