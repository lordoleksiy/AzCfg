using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class ConflictTests
{
    private const string SecretText = "super-secret-value";

    [Fact]
    public void JsonSerialized_NeverContainsSecretValue()
    {
        // Fingerprint is derived from the real secret text via the real fingerprinter, so this exercises
        // the actual value flowing in, not a hand-picked stand-in string.
        var fingerprinter = new HmacSecretFingerprinter(Enumerable.Repeat((byte)1, 32).ToArray());
        var fingerprint = fingerprinter.Fingerprint(new SecretString(SecretText));

        var secretItem = new StateItem(
            "ConnectionStrings:Crm",
            ConfigScope.Service,
            new StateValue.Secret(new Uri("https://vault1.vault.azure.net/secrets/crm"), fingerprint));

        var conflict = new Conflict("ConnectionStrings:Crm", Base: null, Local: secretItem, Remote: secretItem);

        var json = JsonSerializer.Serialize(conflict);

        Assert.DoesNotContain(SecretText, json);
    }
}
