using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Infrastructure.Azure;
using Azure.Data.AppConfiguration;

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
    [InlineData("Development", "Development")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ToEntry_PlainSetting_CarriesLabel(string? settingLabel, string? expected)
    {
        var setting = ConfigurationModelFactory.ConfigurationSetting("crm:A", "v", settingLabel);

        Assert.Equal(expected, AppConfigStore.ToEntry(setting, "crm")!.Label);
    }

    [Fact]
    public void ToEntry_KeyVaultReference_CarriesLabel()
    {
        var setting = ConfigurationModelFactory.SecretReferenceConfigurationSetting("crm:Conn", SecretUri, "Development", default);

        Assert.Equal("Development", AppConfigStore.ToEntry(setting, "crm")!.Label);
    }

    [Theory]
    [InlineData(null, "global:*")]
    [InlineData("dev", null)]
    public void CreateSelector_NoEnvLabel_DoesNotFilterByLabel(string? label, string? keyFilter)
    {
        var selector = AppConfigStore.CreateSelector(keyFilter, label);

        Assert.Equal(label, selector.LabelFilter);
        Assert.Equal(keyFilter, selector.KeyFilter);
    }

    [Fact]
    public void ReadLabels_StoreMode_ReadsAllLabelsInOneQuery() =>
        Assert.Equal(new string?[] { null }, AppConfigStore.ReadLabels(Env(label: null)));

    [Fact]
    public void ReadLabels_LabelMode_ReadsUnlabeledThenEnvironmentLabel() =>
        Assert.Equal(new string?[] { "\0", "dev" }, AppConfigStore.ReadLabels(Env(label: "dev")));

    private static ConfigEnvironment Env(string? label) =>
        new("dev", new Uri("https://appcs.azconfig.io"), label, IsProtected: false);

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
        ChangeKind kind, ConfigValue value, string? beforeETag = null, ConfigScope scope = ConfigScope.Service,
        string? label = null) =>
        new(
            kind,
            beforeETag is null ? null : new ConfigEntry("Db:Host", scope, value, beforeETag, label),
            kind == ChangeKind.Delete ? null : new ConfigEntry("Db:Host", scope, value, Label: label));

    [Theory]
    [InlineData(ChangeKind.Add, null, ConfigScope.Service, "crm:Db:Host", "dev")]
    [InlineData(ChangeKind.Update, "\"e1\"", ConfigScope.Service, "crm:Db:Host", null)]
    [InlineData(ChangeKind.Update, "\"e1\"", ConfigScope.Global, "global:Db:Host", "dev")]
    public void ToSetting_PlainValue_MapsKeyLabelValueAndETag(
        ChangeKind kind, string? eTag, ConfigScope scope, string storeKey, string? label)
    {
        var setting = AppConfigStore.ToSetting(MakeChange(kind, new ConfigValue.Plain("v"), eTag, scope, label), "crm");

        Assert.Equal(storeKey, setting.Key);
        Assert.Equal("v", setting.Value);
        Assert.Equal(label, setting.Label);
        Assert.Equal(eTag, setting.ETag == default ? null : setting.ETag.ToString());
    }

    [Fact]
    public void ToSetting_Delete_UsesBeforeEntry()
    {
        var change = new Change(ChangeKind.Delete, new ConfigEntry("Db:Host", ConfigScope.Service, new ConfigValue.Plain("v"), "\"e1\""), null);

        var setting = AppConfigStore.ToSetting(change, "crm");

        Assert.Equal("crm:Db:Host", setting.Key);
        Assert.Equal("\"e1\"", setting.ETag.ToString());
    }

    [Fact]
    public void ToSetting_Add_WritesAfterLabel()
    {
        var change = new Change(
            ChangeKind.Add, null, new ConfigEntry("Db:Host", ConfigScope.Service, new ConfigValue.Plain("v"), Label: "Development"));

        Assert.Equal("Development", AppConfigStore.ToSetting(change, "crm").Label);
    }

    [Fact]
    public void ToSetting_Delete_TargetsBeforeLabel()
    {
        var change = new Change(
            ChangeKind.Delete, new ConfigEntry("Db:Host", ConfigScope.Service, new ConfigValue.Plain("v"), "\"e1\"", "Legacy"), null);

        var setting = AppConfigStore.ToSetting(change, "crm");

        Assert.Equal("Legacy", setting.Label);
        Assert.Equal("\"e1\"", setting.ETag.ToString());
    }

    [Fact]
    public void ToSetting_Update_KeepsAfterLabelAndBeforeETag()
    {
        var change = new Change(
            ChangeKind.Update,
            new ConfigEntry("Db:Host", ConfigScope.Service, new ConfigValue.Plain("old"), "\"e1\"", "Legacy"),
            new ConfigEntry("Db:Host", ConfigScope.Service, new ConfigValue.Plain("new"), Label: "Development"));

        var setting = AppConfigStore.ToSetting(change, "crm");

        Assert.Equal("Development", setting.Label);
        Assert.Equal("new", setting.Value);
        Assert.Equal("\"e1\"", setting.ETag.ToString());
    }

    [Fact]
    public void ToSetting_KeyVaultValue_WritesEntryLabel()
    {
        var change = new Change(
            ChangeKind.Add, null, new ConfigEntry("Conn", ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri), Label: "Development"));

        Assert.Equal("Development", AppConfigStore.ToSetting(change, "crm").Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToSetting_KeyVaultValue_BuildsSecretReferenceWithoutSecretText(bool resolved)
    {
        ConfigValue value = resolved
            ? new ConfigValue.ResolvedSecret(SecretUri, new SecretString("top-secret"))
            : new ConfigValue.KeyVaultRef(SecretUri);

        var setting = AppConfigStore.ToSetting(MakeChange(ChangeKind.Add, value), "crm");

        var reference = Assert.IsType<SecretReferenceConfigurationSetting>(setting);
        Assert.Equal(SecretUri, reference.SecretId);
        Assert.DoesNotContain("top-secret", reference.Value);
    }

    [Fact]
    public void ToSetting_CreateSecret_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            AppConfigStore.ToSetting(MakeChange(ChangeKind.CreateSecret, new ConfigValue.KeyVaultRef(SecretUri)), "crm"));
}
