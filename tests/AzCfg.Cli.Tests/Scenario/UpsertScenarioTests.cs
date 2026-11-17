namespace AzCfg.Cli.Tests.Scenario;

/// <summary>Push is a 3-way upsert (ADR 0008): right after a pull, with no local edits, nothing is written in either environment mode.</summary>
public sealed class UpsertScenarioTests
{
    [Fact]
    public async Task Store_mode_push_without_local_edits_writes_nothing()
    {
        const string store = "https://orders-dev.azconfig.io";
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", store);
        host.AppConfig.Set(store, "orders:Db:Host", "dev-db", "Development");
        host.AppConfig.Set(store, "orders:Timeout", "30", "Development");
        host.AppConfig.Set(store, "orders:Timeout", "60");
        host.AppConfig.SetSecretReference(store, "orders:Db:Password", host.Secrets.Add("https://kv-orders-dev.vault.azure.net", "orders--Db--Password", "s3cret!"), "Development");
        host.AppConfig.Set(store, "global:Logging:Level", "Information");

        Assert.Equal(0, (await host.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await host.RunAsync("pull", "--yes", "--json")).Exit);

        var (output, human) = await PushUnchangedAsync(host, store);
        Assert.Contains("Warning: 'Timeout' exists under labels", human);
        Assert.DoesNotContain("s3cret!", output + human);
    }

    [Fact]
    public async Task Label_mode_push_without_local_edits_writes_nothing()
    {
        const string store = "https://shared.azconfig.io";
        var host = new ScenarioHost();
        host.AppConfig.Set(store, "orders:Db:Host", "shared-db");
        host.AppConfig.Set(store, "orders:Db:Host", "dev-db", "Development");
        host.AppConfig.Set(store, "orders:Db:Host", "prod-db", "Production");
        host.AppConfig.Set(store, "orders:Only:Shared", "s");
        host.AppConfig.Set(store, "global:Logging:Level", "Info");

        Assert.Equal(0, (await host.RunAsync("init", "--yes", "--mode", "labels", "--store", store)).Exit);
        Assert.Equal(0, (await host.RunAsync("use", "dev", "--yes", "--json")).Exit);

        var (_, human) = await PushUnchangedAsync(host, store);
        Assert.DoesNotContain("Warning:", human);
    }

    /// <summary>Runs <c>push --yes --json</c> and <c>push --yes</c> and asserts that neither wrote anything; returns both outputs.</summary>
    private static async Task<(string Json, string Human)> PushUnchangedAsync(ScenarioHost host, string store)
    {
        var before = host.AppConfig.Settings(store);

        var (exit, json) = await host.RunAsync("push", "--yes", "--json");
        Assert.Equal(0, exit);
        ScenarioJson.AssertApplied(json, 0);

        (exit, var human) = await host.RunAsync("push", "--yes");
        Assert.Equal(0, exit);
        Assert.Contains("No changes.", human);

        Assert.Equal(before, host.AppConfig.Settings(store));
        Assert.Equal(0, host.AppConfig.Writes);
        return (json, human);
    }
}
