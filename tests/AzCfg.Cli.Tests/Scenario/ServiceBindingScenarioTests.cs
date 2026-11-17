namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// A project whose service cannot be detected from the store's prefixes (the store has only <c>payments:*</c> service keys) is bound
/// with <c>--service</c> (ADR 0008).
/// </summary>
public sealed class ServiceBindingScenarioTests
{
    private const string Store = "https://payments-dev.azconfig.io";

    [Fact]
    public async Task Undetectable_service_fails_with_a_hint_and_binds_with_service_option()
    {
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-payments-dev", Store);
        host.AppConfig.Set(Store, "payments:Api:Url", "https://pay.example");
        host.AppConfig.Set(Store, "payments:Timeout", "5", "Development");
        host.AppConfig.Set(Store, "global:Logging:Level", "Information"); // global keys are not service prefixes

        var (exit, _) = await host.RunAsync("init", "--yes");
        Assert.Equal(0, exit);

        // pull --yes: the only service ('payments') does not match the 'Orders' project, so nothing is bound or written.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync("pull", "--yes"));
        Assert.Contains("--service", ex.Message);
        Assert.Null(host.Binding);
        Assert.Equal(0, host.Files.Writes);

        // pull --service: binds without detection and pulls global + 'orders' keys; no 'payments' key leaks in.
        (exit, var output) = await host.RunAsync("pull", "--yes", "--service", "orders", "--json");
        Assert.Equal(0, exit);
        var pull = ScenarioJson.Parse(output);
        Assert.Equal(("dev", 1, 0, false), (pull.GetProperty("environment").GetString(), pull.GetProperty("valuesWritten").GetInt32(), pull.GetProperty("secretsWritten").GetInt32(), pull.GetProperty("blocked").GetBoolean()));
        Assert.Equal(("orders", "dev"), (host.Binding!.Service, host.Binding.ActiveEnvironment));
        Assert.Equal(1, host.Files.Writes);
        Assert.Equal(new Dictionary<string, string> { ["Logging:Level"] = "Information" }, host.Files.Values());
    }
}
