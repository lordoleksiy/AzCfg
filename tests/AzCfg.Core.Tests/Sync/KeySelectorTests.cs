using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public sealed class KeySelectorTests
{
    [Fact]
    public void NoPatterns_SelectsEverything()
    {
        Assert.True(KeySelector.IsSelected(null, "A", ConfigScope.Service));
        Assert.True(KeySelector.IsSelected([], "A", ConfigScope.Global));
    }

    [Theory]
    [InlineData("Logging:*", "Logging:Level", ConfigScope.Service, true)]
    [InlineData("Logging:*", "Other:Level", ConfigScope.Service, false)]
    [InlineData("logging:level", "Logging:Level", ConfigScope.Service, true)]
    [InlineData("Db", "Db:Password", ConfigScope.Service, false)]
    [InlineData("global:*", "A", ConfigScope.Global, true)]
    [InlineData("global:*", "A", ConfigScope.Service, false)]
    [InlineData("global:Serilog:*", "Serilog:Level", ConfigScope.Global, true)]
    [InlineData("global:Serilog:*", "Serilog:Level", ConfigScope.Service, false)]
    public void SinglePattern(string pattern, string key, ConfigScope scope, bool expected) =>
        Assert.Equal(expected, KeySelector.IsSelected([pattern], key, scope));

    [Theory]
    [InlineData("Logging:Level", ConfigScope.Service, true)]
    [InlineData("Db:Password", ConfigScope.Service, true)]
    [InlineData("Serilog:Level", ConfigScope.Global, true)]
    [InlineData("Serilog:Level", ConfigScope.Service, false)]
    [InlineData("Other", ConfigScope.Service, false)]
    public void SeveralPatterns_AreOr(string key, ConfigScope scope, bool expected) =>
        Assert.Equal(expected, KeySelector.IsSelected(["Logging:*", "Db:*", "global:Serilog:*"], key, scope));

    [Fact]
    public void BlankPattern_MatchesNothing() =>
        Assert.False(KeySelector.IsSelected(["  "], "A", ConfigScope.Service));
}
