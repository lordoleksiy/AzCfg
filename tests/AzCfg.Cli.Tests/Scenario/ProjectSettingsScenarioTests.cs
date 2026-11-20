namespace AzCfg.Cli.Tests.Scenario;

/// <summary>Editable project settings end to end (ADR 0014): switching the service resets the active environment and the snapshots; <c>--service</c> never rebinds silently.</summary>
public sealed class ProjectSettingsScenarioTests
{
    private const string Store = "https://orders-dev.azconfig.io";

    [Fact]
    public async Task Changing_the_service_resets_the_active_environment_and_a_conflicting_service_option_is_refused()
    {
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", Store);
        host.AppConfig.Set(Store, "orders:Timeout", "30", "Development");
        host.AppConfig.Set(Store, "other:Timeout", "99", "Development");
        Assert.Equal(0, (await host.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await host.RunAsync("pull", "--yes", "--json")).Exit);
        Assert.Equal(("orders", "dev"), (host.Binding!.Service, host.Binding.ActiveEnvironment));
        Assert.NotNull(host.State.Snapshot(host.Repo, "dev"));

        // Same service is a no-op, (ordinal).
        Assert.Equal(0, (await host.RunAsync("pull", "--yes", "--json", "--service", "orders")).Exit);

        var (exit, output) = await host.RunAsync("config", "set", "project.service", "other");
        Assert.Equal(0, exit);
        Assert.Contains("project.service = other", output);
        Assert.Contains("azcfg use", output);
        Assert.Equal(("other", null), (host.Binding!.Service, host.Binding.ActiveEnvironment));
        Assert.Null(host.State.Snapshot(host.Repo, "dev"));

        (_, output) = await host.RunAsync("status", "--json");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, ScenarioJson.Parse(output).GetProperty("activeEnvironment").ValueKind);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync("pull", "--yes", "--service", "orders"));
        Assert.Contains("'other'", ex.Message);
        Assert.Contains("azcfg config set project.service orders", ex.Message);
        Assert.Equal("other", host.Binding.Service);

        (exit, output) = await host.RunAsync("config", "get", "project.service", "--json");
        Assert.Equal(0, exit);
        Assert.Equal("""{"key":"project.service","value":"other"}""", output.Trim());
    }
}
