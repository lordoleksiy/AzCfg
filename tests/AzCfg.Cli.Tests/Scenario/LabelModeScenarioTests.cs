using AzCfg.Core.Model;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// One shared store with a label per environment (ADR 0008): the environment's label wins over unlabeled defaults, and a change to a
/// key that exists only unlabeled adds an override with the environment's label instead of modifying the shared setting.
/// </summary>
public sealed class LabelModeScenarioTests
{
    private const string Store = "https://shared.azconfig.io";

    [Fact]
    public async Task Label_per_environment_reads_env_label_and_overrides_shared_keys()
    {
        var host = new ScenarioHost();
        var dbShared = host.AppConfig.Set(Store, "orders:Db:Host", "shared-db");
        var dbDev = host.AppConfig.Set(Store, "orders:Db:Host", "dev-db", "Development");
        var dbProd = host.AppConfig.Set(Store, "orders:Db:Host", "prod-db", "Production");
        var onlyShared = host.AppConfig.Set(Store, "orders:Only:Shared", "s");
        var logging = host.AppConfig.Set(Store, "global:Logging:Level", "Info");

        // init: labels mode, one environment per label found in the store.
        var (exit, output) = await host.RunAsync("init", "--yes", "--mode", "labels", "--store", Store);
        Assert.Equal(0, exit);
        var profile = host.State.Profile!;
        Assert.Equal((EnvironmentMode.LabelPerEnvironment, (string?)null), (profile.Mode, profile.DefaultLabel));
        Assert.Equal(
            [new ConfigEnvironment("dev", new Uri(Store), "Development", false), new ConfigEnvironment("prod", new Uri(Store), "Production", true)],
            profile.Environments);
        Assert.Contains("Mode: labels", output);

        // use dev: the Development copy wins over the unlabeled one, unlabeled-only keys are shared defaults, no warnings.
        (exit, output) = await host.RunAsync("use", "dev", "--yes", "--json");
        Assert.Equal(0, exit);
        var pull = ScenarioJson.Parse(output);
        Assert.Equal(("dev", 3, 0, false), (pull.GetProperty("environment").GetString(), pull.GetProperty("valuesWritten").GetInt32(), pull.GetProperty("secretsWritten").GetInt32(), pull.GetProperty("blocked").GetBoolean()));
        Assert.Empty(ScenarioJson.Warnings(pull));
        Assert.Equal(("orders", "dev"), (host.Binding!.Service, host.Binding.ActiveEnvironment));
        Assert.Equal(
            new Dictionary<string, string> { ["Db:Host"] = "dev-db", ["Logging:Level"] = "Info", ["Only:Shared"] = "s" },
            host.Files.Values());

        host.Files.SetLocal("Only:Shared", "mine");

        // plan: an override Add with the environment's label, never an update of the unlabeled setting.
        (exit, output) = await host.RunAsync("plan", "--json");
        Assert.Equal(0, exit);
        var plan = ScenarioJson.Parse(output);
        Assert.Equal([("add", "service", "Only:Shared", "Development", null)], ScenarioJson.Changes(plan));
        Assert.Equal([("labelOverride", "Only:Shared")], ScenarioJson.Warnings(plan));

        (exit, output) = await host.RunAsync("plan");
        Assert.Equal(0, exit);
        ScenarioOutput.AssertLine(output, "Add", "Service", "Only:Shared", "Development");
        ScenarioOutput.AssertLine(output, "Warning: 'Only:Shared' exists only under no label; adding it with label 'Development' instead of changing it.");

        // push: the override is added; the unlabeled 's' and the other labels stay exactly as they were.
        (exit, output) = await host.RunAsync("push", "--yes", "--json");
        Assert.Equal(0, exit);
        ScenarioJson.AssertApplied(output, 1);
        Assert.Equal(onlyShared, host.AppConfig.Get(Store, "orders:Only:Shared"));
        Assert.Equal("mine", host.AppConfig.Get(Store, "orders:Only:Shared", "Development")!.Text);
        Assert.Equal([dbShared, dbDev, dbProd], host.AppConfig.Copies(Store, "orders:Db:Host"));
        Assert.Equal([logging], host.AppConfig.Copies(Store, "global:Logging:Level"));

        // plan again: the override is now the environment's own setting, nothing left to do.
        (exit, output) = await host.RunAsync("plan", "--json");
        Assert.Equal(0, exit);
        plan = ScenarioJson.Parse(output);
        Assert.Empty(ScenarioJson.Changes(plan));
        Assert.Empty(ScenarioJson.Warnings(plan));

        // config: the saved settings show the mode and can be changed without re-running init.
        (exit, output) = await host.RunAsync("config", "list", "--json");
        Assert.Equal(0, exit);
        var settings = ScenarioJson.Parse(output).EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!, s => s.GetProperty("value").GetString());
        Assert.Equal("labels", settings["mode"]);
        Assert.Null(settings["defaultLabel"]);
        Assert.Equal(("Development", Store, "false"), (settings["env.dev.label"], settings["env.dev.endpoint"], settings["env.dev.protected"]));
        Assert.Equal(("Production", "true"), (settings["env.prod.label"], settings["env.prod.protected"]));

        (exit, output) = await host.RunAsync("config", "set", "env.dev.label", "Development2");
        Assert.Equal(0, exit);
        Assert.Contains("env.dev.label = Development2", output);

        (exit, output) = await host.RunAsync("config", "get", "env.dev.label", "--json");
        Assert.Equal(0, exit);
        var setting = ScenarioJson.Parse(output);
        Assert.Equal(("env.dev.label", "Development2"), (setting.GetProperty("key").GetString(), setting.GetProperty("value").GetString()));
        Assert.Equal("Development2", host.State.Profile!.Environments[0].Label);
    }
}
