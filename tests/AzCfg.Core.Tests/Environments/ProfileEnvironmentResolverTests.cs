using AzCfg.Core.Environments;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Environments;

public class ProfileEnvironmentResolverTests
{
    private static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), "Development", false);

    [Fact]
    public void Resolve_StorePerEnvironment_ReadsAllLabels()
    {
        var env = Assert.Single(new ProfileEnvironmentResolver().Resolve(new UserProfile([Dev])));

        Assert.Null(env.Label);
        Assert.Equal("Development", env.WriteLabel);
    }

    [Fact]
    public void Resolve_LabelPerEnvironment_SelectsByLabel()
    {
        var env = Assert.Single(new ProfileEnvironmentResolver().Resolve(new UserProfile([Dev], EnvironmentMode.LabelPerEnvironment)));

        Assert.Equal("Development", env.Label);
    }

    [Fact]
    public void Resolve_NullProfile_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ProfileEnvironmentResolver().Resolve(null!));
}
