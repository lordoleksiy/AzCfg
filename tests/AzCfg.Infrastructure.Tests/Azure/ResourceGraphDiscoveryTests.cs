using System.Text.Json;
using AzCfg.Infrastructure.Azure;

namespace AzCfg.Infrastructure.Tests.Azure;

public sealed class ResourceGraphDiscoveryTests
{
    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void MapRow_MapsAllFields()
    {
        var row = Row("""
            {"id":"/subscriptions/s1/resourceGroups/rg1/providers/Microsoft.AppConfiguration/configurationStores/cfg-dev",
             "name":"cfg-dev","endpoint":"https://cfg-dev.azconfig.io","subscriptionId":"s1","resourceGroup":"rg1",
             "tags":{"env":"dev","team":"core"}}
            """);

        var resource = ResourceGraphDiscovery.MapRow(row);

        Assert.Equal("/subscriptions/s1/resourceGroups/rg1/providers/Microsoft.AppConfiguration/configurationStores/cfg-dev", resource.Id);
        Assert.Equal("cfg-dev", resource.Name);
        Assert.Equal(new Uri("https://cfg-dev.azconfig.io"), resource.Endpoint);
        Assert.Equal("s1", resource.SubscriptionId);
        Assert.Equal("rg1", resource.ResourceGroup);
        Assert.Equal(new Dictionary<string, string> { ["env"] = "dev", ["team"] = "core" }, resource.Tags);
    }

    [Theory]
    [InlineData("""{"id":"i","name":"n","endpoint":"https://n.vault.azure.net/","subscriptionId":"s","resourceGroup":"r"}""")]
    [InlineData("""{"id":"i","name":"n","endpoint":"https://n.vault.azure.net/","subscriptionId":"s","resourceGroup":"r","tags":null}""")]
    [InlineData("""{"id":"i","name":"n","endpoint":"https://n.vault.azure.net/","subscriptionId":"s","resourceGroup":"r","tags":{}}""")]
    public void MapRow_MissingOrEmptyTags_YieldsEmptyDictionary(string json) =>
        Assert.Empty(ResourceGraphDiscovery.MapRow(Row(json)).Tags);

    [Fact]
    public void MapRows_MapsEveryRowInOrder()
    {
        var rows = Row("""
            [{"id":"1","name":"a","endpoint":"https://a.azconfig.io","subscriptionId":"s","resourceGroup":"r"},
             {"id":"2","name":"b","endpoint":"https://b.azconfig.io","subscriptionId":"s","resourceGroup":"r"}]
            """);

        Assert.Equal(["a", "b"], ResourceGraphDiscovery.MapRows(rows).Select(r => r.Name));
    }
}
