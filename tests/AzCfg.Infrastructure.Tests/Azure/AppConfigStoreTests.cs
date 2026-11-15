using Azure.Data.AppConfiguration;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
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

    private static Change MakeChange(
        ChangeKind kind, ConfigValue value, string? beforeETag = null, ConfigScope scope = ConfigScope.Service) =>
        new(
            kind,
            beforeETag is null ? null : new ConfigEntry("Db:Host", scope, value, beforeETag),
            kind == ChangeKind.Delete ? null : new ConfigEntry("Db:Host", scope, value));

    [Theory]
    [InlineData(ChangeKind.Add, null, ConfigScope.Service, "crm:Db:Host", "dev")]
    [InlineData(ChangeKind.Update, "\"e1\"", ConfigScope.Service, "crm:Db:Host", null)]
    [InlineData(ChangeKind.Update, "\"e1\"", ConfigScope.Global, "global:Db:Host", "dev")]
    public void ToSetting_PlainValue_MapsKeyLabelValueAndETag(
        ChangeKind kind, string? eTag, ConfigScope scope, string storeKey, string? label)
    {
        var setting = AppConfigStore.ToSetting(MakeChange(kind, new ConfigValue.Plain("v"), eTag, scope), "crm", label);

        Assert.Equal(storeKey, setting.Key);
        Assert.Equal("v", setting.Value);
        Assert.Equal(label, setting.Label);
        Assert.Equal(eTag, setting.ETag == default ? null : setting.ETag.ToString());
    }

    [Fact]
    public void ToSetting_Delete_UsesBeforeEntry()
    {
        var change = new Change(ChangeKind.Delete, new ConfigEntry("Db:Host", ConfigScope.Service, new ConfigValue.Plain("v"), "\"e1\""), null);

        var setting = AppConfigStore.ToSetting(change, "crm", null);

        Assert.Equal("crm:Db:Host", setting.Key);
        Assert.Equal("\"e1\"", setting.ETag.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToSetting_KeyVaultValue_BuildsSecretReferenceWithoutSecretText(bool resolved)
    {
        ConfigValue value = resolved
            ? new ConfigValue.ResolvedSecret(SecretUri, new SecretString("top-secret"))
            : new ConfigValue.KeyVaultRef(SecretUri);

        var setting = AppConfigStore.ToSetting(MakeChange(ChangeKind.Add, value), "crm", null);

        var reference = Assert.IsType<SecretReferenceConfigurationSetting>(setting);
        Assert.Equal(SecretUri, reference.SecretId);
        Assert.DoesNotContain("top-secret", reference.Value);
    }

    [Fact]
    public void ToSetting_CreateSecret_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            AppConfigStore.ToSetting(MakeChange(ChangeKind.CreateSecret, new ConfigValue.KeyVaultRef(SecretUri)), "crm", null));
}
