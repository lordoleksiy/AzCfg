namespace AzCfg.Cli.Tests.Scenario;

/// <summary><c>azcfg status --check</c> end to end (ADR 0013 amendment): a Key Vault secret deleted after the pull is a dead reference, exit code 1, no secret value in the output.</summary>
public sealed class HealthCheckScenarioTests
{
    private const string Store = "https://orders-dev.azconfig.io";
    private const string Secret = "s3cret!";

    private static async Task<(ScenarioHost Host, Uri SecretUri)> PulledAsync()
    {
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", Store);
        host.AppConfig.Set(Store, "orders:Timeout", "30", "Development");
        var uri = host.Secrets.Add("https://kv-orders-dev.vault.azure.net", "orders--Db--Password", Secret);
        host.AppConfig.SetSecretReference(Store, "orders:Db:Password", uri, "Development");

        Assert.Equal(0, (await host.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await host.RunAsync("pull", "--yes", "--json")).Exit);
        return (host, uri);
    }

    [Fact]
    public async Task Healthy_project_exits_0()
    {
        var (host, _) = await PulledAsync();

        var (exit, output) = await host.RunAsync("status", "--check");

        Assert.Equal(0, exit);
        Assert.Contains("signed in as dev@contoso.com", output);
        Assert.Contains("No problems found.", output);
    }

    [Fact]
    public async Task Deleted_key_vault_secret_is_a_dead_reference_and_fails_the_check()
    {
        var (host, uri) = await PulledAsync();
        Assert.True(host.Secrets.Remove(uri));

        var (exit, json) = await host.RunAsync("status", "--check", "--json");

        Assert.Equal(1, exit);
        var report = ScenarioJson.Parse(json);
        Assert.True(report.GetProperty("hasErrors").GetBoolean());
        var finding = Assert.Single(report.GetProperty("findings").EnumerateArray());
        Assert.Equal("deadSecretReference", finding.GetProperty("kind").GetString());
        Assert.Equal("error", finding.GetProperty("severity").GetString());
        Assert.Equal("Db:Password", finding.GetProperty("key").GetString());

        var (humanExit, human) = await host.RunAsync("status", "--check");
        Assert.Equal(1, humanExit);
        Assert.Contains("Errors:", human);
        Assert.Contains("Db:Password", human);
        Assert.DoesNotContain(Secret, json + human);
    }

    [Fact]
    public async Task Signed_out_account_fails_the_check_with_the_az_login_fix()
    {
        var (host, _) = await PulledAsync();
        host.Account.Result = new(false, null, "no credential");

        var (exit, output) = await host.RunAsync("status", "--check");

        Assert.Equal(1, exit);
        Assert.Contains("not signed in", output);
        Assert.Contains("fix: az login", output);
    }
}
