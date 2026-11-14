using System.Text.Json;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class PullResultTests
{
    private const string SecretText = "super-secret-value";

    [Fact]
    public void JsonSerialized_NeverContainsSecretValue()
    {
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
