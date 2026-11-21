using System.Text.Json.Nodes;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Tests.Application.Configuration;

public sealed class LocalSettingsPullTests
{
    [Fact]
    public async Task Pull_LocalSettingsValues_WritesOnlyValuesAndKeepsOtherSections()
    {
        using var world = new ConfigurationWorld(valuesKind: LocalTargetKind.LocalSettings);
        await File.WriteAllTextAsync(world.ValuesFile, """{ "IsEncrypted": true, "Values": { "Stale:Key": "x" }, "Host": { "CORS": "*" } }""");
        world.SeedRemote(ConfigurationWorld.Dev, "crm:Orders:Limit", "150");

        await world.PullAsync(ConfigurationWorld.Dev);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(world.ValuesFile))!.AsObject();
        Assert.Equal("150", root["Values"]!["Orders:Limit"]!.GetValue<string>());
        Assert.Equal("*", root["Host"]!["CORS"]!.GetValue<string>());
        Assert.True(root["IsEncrypted"]!.GetValue<bool>());
    }
}
