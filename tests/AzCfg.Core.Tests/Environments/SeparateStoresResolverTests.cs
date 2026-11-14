using AzCfg.Core.Environments;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Environments;

public class SeparateStoresResolverTests
{
    [Fact]
    public void Resolve_ReturnsProfileEnvironments()
    {
        var dev = new ConfigEnvironment("dev", new Uri("https://dev.azconfig.io"), null, IsProtected: false);
        var prod = new ConfigEnvironment("prod", new Uri("https://prod.azconfig.io"), null, IsProtected: true);
        var profile = new UserProfile(new[] { dev, prod });

        var result = new SeparateStoresResolver().Resolve(profile);

        Assert.Equal(new[] { dev, prod }, result);
    }

    [Fact]
    public void Resolve_EnvironmentHasLabel_ClearsLabel()
    {
        var withLabel = new ConfigEnvironment("dev", new Uri("https://dev.azconfig.io"), "some-label", IsProtected: false);
        var profile = new UserProfile(new[] { withLabel });

        var result = new SeparateStoresResolver().Resolve(profile);

        Assert.Null(Assert.Single(result).Label);
    }

    [Fact]
    public void Resolve_NullProfile_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SeparateStoresResolver().Resolve(null!));
    }

    [Fact]
    public void Resolve_EmptyProfile_ReturnsEmpty()
    {
        var profile = new UserProfile(Array.Empty<ConfigEnvironment>());

        var result = new SeparateStoresResolver().Resolve(profile);

        Assert.Empty(result);
    }
}
