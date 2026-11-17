using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Mapping;

public class LabelSelectorTests
{
    private static readonly Uri Store = new("https://dev.azconfig.io");

    private static ConfigEnvironment StoreMode(string? writeLabel = null) => new("dev", Store, null, false, writeLabel);

    private static ConfigEnvironment LabelMode(string label) => new("dev", Store, label, false, label);

    private static ConfigEntry Entry(string key, string value, string? label, ConfigScope scope = ConfigScope.Service) =>
        new(key, scope, new ConfigValue.Plain(value), Label: label);

    [Fact]
    public void Select_UniqueKeys_KeptInOrderWithoutWarnings()
    {
        ConfigEntry[] entries = [Entry("B", "1", "Development"), Entry("A", "2", null), Entry("A", "3", null, ConfigScope.Global)];

        var (selected, warnings) = LabelSelector.Select(entries, StoreMode());

        Assert.Equal(entries, selected);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Select_StoreMode_PrefersWriteLabel_AndWarns()
    {
        var (selected, warnings) = LabelSelector.Select(
            [Entry("K", "none", null), Entry("K", "dev", "Development"), Entry("K", "x", "Another")], StoreMode("Development"));

        Assert.Equal("dev", ((ConfigValue.Plain)Assert.Single(selected).Value).Value);
        var warning = Assert.Single(warnings);
        Assert.Equal(SyncWarningKind.DuplicateLabels, warning.Kind);
        Assert.Equal("K", warning.Key);
        Assert.Contains("Development", warning.Message);
    }

    [Fact]
    public void Select_StoreMode_WithoutWriteLabelMatch_PrefersUnlabeled()
    {
        var (selected, _) = LabelSelector.Select([Entry("K", "dev", "Development"), Entry("K", "none", null)], StoreMode("Other"));

        Assert.Null(Assert.Single(selected).Label);
    }

    [Fact]
    public void Select_StoreMode_AllLabeled_PrefersOrdinalFirstLabel()
    {
        var (selected, warnings) = LabelSelector.Select([Entry("K", "b", "B"), Entry("K", "a", "A")], StoreMode());

        Assert.Equal("A", Assert.Single(selected).Label);
        Assert.Single(warnings);
    }

    [Fact]
    public void Select_LabelMode_EnvironmentLabelOverridesUnlabeled_WithoutWarning()
    {
        var (selected, warnings) = LabelSelector.Select([Entry("K", "shared", null), Entry("K", "dev", "Development")], LabelMode("Development"));

        Assert.Equal("Development", Assert.Single(selected).Label);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Select_SameKeyInDifferentScopes_IsNotADuplicate()
    {
        var (selected, warnings) = LabelSelector.Select(
            [Entry("K", "g", "Development", ConfigScope.Global), Entry("K", "s", "Development")], StoreMode());

        Assert.Equal(2, selected.Count);
        Assert.Empty(warnings);
    }
}
