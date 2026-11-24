using AzCfg.Core.Abstractions;
using AzCfg.Core.UseCases;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// Team onboarding through the shared <c>azcfg.json</c> (ADR 0012 amendment 2): developer A shares the binding with
/// <c>config set project.shared true</c>; developer B, with empty user state on the same repository, pulls without the wizard into the
/// same targets. The manifest holds names only, never values.
/// </summary>
public sealed class SharedManifestScenarioTests
{
    private const string Store = "https://orders-dev.azconfig.io";
    private const string Vault = "https://orders-dev-kv.vault.azure.net";

    [Fact]
    public async Task Developer_A_shares_the_binding_and_developer_B_pulls_without_the_wizard()
    {
        var a = new ScenarioHost();
        a.Discovery.AddStore("appcs-orders-dev", Store);
        a.AppConfig.Set(Store, "orders:Timeout", "30", "Development");
        a.AppConfig.Set(Store, "billing:Timeout", "99", "Development");
        var secret = a.Secrets.Add(Vault, "orders--Db--Password", "canary-s3cret");
        a.AppConfig.SetSecretReference(Store, "orders:Db:Password", secret, "Development");
        var manifestPath = Path.Combine(a.Repo, IProjectManifestStore.FileName);

        // Developer A: first pull binds via the non-interactive wizard (never writes azcfg.json), then shares explicitly.
        Assert.Equal(0, (await a.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await a.RunAsync("pull", "--yes", "--json")).Exit);
        Assert.Null(a.Manifests.Json(manifestPath));
        Assert.Equal(0, (await a.RunAsync("config", "set", "project.vault.dev", Vault)).Exit);

        var (exit, output) = await a.RunAsync("config", "set", "project.shared", "true", "--json");
        Assert.Equal(0, exit);
        var shared = ScenarioJson.Parse(output);
        Assert.Equal(("true", manifestPath, ProjectSettingKeys.SharedHint), (shared.GetProperty("value").GetString(), shared.GetProperty("source").GetString(), shared.GetProperty("hint").GetString()));
        var json = a.Manifests.Json(manifestPath)!;
        Assert.Contains("\"service\": \"orders\"", json);
        Assert.Contains("\"project\": \"Orders/Orders.csproj\"", json);
        Assert.DoesNotContain("canary", json);
        Assert.DoesNotContain("30", json);

        (_, output) = await a.RunAsync("config", "list");
        ScenarioOutput.AssertLine(output, "project.shared", $"true ({manifestPath})");

        // Developer B: same repository and Azure, empty user state.
        var b = new ScenarioHost(a);
        Assert.Null(b.Binding);
        Assert.Equal(0, (await b.RunAsync("init", "--yes", "--label", "Development")).Exit);
        (exit, output) = await b.RunAsync("pull", "--json");
        Assert.Equal(0, exit);
        var pull = ScenarioJson.Parse(output);
        Assert.Equal(("dev", 1, 1), (pull.GetProperty("environment").GetString(), pull.GetProperty("valuesWritten").GetInt32(), pull.GetProperty("secretsWritten").GetInt32()));
        Assert.DoesNotContain("canary", output);

        var (bindingA, bindingB) = (a.Binding!, b.Binding!);
        Assert.Equal(
            (bindingA.Service, bindingA.ValuesTarget, bindingA.SecretsTarget, bindingA.VaultByEnvironment["dev"]),
            (bindingB.Service, bindingB.ValuesTarget, bindingB.SecretsTarget, bindingB.VaultByEnvironment["dev"]));
        Assert.Equal("dev", bindingB.ActiveEnvironment);
        Assert.Equal(new Dictionary<string, string> { ["Timeout"] = "30", ["Db:Password"] = "canary-s3cret" }, b.Files.Values());

        (_, output) = await b.RunAsync("config", "get", "project.shared", "--json");
        Assert.Equal(manifestPath, ScenarioJson.Parse(output).GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_manifest_with_another_service_rebinds_and_clears_the_active_environment()
    {
        var a = new ScenarioHost();
        a.Discovery.AddStore("appcs-orders-dev", Store);
        a.AppConfig.Set(Store, "orders:Timeout", "30", "Development");
        a.AppConfig.Set(Store, "billing:Timeout", "99", "Development");
        Assert.Equal(0, (await a.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await a.RunAsync("pull", "--yes", "--json")).Exit);
        Assert.Equal(0, (await a.RunAsync("config", "set", "project.shared", "true")).Exit);

        // A teammate switches the shared service (e.g. committed azcfg.json); A's next command follows it.
        var b = new ScenarioHost(a);
        Assert.Equal(0, (await b.RunAsync("init", "--yes", "--label", "Development")).Exit);
        Assert.Equal(0, (await b.RunAsync("pull", "--yes", "--json")).Exit);
        var (exit, output) = await b.RunAsync("config", "set", "project.service", "billing");
        Assert.Equal(0, exit);
        Assert.Contains(ProjectSettingKeys.SharedHint, output);

        Assert.Equal(("orders", "dev"), (a.Binding!.Service, a.Binding.ActiveEnvironment));
        (exit, output) = await a.RunAsync("status", "--json");
        Assert.Equal(0, exit);
        Assert.Equal(("billing", (string?)null), (a.Binding!.Service, a.Binding.ActiveEnvironment));
        Assert.Null(a.State.Snapshot(a.Repo, "dev"));
    }

    [Fact]
    public async Task A_corrupted_manifest_fails_the_command_naming_the_file()
    {
        var host = new ScenarioHost();
        host.Discovery.AddStore("appcs-orders-dev", Store);
        host.AppConfig.Set(Store, "orders:Timeout", "30", "Development");
        Assert.Equal(0, (await host.RunAsync("init", "--yes", "--label", "Development")).Exit);
        var manifestPath = Path.Combine(host.Repo, IProjectManifestStore.FileName);
        host.Manifests.SetJson(manifestPath, """{ "schemaVersion": 1, "service": "orders", "value": "x" }""");

        var ex = await Assert.ThrowsAsync<AzCfg.Core.Manifest.ManifestException>(() => host.RunAsync("pull", "--yes", "--json"));

        Assert.Contains(manifestPath, ex.Message);
        Assert.Null(host.Binding);
        Assert.Equal(0, host.Files.Writes);
    }
}
