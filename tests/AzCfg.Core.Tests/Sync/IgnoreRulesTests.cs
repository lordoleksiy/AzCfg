using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class IgnoreRulesTests
{
    [Theory]
    [InlineData("Logging:*", "Logging:LogLevel:Default", ConfigScope.Service, true)]
    [InlineData("Logging:*", "Logging:LogLevel:Default", ConfigScope.Global, true)]
    [InlineData("Logging:*", "Logging", ConfigScope.Service, false)]
    [InlineData("Logging:*", "LoggingX:Level", ConfigScope.Service, false)]
    [InlineData("global:*", "Anything:At:All", ConfigScope.Global, true)]
    [InlineData("global:*", "Anything", ConfigScope.Service, false)]
    [InlineData("global:Serilog:*", "Serilog:MinimumLevel", ConfigScope.Global, true)]
    [InlineData("global:Serilog:*", "Serilog:MinimumLevel", ConfigScope.Service, false)]
    [InlineData("global:Serilog:*", "Logging:Level", ConfigScope.Global, false)]
    [InlineData("GLOBAL:serilog:*", "Serilog:MinimumLevel", ConfigScope.Global, true)]
    [InlineData("*:Password", "Db:Password", ConfigScope.Service, true)]
    [InlineData("*:Password", "Crm:Db:Password", ConfigScope.Global, true)]
    [InlineData("*:Password", "Password", ConfigScope.Service, false)]
    [InlineData("*:Password", "Db:PasswordHash", ConfigScope.Service, false)]
    [InlineData("ConnectionStrings:Local", "ConnectionStrings:Local", ConfigScope.Service, true)]
    [InlineData("ConnectionStrings:Local", "connectionstrings:LOCAL", ConfigScope.Global, true)]
    [InlineData("ConnectionStrings:Local", "ConnectionStrings:Local2", ConfigScope.Service, false)]
    [InlineData("ConnectionStrings:Local", "My:ConnectionStrings:Local", ConfigScope.Service, false)]
    [InlineData("*", "A", ConfigScope.Service, true)]
    [InlineData("A*C", "A:B:C", ConfigScope.Service, true)]
    [InlineData("A*C", "A:B:CD", ConfigScope.Service, false)]
    [InlineData("a.b", "aXb", ConfigScope.Service, false)]
    [InlineData("A?", "AB", ConfigScope.Service, false)]
    [InlineData("A?", "A?", ConfigScope.Service, true)]
    [InlineData("  Logging:*  ", "Logging:Level", ConfigScope.Service, true)]
    public void IsIgnored_MatchesScopePrefixAndWildcardIgnoringCase(string pattern, string key, ConfigScope scope, bool expected) =>
        Assert.Equal(expected, IgnoreRules.IsIgnored([pattern], key, scope));

    [Fact]
    public void IsIgnored_NoPatterns_IsFalse()
    {
        Assert.False(IgnoreRules.IsIgnored(null, "A", ConfigScope.Service));
        Assert.False(IgnoreRules.IsIgnored([], "A", ConfigScope.Global));
    }

    [Fact]
    public void IsIgnored_AnyPatternMatches()
    {
        string[] patterns = ["global:Serilog:*", "Db:*"];

        Assert.True(IgnoreRules.IsIgnored(patterns, "Db:Host", ConfigScope.Service));
        Assert.True(IgnoreRules.IsIgnored(patterns, "Serilog:Level", ConfigScope.Global));
        Assert.False(IgnoreRules.IsIgnored(patterns, "Serilog:Level", ConfigScope.Service));
    }

    [Fact]
    public void IsIgnored_BlankStoredPattern_MatchesNothing() =>
        Assert.False(IgnoreRules.IsIgnored(["", "  "], "A", ConfigScope.Service));

    [Theory]
    [InlineData("Logging:*", "Logging:*")]
    [InlineData("  global:Serilog:*\t", "global:Serilog:*")]
    public void Normalize_Trims(string pattern, string expected) =>
        Assert.Equal(expected, IgnoreRules.Normalize(pattern));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("global:")]
    [InlineData(" GLOBAL:  ")]
    public void Normalize_BlankOrEmptyPath_Throws(string pattern) =>
        Assert.Throws<ArgumentException>(() => IgnoreRules.Normalize(pattern));
}
