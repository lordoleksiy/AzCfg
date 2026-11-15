using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Model;

public class ConfigEnvironmentTests
{
    [Theory]
    [InlineData("prod")]
    [InlineData("Prod")]
    [InlineData("PROD")]
    [InlineData("production")]
    [InlineData("Production")]
    [InlineData("prd")]
    [InlineData("PRD")]
    public void IsProtectedByName_ProtectedName_ReturnsTrue(string name)
    {
        Assert.True(ConfigEnvironment.IsProtectedByName(name));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("stage")]
    [InlineData("staging")]
    [InlineData("Development")]
    [InlineData("production2")]
    public void IsProtectedByName_NonProtectedName_ReturnsFalse(string name)
    {
        Assert.False(ConfigEnvironment.IsProtectedByName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsProtectedByName_BlankName_ReturnsFalse(string? name)
    {
        Assert.False(ConfigEnvironment.IsProtectedByName(name!));
    }

    [Fact]
    public void Constructor_SetsIsProtected()
    {
        var env = new ConfigEnvironment("prod", new Uri("https://cfg.azconfig.io"), null, IsProtected: true);

        Assert.True(env.IsProtected);
    }
}
