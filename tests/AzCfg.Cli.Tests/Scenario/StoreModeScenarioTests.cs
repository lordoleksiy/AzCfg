using AzCfg.Core.Model;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// One store per environment (ADR 0008, issue #40): the environment is named <c>dev</c> but its keys carry the label
/// <c>Development</c>; labels never select the environment, duplicates are warnings, ignored keys stay local, push is an upsert.
/// </summary>
public sealed class StoreModeScenarioTests
{
    private const string Store = "https://orders-dev.azconfig.io";
    private const string Vault = "https://kv-orders-dev.vault.azure.net";
    private const string Secret = "dev-Pa55w0rd!";

    [Fact]
    public async Task Env_name_unlike_label_pulls_ignores_and_pushes_without_deleting()
    {
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", Store);
        host.AppConfig.Set(Store, "orders:Db:Host", "dev-db", "Development");
        host.AppConfig.Set(Store, "orders:Feature:X", "true", "Development");
        var timeoutDev = host.AppConfig.Set(Store, "orders:Timeout", "30", "Development");
        var timeoutUnlabeled = host.AppConfig.Set(Store, "orders:Timeout", "60");
        host.AppConfig.SetSecretReference(Store, "orders:Db:Password", host.Secrets.Add(Vault, "orders--Db--Password", Secret), "Development");
        host.AppConfig.Set(Store, "global:Logging:Level", "Information");
        var serilog = host.AppConfig.Set(Store, "global:Serilog:MinimumLevel", "Warning");
        var billing = host.AppConfig.Set(Store, "billing:Key", "x", "Development");

        // init: store mode, 'Development' as the label for new keys.
        var (exit, output) = await host.RunAsync("init", "--yes", "--label", "Development");
        Assert.Equal(0, exit);
        var profile = host.State.Profile!;
        Assert.Equal((EnvironmentMode.StorePerEnvironment, "Development"), (profile.Mode, profile.DefaultLabel));
        var dev = Assert.Single(profile.Environments);
        Assert.Equal(("dev", new Uri(Store), (string?)null, false), (dev.Name, dev.StoreEndpoint, dev.Label, dev.IsProtected));
        Assert.Contains("Mode: stores", output);

        // pull: binds 'orders' automatically and reads every label although 'dev' != 'Development'.
        (exit, output) = await host.RunAsync("pull", "--yes", "--json");
        Assert.Equal(0, exit);
        Assert.DoesNotContain(Secret, output);
        var pull = ScenarioJson.Parse(output);
        Assert.Equal("dev", pull.GetProperty("environment").GetString());
        Assert.Equal((5, 1, false), (pull.GetProperty("valuesWritten").GetInt32(), pull.GetProperty("secretsWritten").GetInt32(), pull.GetProperty("blocked").GetBoolean()));
        Assert.Equal([("duplicateLabels", "Timeout")], ScenarioJson.Warnings(pull));
        Assert.Empty(ScenarioJson.Strings(pull, "ignored"));
        Assert.Equal(("orders", "dev"), (host.Binding!.Service, host.Binding.ActiveEnvironment));
        Assert.Equal(new LocalTargetBinding(LocalTargetKind.UserSecrets, host.CsprojPath, LocalFileFormat.NestedJson), host.Binding.SecretsTarget);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Db:Host"] = "dev-db",
                ["Db:Password"] = Secret,
                ["Feature:X"] = "true",
                ["Logging:Level"] = "Information",
                ["Serilog:MinimumLevel"] = "Warning",
                ["Timeout"] = "30",
            },
            host.Files.Values());
        Assert.DoesNotContain(Secret, host.State.SnapshotJson(host.Repo, "dev")!);

        // ignore: global Serilog keys become local only.
        (exit, output) = await host.RunAsync("ignore", "add", "global:Serilog:*", "--json");
        Assert.Equal(0, exit);
        Assert.Equal(["global:Serilog:*"], ScenarioJson.Strings(ScenarioJson.Parse(output), "ignore"));
        Assert.Equal(["global:Serilog:*"], host.Binding!.Ignore!);

        host.Files.SetLocal("Db:Host", "localhost");
        host.Files.RemoveLocal("Feature:X");
        host.Files.SetLocal("NewKey", "1");
        host.Files.SetLocal("Serilog:MinimumLevel", "Debug");

        // plan: update keeps the setting's label, add gets the default label, ignored key absent, delete only warned.
        (exit, output) = await host.RunAsync("plan", "--json");
        Assert.Equal(0, exit);
        Assert.DoesNotContain(Secret, output);
        var plan = ScenarioJson.Parse(output);
        Assert.Equal(
            [("update", "service", "Db:Host", "Development", "Development"), ("add", "service", "NewKey", "Development", null)],
            ScenarioJson.Changes(plan));
        Assert.Empty(plan.GetProperty("conflicts").EnumerateArray());
        Assert.False(plan.GetProperty("touchesGlobal").GetBoolean());
        Assert.Equal([("duplicateLabels", "Timeout"), ("deleteSkipped", "Feature:X")], ScenarioJson.Warnings(plan));

        (exit, output) = await host.RunAsync("plan");
        Assert.Equal(0, exit);
        ScenarioOutput.AssertLine(output, "Change", "Scope", "Key", "Label");
        ScenarioOutput.AssertLine(output, "Update", "Service", "Db:Host", "Development");
        ScenarioOutput.AssertLine(output, "Add", "Service", "NewKey", "Development");
        ScenarioOutput.AssertLine(output, "Warning: 'Timeout' exists under labels (no label), 'Development'; using 'Development'.");
        ScenarioOutput.AssertLine(output, "Warning: 'Feature:X' was deleted locally; it is not deleted remotely (use --delete).");
        Assert.DoesNotContain("Serilog", output);

        // push (upsert): 2 changes, nothing deleted, no unlabeled duplicate created, ignored key untouched.
        (exit, output) = await host.RunAsync("push", "--yes", "--json");
        Assert.Equal(0, exit);
        Assert.DoesNotContain(Secret, output);
        ScenarioJson.AssertApplied(output, 2);
        Assert.Equal([("Development", "localhost")], Copies(host, "orders:Db:Host"));
        Assert.Equal([("Development", "1")], Copies(host, "orders:NewKey"));
        Assert.Equal([("Development", "true")], Copies(host, "orders:Feature:X"));
        Assert.Equal(serilog, host.AppConfig.Get(Store, "global:Serilog:MinimumLevel"));
        Assert.Empty(host.AppConfig.Copies(Store, "orders:Serilog:MinimumLevel"));
        Assert.Equal([timeoutUnlabeled, timeoutDev], host.AppConfig.Copies(Store, "orders:Timeout"));

        // push --delete: the local deletion reaches Azure.
        (exit, output) = await host.RunAsync("push", "--delete", "--yes", "--json");
        Assert.Equal(0, exit);
        ScenarioJson.AssertApplied(output, 1);
        Assert.Empty(host.AppConfig.Copies(Store, "orders:Feature:X"));
        Assert.Equal(serilog, host.AppConfig.Get(Store, "global:Serilog:MinimumLevel"));

        // pull: the ignored key keeps its local value; duplicates stay remote untouched (human run first, then the same pull as JSON).
        (exit, output) = await host.RunAsync("pull");
        Assert.Equal(0, exit);
        Assert.DoesNotContain(Secret, output);
        ScenarioOutput.AssertLine(output, "dev", "4", "1");
        ScenarioOutput.AssertLine(output, "Warning: 'Timeout' exists under labels");
        ScenarioOutput.AssertLine(output, "1 ignored key(s) kept local: Serilog:MinimumLevel");

        (exit, output) = await host.RunAsync("pull", "--json");
        Assert.Equal(0, exit);
        Assert.DoesNotContain(Secret, output);
        pull = ScenarioJson.Parse(output);
        Assert.Equal((4, 1, false), (pull.GetProperty("valuesWritten").GetInt32(), pull.GetProperty("secretsWritten").GetInt32(), pull.GetProperty("blocked").GetBoolean()));
        Assert.Equal(["Serilog:MinimumLevel"], ScenarioJson.Strings(pull, "ignored"));
        Assert.Equal([("duplicateLabels", "Timeout")], ScenarioJson.Warnings(pull));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Db:Host"] = "localhost",
                ["Db:Password"] = Secret,
                ["Logging:Level"] = "Information",
                ["NewKey"] = "1",
                ["Serilog:MinimumLevel"] = "Debug",
                ["Timeout"] = "30",
            },
            host.Files.Values());
        Assert.Equal([timeoutUnlabeled, timeoutDev], host.AppConfig.Copies(Store, "orders:Timeout"));
        Assert.Equal(serilog, host.AppConfig.Get(Store, "global:Serilog:MinimumLevel"));
        Assert.Equal(billing, host.AppConfig.Get(Store, "billing:Key", "Development"));
        var snapshot = host.State.SnapshotJson(host.Repo, "dev")!;
        Assert.DoesNotContain(Secret, snapshot);
        Assert.DoesNotContain("Serilog:MinimumLevel", host.State.Snapshot(host.Repo, "dev")!.Items.Keys);
    }

    private static IReadOnlyList<(string? Label, string? Value)> Copies(ScenarioHost host, string key) =>
        [.. host.AppConfig.Copies(Store, key).Select(s => (s.Label, s.Text))];
}
