using AzCfg.Core.Environments;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Environments;

public class LabelResolverTests
{
    private static readonly Uri Store = new("https://shared.azconfig.io");

    [Fact]
    public void Resolve_UsesEnvironmentLabelForReadAndWrite()
    {
        var profile = new UserProfile([new ConfigEnvironment("dev", Store, "Development", false)], EnvironmentMode.LabelPerEnvironment);

        var env = Assert.Single(new LabelResolver().Resolve(profile));

        Assert.Equal("Development", env.Label);
        Assert.Equal("Development", env.WriteLabel);
        Assert.Equal(Store, env.StoreEndpoint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Resolve_NoLabel_FallsBackToEnvironmentName(string? label)
    {
        var profile = new UserProfile([new ConfigEnvironment("stage", Store, label, false)], EnvironmentMode.LabelPerEnvironment);

        var env = Assert.Single(new LabelResolver().Resolve(profile));

        Assert.Equal("stage", env.Label);
        Assert.Equal("stage", env.WriteLabel);
    }

    [Fact]
    public void Resolve_NullProfile_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new LabelResolver().Resolve(null!));
}
