using AzCfg.Core.Model;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// <c>//@global</c> marker (ADR 0009, U3): pull marks keys that come from <c>global:</c>, and a locally modified marked key is pushed
/// back to <c>global:</c>, not as a service override.
/// </summary>
public sealed class GlobalMarkerScenarioTests
{
    private const string Store = "https://orders-dev.azconfig.io";

    [Fact]
    public async Task Modified_global_key_is_pushed_to_global_scope()
    {
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", Store);
        host.AppConfig.Set(Store, "orders:Db:Host", "dev-db");
        host.AppConfig.Set(Store, "global:Logging:Level", "Information");
        host.AppConfig.Set(Store, "global:Timeout", "30");
        host.AppConfig.Set(Store, "orders:Timeout", "60");

        Assert.Equal(0, (await host.RunAsync("init", "--yes")).Exit);
        Assert.Equal(0, (await host.RunAsync("pull", "--yes", "--json")).Exit);

        // global keys come marked; a service override of a global key stays service.
        Assert.Equal(ConfigScope.Global, host.Files.Scope("Logging:Level"));
        Assert.Equal(ConfigScope.Service, host.Files.Scope("Timeout"));
        Assert.Equal(ConfigScope.Service, host.Files.Scope("Db:Host"));

        host.Files.SetLocal("Logging:Level", "Debug");

        var (exit, output) = await host.RunAsync("plan", "--json");
        Assert.Equal(0, exit);
        var plan = ScenarioJson.Parse(output);
        Assert.Equal([("update", "global", "Logging:Level", null, null)], ScenarioJson.Changes(plan));
        Assert.True(plan.GetProperty("touchesGlobal").GetBoolean());

        (exit, output) = await host.RunAsync("push", "--yes", "--json");
        Assert.Equal(0, exit);
        ScenarioJson.AssertApplied(output, 1);
        Assert.Equal("Debug", host.AppConfig.Get(Store, "global:Logging:Level")!.Text);
        Assert.Null(host.AppConfig.Get(Store, "orders:Logging:Level"));
        Assert.Equal("dev-db", host.AppConfig.Get(Store, "orders:Db:Host")!.Text);
    }
}
