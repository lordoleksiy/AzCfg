using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Mapping;

public class ScopeMergerTests
{
    private static ConfigEntry Entry(string key, ConfigScope scope, string value, string? etag = null) =>
        new(key, scope, new ConfigValue.Plain(value), etag);

    private static IEnumerable<ConfigEntry> LazyEntries()
    {
        yield return Entry("x", ConfigScope.Global, "1");
        yield return Entry("x", ConfigScope.Service, "2");
    }

    [Fact]
    public void Merge_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ScopeMerger.Merge(null!));
    }

    [Fact]
    public void Merge_EmptyInput_ReturnsEmptyList()
    {
        var result = ScopeMerger.Merge(Array.Empty<ConfigEntry>());

        Assert.Empty(result);
    }

    [Fact]
    public void Merge_ServiceOverridesGlobal_WithSameKey()
    {
        var global = Entry("Level", ConfigScope.Global, "Info", "g1");
        var service = Entry("Level", ConfigScope.Service, "Debug", "s1");

        var result = ScopeMerger.Merge(new[] { global, service });

        var winner = Assert.Single(result);
        Assert.Equal(service, winner);
        Assert.Equal(ConfigScope.Service, winner.Scope);
        Assert.Equal("s1", winner.ETag);
        Assert.Equal(new ConfigValue.Plain("Debug"), winner.Value);
    }

    [Fact]
    public void Merge_ServiceOverridesGlobal_RegardlessOfInputOrder()
    {
        var global = Entry("Level", ConfigScope.Global, "Info");
        var service = Entry("Level", ConfigScope.Service, "Debug");

        var result = ScopeMerger.Merge(new[] { service, global });

        Assert.Equal(service, Assert.Single(result));
    }

    [Fact]
    public void Merge_KeysUniqueToEachScope_AreKeptWithTheirScope()
    {
        var g = Entry("OnlyGlobal", ConfigScope.Global, "1");
        var s = Entry("OnlyService", ConfigScope.Service, "2");

        var result = ScopeMerger.Merge(new[] { g, s });

        Assert.Equal(2, result.Count);
        Assert.Contains(g, result);
        Assert.Contains(s, result);
    }

    [Fact]
    public void Merge_GlobalEntryNotOverridden_KeepsGlobalProvenanceAndEtag()
    {
        var g = Entry("Shared", ConfigScope.Global, "v", "etag-g");

        var result = ScopeMerger.Merge(new[] { g });

        var only = Assert.Single(result);
        Assert.Equal(ConfigScope.Global, only.Scope);
        Assert.Equal("etag-g", only.ETag);
    }

    [Fact]
    public void Merge_ResultIsOrderedByKey_Ordinal()
    {
        var entries = new[]
        {
            Entry("b", ConfigScope.Service, "1"),
            Entry("B", ConfigScope.Global, "2"),
            Entry("a", ConfigScope.Global, "3"),
            Entry("A:Z", ConfigScope.Service, "4"),
            Entry("A", ConfigScope.Global, "5"),
        };

        var result = ScopeMerger.Merge(entries);

        Assert.Equal(new[] { "A", "A:Z", "B", "a", "b" }, result.Select(e => e.Key));
    }

    [Fact]
    public void Merge_KeyComparisonIsCaseSensitive()
    {
        var lower = Entry("a", ConfigScope.Global, "1");
        var upper = Entry("A", ConfigScope.Service, "2");

        var result = ScopeMerger.Merge(new[] { lower, upper });

        Assert.Equal(2, result.Count);
        Assert.Contains(lower, result);
        Assert.Contains(upper, result);
    }

    [Fact]
    public void Merge_DuplicateKeyWithinSameScope_LastWins()
    {
        var first = Entry("K", ConfigScope.Service, "first", "1");
        var last = Entry("K", ConfigScope.Service, "last", "2");

        var result = ScopeMerger.Merge(new[] { first, last });

        Assert.Equal(last, Assert.Single(result));
    }

    [Fact]
    public void Merge_DuplicateKeyWithinGlobalScope_LastWins()
    {
        var first = Entry("K", ConfigScope.Global, "first");
        var last = Entry("K", ConfigScope.Global, "last");

        var result = ScopeMerger.Merge(new[] { first, last });

        Assert.Equal(last, Assert.Single(result));
    }

    [Fact]
    public void Merge_DuplicatesInBothScopes_LastServiceWinsOverGlobal()
    {
        var entries = new[]
        {
            Entry("K", ConfigScope.Service, "s1"),
            Entry("K", ConfigScope.Global, "g1"),
            Entry("K", ConfigScope.Service, "s2"),
            Entry("K", ConfigScope.Global, "g2"),
        };

        var result = ScopeMerger.Merge(entries);

        var winner = Assert.Single(result);
        Assert.Equal(ConfigScope.Service, winner.Scope);
        Assert.Equal(new ConfigValue.Plain("s2"), winner.Value);
    }

    [Fact]
    public void Merge_PreservesNonPlainValuesUntouched()
    {
        var uri = new Uri("https://kv.vault.azure.net/secrets/x");
        var service = new ConfigEntry("Secret", ConfigScope.Service, new ConfigValue.KeyVaultRef(uri), "e");

        var result = ScopeMerger.Merge(new[] { Entry("Secret", ConfigScope.Global, "plain"), service });

        Assert.Equal(service, Assert.Single(result));
    }

    [Fact]
    public void Merge_AcceptsLazyEnumerable()
    {
        var result = ScopeMerger.Merge(LazyEntries());

        Assert.Equal(ConfigScope.Service, Assert.Single(result).Scope);
    }
}
