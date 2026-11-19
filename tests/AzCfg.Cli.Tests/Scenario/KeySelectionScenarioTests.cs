namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// <c>--key</c> on <c>plan</c>/<c>push</c>/<c>pull</c> (ADR 0013): patterns select keys, and <c>--force</c> with <c>--key</c> resolves the
/// conflict of the selected keys only (push keeps local, pull keeps remote).
/// </summary>
public sealed class KeySelectionScenarioTests
{
    private const string Store = "https://orders-dev.azconfig.io";

    private static async Task<ScenarioHost> PulledWithConflictsAsync()
    {
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", Store);
        host.AppConfig.Set(Store, "orders:Db:Host", "dev-db", "Development");
        host.AppConfig.Set(Store, "orders:Timeout", "30", "Development");
        host.AppConfig.Set(Store, "orders:Feature:X", "true", "Development");
        Assert.Equal(0, (await host.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await host.RunAsync("pull", "--yes", "--json")).Exit);

        // Local edits.
        host.Files.SetLocal("Db:Host", "local-db");
        host.Files.SetLocal("Timeout", "1");
        host.Files.SetLocal("Feature:X", "false");
        host.Files.SetLocal("Other", "1");

        // Concurrent remote edits of the same two keys: Db:Host and Timeout now conflict.
        host.AppConfig.Set(Store, "orders:Db:Host", "remote-db", "Development");
        host.AppConfig.Set(Store, "orders:Timeout", "99", "Development");
        return host;
    }

    [Fact]
    public async Task Plan_and_push_touch_only_keys_matching_the_patterns()
    {
        var host = await PulledWithConflictsAsync();

        var (exit, output) = await host.RunAsync("plan", "--key", "Feature:*", "--key", "Other", "--json");
        Assert.Equal(0, exit);
        var plan = ScenarioJson.Parse(output);
        Assert.Equal([("update", "service", "Feature:X", "Development", "Development"), ("add", "service", "Other", "Development", null)], ScenarioJson.Changes(plan));
        Assert.Empty(plan.GetProperty("conflicts").EnumerateArray());

        (exit, output) = await host.RunAsync("push", "--key", "Feature:*", "--yes", "--json");
        Assert.Equal(0, exit);
        ScenarioJson.AssertApplied(output, 1);
        Assert.Equal("false", host.AppConfig.Get(Store, "orders:Feature:X", "Development")!.Text);
        Assert.Null(host.AppConfig.Get(Store, "orders:Other", "Development"));
        Assert.Equal("remote-db", host.AppConfig.Get(Store, "orders:Db:Host", "Development")!.Text);
    }

    [Fact]
    public async Task Conflict_blocks_the_selected_key_and_force_resolves_only_that_key()
    {
        var host = await PulledWithConflictsAsync();

        var (exit, output) = await host.RunAsync("push", "--key", "Db:*", "--yes", "--json");
        Assert.Equal(1, exit);
        Assert.Equal(["Db:Host"], ConflictKeys(output));

        (exit, output) = await host.RunAsync("push", "--key", "Db:*", "--force", "--yes", "--json");
        Assert.Equal(0, exit);
        ScenarioJson.AssertApplied(output, 1);
        Assert.Equal("local-db", host.AppConfig.Get(Store, "orders:Db:Host", "Development")!.Text);
        Assert.Equal("99", host.AppConfig.Get(Store, "orders:Timeout", "Development")!.Text);

        // The other conflict is untouched.
        (_, output) = await host.RunAsync("plan", "--json");
        Assert.Equal(["Timeout"], ConflictKeys(output));
    }

    [Fact]
    public async Task Pull_key_is_blocked_by_a_local_change_and_force_keeps_remote_for_that_key_only()
    {
        var host = await PulledWithConflictsAsync();

        var (exit, output) = await host.RunAsync("pull", "--key", "Timeout", "--json");
        Assert.Equal(1, exit);
        var blocked = ScenarioJson.Parse(output);
        Assert.True(blocked.GetProperty("blocked").GetBoolean());
        Assert.Equal(["Timeout"], ScenarioJson.Strings(blocked, "localChanges"));
        Assert.Equal("1", host.Files.Local("Timeout"));

        (exit, output) = await host.RunAsync("pull", "--key", "Timeout", "--force", "--json");
        Assert.Equal(0, exit);
        Assert.Equal(1, ScenarioJson.Parse(output).GetProperty("valuesWritten").GetInt32());
        Assert.Equal("99", host.Files.Local("Timeout"));
        Assert.Equal("local-db", host.Files.Local("Db:Host"));

        (_, output) = await host.RunAsync("plan", "--json");
        Assert.Equal(["Db:Host"], ConflictKeys(output));
    }

    [Fact]
    public async Task Pull_key_matching_nothing_is_an_error()
    {
        var host = await PulledWithConflictsAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync("pull", "--key", "Nope:*", "--json"));

        Assert.Contains("No key matches", ex.Message);
    }

    private static IReadOnlyList<string?> ConflictKeys(string output) =>
        [.. ScenarioJson.Parse(output).GetProperty("conflicts").EnumerateArray().Select(c => c.GetProperty("key").GetString())];
}
