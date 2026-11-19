namespace AzCfg.Cli.Tests.Scenario;

/// <summary><c>azcfg status</c> end to end: a local edit and a change in Azure are counted separately and no secret value leaks (ADR 0013).</summary>
public sealed class StatusScenarioTests
{
    [Fact]
    public async Task Status_after_pull_shows_one_local_and_one_remote_change()
    {
        const string store = "https://orders-dev.azconfig.io";
        const string secret = "s3cret!";
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", store);
        host.AppConfig.Set(store, "orders:Db:Host", "dev-db", "Development");
        host.AppConfig.Set(store, "orders:Timeout", "30", "Development");
        host.AppConfig.SetSecretReference(store, "orders:Db:Password", host.Secrets.Add("https://kv-orders-dev.vault.azure.net", "orders--Db--Password", secret), "Development");

        Assert.Equal(0, (await host.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await host.RunAsync("pull", "--yes", "--json")).Exit);

        var (exit, clean) = await host.RunAsync("status", "--json");
        Assert.Equal(0, exit);
        var summary = ScenarioJson.Parse(clean).GetProperty("summary");
        Assert.True(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, summary.GetProperty("localChanges").GetInt32());
        Assert.Equal(0, summary.GetProperty("remoteChanges").GetInt32());

        host.Files.SetLocal("Db:Host", "localhost");
        host.AppConfig.Set(store, "orders:Timeout", "45", "Development");

        (exit, var json) = await host.RunAsync("status", "--json");
        Assert.Equal(0, exit);
        var root = ScenarioJson.Parse(json);
        Assert.Equal("orders", root.GetProperty("service").GetString());
        Assert.Equal("dev", root.GetProperty("activeEnvironment").GetString());
        summary = root.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("localChanges").GetInt32());
        Assert.Equal(1, summary.GetProperty("remoteChanges").GetInt32());
        Assert.Empty(ScenarioJson.Strings(summary, "conflicts"));
        Assert.Equal(["Db:Host"], ScenarioJson.Strings(summary, "blockedKeys"));

        (exit, var human) = await host.RunAsync("status");
        Assert.Equal(0, exit);
        Assert.Contains("⇡ 1 local changes, ⇣ 1 remote changes, 0 conflicts", human);
        Assert.Contains("Db:Host", human);
        Assert.DoesNotContain(secret, json + human);
    }
}
