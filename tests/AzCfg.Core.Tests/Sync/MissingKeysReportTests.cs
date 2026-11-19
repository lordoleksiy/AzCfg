using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public sealed class MissingKeysReportTests
{
    private static RemoteConfig Config(string env, params string[] keys) =>
        new(new ConfigEnvironment(env, new Uri($"https://{env}.azconfig.io"), null, false),
            [.. keys.Select(k => new ConfigEntry(k, ConfigScope.Service, new ConfigValue.Plain("v")))], []);

    [Fact]
    public void Compute_ThreeEnvironments_ListsKeysMissingSomewhere()
    {
        var report = MissingKeysReport.Compute([
            Config("dev", "A", "B", "C"),
            Config("test", "A", "C"),
            Config("prod", "A", "D"),
        ]);

        Assert.Equal(["B", "C", "D"], report.Select(m => m.Key));
        Assert.Equal(["dev"], report[0].PresentIn);
        Assert.Equal(["test", "prod"], report[0].MissingIn);
        Assert.Equal(["dev", "test"], report[1].PresentIn);
        Assert.Equal(["prod"], report[1].MissingIn);
        Assert.Equal(["prod"], report[2].PresentIn);
        Assert.Equal(["dev", "test"], report[2].MissingIn);
    }

    [Fact]
    public void Compute_KeyInEveryEnvironment_IsNotReported()
    {
        Assert.Empty(MissingKeysReport.Compute([Config("dev", "A"), Config("prod", "A")]));
    }

    [Fact]
    public void Compute_NoConfigs_IsEmpty()
    {
        Assert.Empty(MissingKeysReport.Compute([]));
    }
}
