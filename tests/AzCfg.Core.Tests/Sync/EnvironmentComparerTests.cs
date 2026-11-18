using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class EnvironmentComparerTests
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service, string? label = null) =>
        new(key, scope, new ConfigValue.Plain(value), Label: label);
    private static ConfigEntry Ref(string key, string uri) => new(key, ConfigScope.Service, new ConfigValue.KeyVaultRef(new(uri)));
    private static ConfigEntry Resolved(string key, string uri, string value) =>
        new(key, ConfigScope.Service, new ConfigValue.ResolvedSecret(new(uri), new SecretString(value)));

    [Fact]
    public void Compare_AllFourKinds_OrderedOrdinallyByKey()
    {
        var left = new[] { Plain("b", "1"), Plain("Same", "x"), Plain("Diff", "1"), Plain("OnlyL", "l") };
        var right = new[] { Plain("OnlyR", "r"), Plain("Diff", "2"), Plain("Same", "x"), Plain("B", "1") };

        var items = EnvironmentComparer.Compare(left, right);

        Assert.Equal(
            [("B", ComparisonKind.OnlyRight), ("Diff", ComparisonKind.Different), ("OnlyL", ComparisonKind.OnlyLeft),
                ("OnlyR", ComparisonKind.OnlyRight), ("Same", ComparisonKind.Same), ("b", ComparisonKind.OnlyLeft)],
            items.Select(i => (i.Key, i.Kind)));
        var diff = items.Single(i => i.Key == "Diff");
        Assert.Equal((left[2], right[1]), (diff.Left, diff.Right));
        Assert.Null(items.Single(i => i.Key == "OnlyL").Right);
        Assert.Null(items.Single(i => i.Key == "OnlyR").Left);
    }

    [Fact]
    public void Compare_PlainValues_CaseSensitive()
    {
        var item = Assert.Single(EnvironmentComparer.Compare([Plain("A", "x")], [Plain("A", "X")]));

        Assert.Equal(ComparisonKind.Different, item.Kind);
    }

    [Fact]
    public void Compare_SecretsEqualByNameAcrossVaults()
    {
        var items = EnvironmentComparer.Compare(
            [Ref("P", "https://dev.vault.azure.net/secrets/orders--P/111"), Ref("Q", "https://dev.vault.azure.net/secrets/q1")],
            [Resolved("P", "https://prod.vault.azure.net/secrets/orders--P", Sentinel), Ref("Q", "https://dev.vault.azure.net/secrets/q2")]);

        Assert.Equal([ComparisonKind.Same, ComparisonKind.Different], items.Select(i => i.Kind));
    }

    [Fact]
    public void Compare_ResolvedSecretValueIgnored()
    {
        var item = Assert.Single(EnvironmentComparer.Compare(
            [Resolved("P", "https://dev.vault.azure.net/secrets/p", "one")],
            [Resolved("P", "https://dev.vault.azure.net/secrets/p", "two")]));

        Assert.Equal(ComparisonKind.Same, item.Kind);
    }

    [Fact]
    public void Compare_PlainVsReference_Different()
    {
        var item = Assert.Single(EnvironmentComparer.Compare(
            [Plain("P", "https://dev.vault.azure.net/secrets/p")], [Ref("P", "https://dev.vault.azure.net/secrets/p")]));

        Assert.Equal(ComparisonKind.Different, item.Kind);
    }

    [Fact]
    public void Compare_ScopeAndLabelIgnored()
    {
        var item = Assert.Single(EnvironmentComparer.Compare([Plain("A", "1", ConfigScope.Global, "dev")], [Plain("A", "1")]));

        Assert.Equal(ComparisonKind.Same, item.Kind);
    }

    [Fact]
    public void Compare_Empty_ReturnsEmpty()
    {
        Assert.Empty(EnvironmentComparer.Compare([], []));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compare_DuplicateKey_ThrowsNamingKey(bool onLeft)
    {
        ConfigEntry[] duplicates = [Plain("Dup", Sentinel), Plain("Dup", Sentinel, ConfigScope.Global)];

        var ex = Assert.Throws<ArgumentException>(() => onLeft
            ? EnvironmentComparer.Compare(duplicates, [])
            : EnvironmentComparer.Compare([], duplicates));

        Assert.Contains("'Dup'", ex.Message);
        Assert.DoesNotContain(Sentinel, ex.Message);
    }

    [Fact]
    public void Compare_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => EnvironmentComparer.Compare(null!, []));
        Assert.Throws<ArgumentNullException>(() => EnvironmentComparer.Compare([], null!));
    }
}
