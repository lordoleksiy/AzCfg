using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class ConflictTests
{
    private const string SecretText = "super-secret-value";

    [Fact]
    public void JsonSerialized_NeverContainsSecretValue()
    {
        var secretItem = new StateItem(
            "ConnectionStrings:Crm",
            ConfigScope.Service,
            new StateValue.Secret(new Uri("https://vault1.vault.azure.net/secrets/crm"), "fingerprint-abc"));

        var conflict = new Conflict("ConnectionStrings:Crm", Base: null, Local: secretItem, Remote: secretItem);

        var json = JsonSerializer.Serialize(conflict);

        Assert.DoesNotContain(SecretText, json);
    }
}
