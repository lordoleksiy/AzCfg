using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Model;

public class EnvironmentModeNamesTests
{
    [Theory]
    [InlineData(EnvironmentMode.StorePerEnvironment, "stores")]
    [InlineData(EnvironmentMode.LabelPerEnvironment, "labels")]
    public void Format_UsesCliNames(EnvironmentMode mode, string expected) =>
        Assert.Equal(expected, EnvironmentModeNames.Format(mode));

    [Theory]
    [InlineData("stores", EnvironmentMode.StorePerEnvironment)]
    [InlineData("LABELS", EnvironmentMode.LabelPerEnvironment)]
    [InlineData(" labels ", EnvironmentMode.LabelPerEnvironment)]
    public void TryParse_AcceptsNamesCaseInsensitively(string value, EnvironmentMode expected)
    {
        Assert.True(EnvironmentModeNames.TryParse(value, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("store")]
    [InlineData("StorePerEnvironment")]
    public void TryParse_RejectsOtherValues(string? value) =>
        Assert.False(EnvironmentModeNames.TryParse(value, out _));
}
