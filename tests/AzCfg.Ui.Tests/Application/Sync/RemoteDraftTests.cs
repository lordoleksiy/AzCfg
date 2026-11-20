using AzCfg.Core.Model;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Application.Sync;

public sealed class RemoteDraftTests
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";

    [Fact]
    public void ToString_PrintsEnvironmentAndCountsOnly()
    {
        var target = new LocalTargetBinding(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);
        var binding = new ProjectBinding("C:/src/Crm", "crm", target, target, new Dictionary<string, Uri>());
        var env = new ConfigEnvironment("dev", new Uri("https://dev.azconfig.io"), null, false);
        var draft = new RemoteDraft(binding, env, """{"Db":{"Host":"base-value"}}""", """{"Db":{"Host":"edited-value"}}""",
            new Dictionary<DraftKey, DraftSecret>
            {
                [new DraftKey(ConfigScope.Service, "Api:Key")] = new("crm--Api--Key", new SecretString(Sentinel)),
            });

        var text = draft.ToString();

        Assert.Equal("RemoteDraft { Environment = dev, Secrets = 1 }", text);
        Assert.DoesNotContain(Sentinel, text);
        Assert.DoesNotContain("value", text);
    }
}
