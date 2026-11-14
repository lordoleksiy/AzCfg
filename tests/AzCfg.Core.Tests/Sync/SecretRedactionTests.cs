using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class SecretRedactionTests
{
    private const string SecretText = "super-secret-value";

    [Fact]
    public void Change_ToString_NeverContainsSecretText()
    {
        var change = CreateChangeWithSecret();

        Assert.DoesNotContain(SecretText, change.ToString());
    }

    [Fact]
    public void SyncPlan_ToString_NeverContainsSecretText()
    {
        var plan = CreatePlanWithSecret();

        Assert.DoesNotContain(SecretText, plan.ToString());
    }

    [Fact]
    public void SyncPlan_JsonSerialized_NeverContainsSecretText()
    {
        var plan = CreatePlanWithSecret();

        var json = JsonSerializer.Serialize(plan);

        Assert.DoesNotContain(SecretText, json);
        Assert.Contains("***", json);
    }

    private static Change CreateChangeWithSecret()
    {
        var entry = new ConfigEntry(
            "ConnectionStrings:Crm",
            ConfigScope.Service,
            new ConfigValue.ResolvedSecret(new Uri("https://vault1.vault.azure.net/secrets/crm"), new SecretString(SecretText)));

        return new Change(ChangeKind.CreateSecret, Before: null, After: entry, SecretName: "crm", SecretValue: new SecretString(SecretText));
    }

    private static SyncPlan CreatePlanWithSecret()
    {
        var binding = new ProjectBinding(
            "repo",
            "svc",
            new LocalTargetBinding(LocalTargetKind.UserSecrets, "proj.csproj", LocalFileFormat.NestedJson),
            new LocalTargetBinding(LocalTargetKind.UserSecrets, "proj.csproj", LocalFileFormat.NestedJson),
            new Dictionary<string, Uri>());

        var env = new ConfigEnvironment("dev", new Uri("https://cfg.azconfig.io"), null, IsProtected: false);

        return new SyncPlan(binding, env, new[] { CreateChangeWithSecret() }, Array.Empty<Conflict>(), false, false);
    }
}
