using Azure.Data.AppConfiguration;
using AzCfg.Core.Model;
using AzCfg.Infrastructure.Azure;

namespace AzCfg.Infrastructure.Tests.Azure;

public sealed class AppConfigStoreTests
{
    private static readonly Uri SecretUri = new("https://kv.vault.azure.net/secrets/Crm--Conn/abc");

    [Theory]
    [InlineData("crm:Db:Host", ConfigScope.Service, "Db:Host")]
    [InlineData("global:Logging:Level", ConfigScope.Global, "Logging:Level")]
    public void ToEntry_PlainSetting_MapsScopeKeyAndValue(string storeKey, ConfigScope scope, string key) =>
        Assert.Equal(
            new ConfigEntry(key, scope, new ConfigValue.Plain("v")),
            AppConfigStore.ToEntry(new ConfigurationSetting(storeKey, "v"), "crm"));

    [Fact]
    public void ToEntry_NullValue_MapsToEmptyString() =>
        Assert.Equal(new ConfigValue.Plain(""), AppConfigStore.ToEntry(new ConfigurationSetting("crm:A", null), "crm")!.Value);

    [Fact]
    public void ToEntry_KeyVaultReference_MapsToKeyVaultRef() =>
        Assert.Equal(
            new ConfigValue.KeyVaultRef(SecretUri),
            AppConfigStore.ToEntry(new SecretReferenceConfigurationSetting("crm:Conn", SecretUri), "crm")!.Value);

    [Theory]
    [InlineData("billing:A")]
    [InlineData("crm:")]
    [InlineData("NoPrefix")]
    public void ToEntry_ForeignOrMalformedKey_ReturnsNull(string storeKey) =>
        Assert.Null(AppConfigStore.ToEntry(new ConfigurationSetting(storeKey, "v"), "crm"));

    [Fact]
    public void ToEntry_CarriesETag()
    {
        var setting = ConfigurationModelFactory.ConfigurationSetting("crm:A", "v", eTag: new global::Azure.ETag("\"tag-1\""));

        Assert.Equal("\"tag-1\"", AppConfigStore.ToEntry(setting, "crm")!.ETag);
    }

    [Theory]
    [InlineData("svc:A", "svc")]
    [InlineData("global:A", "")]
    [InlineData("noColon", "")]
    [InlineData(":A", "")]
    [InlineData("svc:A,svc:B,other:C,global:D,noColon", "svc,other")]
    public void ToServicePrefixes_ReturnsUniqueServicePrefixes(string keys, string expected) =>
        Assert.Equal(
            expected.Split(',', StringSplitOptions.RemoveEmptyEntries),
            AppConfigStore.ToServicePrefixes(keys.Split(',')));
}
