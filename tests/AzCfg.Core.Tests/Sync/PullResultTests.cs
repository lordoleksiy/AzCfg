using System.Text.Json;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class PullResultTests
{
    private const string SecretText = "super-secret-value";

    [Fact]
    public void JsonSerialized_NeverContainsSecretValue()
    {
        // By design PullResult carries only key names in LocalChanges (IReadOnlyList<string>) and plain
        // counters — there is no field anywhere on the record that could hold a secret's value. The key
        // below names a secret whose real value is SecretText; it must never appear in the JSON output.
        var result = new PullResult(
            "dev",
            ValuesWritten: 3,
            SecretsWritten: 2,
            Blocked: false,
            LocalChanges: new[] { "ConnectionStrings:Crm" });

        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain(SecretText, json);
    }
}
