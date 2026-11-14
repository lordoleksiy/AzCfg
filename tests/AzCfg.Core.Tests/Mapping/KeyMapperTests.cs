using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Mapping;

public class KeyMapperTests
{
    // ToStoreKey

    [Theory]
    [InlineData("crm-api-service", "ConnectionStrings:Crm", "crm-api-service:ConnectionStrings:Crm")]
    [InlineData("svc", "Key", "svc:Key")]
    public void ToStoreKey_ServiceScope_PrefixesWithService(string service, string path, string expected)
    {
        Assert.Equal(expected, KeyMapper.ToStoreKey(ConfigScope.Service, service, path));
    }

    [Theory]
    [InlineData("crm-api-service")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a:b")]
    public void ToStoreKey_GlobalScope_PrefixesWithGlobal_AndIgnoresService(string? service)
    {
        Assert.Equal("global:Logging:Level", KeyMapper.ToStoreKey(ConfigScope.Global, service!, "Logging:Level"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToStoreKey_BlankPath_Throws(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.ToStoreKey(ConfigScope.Service, "svc", path!));
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.ToStoreKey(ConfigScope.Global, "svc", path!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a:b")]
    [InlineData(":")]
    public void ToStoreKey_ServiceScope_InvalidService_Throws(string? service)
    {
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.ToStoreKey(ConfigScope.Service, service!, "Key"));
    }

    // TryParseStoreKey

    [Fact]
    public void TryParseStoreKey_GlobalKey_ParsesAsGlobal()
    {
        var ok = KeyMapper.TryParseStoreKey("global:Logging:Level", "svc", out var scope, out var path);

        Assert.True(ok);
        Assert.Equal(ConfigScope.Global, scope);
        Assert.Equal("Logging:Level", path);
    }

    [Fact]
    public void TryParseStoreKey_ServiceKey_ParsesAsService()
    {
        var ok = KeyMapper.TryParseStoreKey("svc:Key", "svc", out var scope, out var path);

        Assert.True(ok);
        Assert.Equal(ConfigScope.Service, scope);
        Assert.Equal("Key", path);
    }

    [Fact]
    public void TryParseStoreKey_NestedPath_KeepsColons()
    {
        var ok = KeyMapper.TryParseStoreKey("svc:A:B", "svc", out var scope, out var path);

        Assert.True(ok);
        Assert.Equal(ConfigScope.Service, scope);
        Assert.Equal("A:B", path);
    }

    [Theory]
    [InlineData("other:Key")]
    [InlineData("svc2:Key")]
    [InlineData("svcKey")]
    [InlineData("Key")]
    [InlineData("")]
    public void TryParseStoreKey_ForeignOrMalformedKey_ReturnsFalseWithEmptyPath(string storeKey)
    {
        var ok = KeyMapper.TryParseStoreKey(storeKey, "svc", out _, out var path);

        Assert.False(ok);
        Assert.Equal("", path);
    }

    [Theory]
    [InlineData("svc:")]
    [InlineData("global:")]
    public void TryParseStoreKey_EmptyRemainder_ReturnsFalse(string storeKey)
    {
        var ok = KeyMapper.TryParseStoreKey(storeKey, "svc", out _, out var path);

        Assert.False(ok);
        Assert.Equal("", path);
    }

    [Theory]
    [InlineData("SVC:Key")]
    [InlineData("Svc:Key")]
    [InlineData("GLOBAL:Key")]
    [InlineData("Global:Key")]
    public void TryParseStoreKey_IsCaseSensitive(string storeKey)
    {
        var ok = KeyMapper.TryParseStoreKey(storeKey, "svc", out _, out var path);

        Assert.False(ok);
        Assert.Equal("", path);
    }

    [Fact]
    public void TryParseStoreKey_ServiceNamedGlobal_ParsesAsGlobal()
    {
        var ok = KeyMapper.TryParseStoreKey("global:x", "global", out var scope, out var path);

        Assert.True(ok);
        Assert.Equal(ConfigScope.Global, scope);
        Assert.Equal("x", path);
    }

    [Fact]
    public void ToStoreKey_ServiceNamedGlobal_WithGlobalScope_Throws()
    {
        Assert.Throws<ArgumentException>(() => KeyMapper.ToStoreKey(ConfigScope.Service, "global", "x"));
        Assert.Throws<ArgumentException>(() => KeyMapper.ToStoreKey(ConfigScope.Service, "global", "x", hasGlobalScope: true));
    }

    [Fact]
    public void ToStoreKey_ServiceNamedGlobal_WithoutGlobalScope_IsAllowed()
    {
        Assert.Equal("global:x", KeyMapper.ToStoreKey(ConfigScope.Service, "global", "x", hasGlobalScope: false));
    }

    [Fact]
    public void ToStoreKey_ServiceNameCheckIsCaseSensitive_WithGlobalScope()
    {
        Assert.Equal("Global:x", KeyMapper.ToStoreKey(ConfigScope.Service, "Global", "x"));
    }

    [Fact]
    public void ToStoreKey_GlobalScope_WithoutGlobalScope_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => KeyMapper.ToStoreKey(ConfigScope.Global, "svc", "x", hasGlobalScope: false));
    }

    [Fact]
    public void TryParseStoreKey_WithoutGlobalScope_ServiceNamedGlobal_ParsesAsService()
    {
        var ok = KeyMapper.TryParseStoreKey("global:x", "global", out var scope, out var path, hasGlobalScope: false);

        Assert.True(ok);
        Assert.Equal(ConfigScope.Service, scope);
        Assert.Equal("x", path);
    }

    [Fact]
    public void TryParseStoreKey_WithoutGlobalScope_GlobalKeyOfOtherService_IsForeign()
    {
        var ok = KeyMapper.TryParseStoreKey("global:x", "svc", out _, out var path, hasGlobalScope: false);

        Assert.False(ok);
        Assert.Equal("", path);
    }

    [Fact]
    public void TryParseStoreKey_WithoutGlobalScope_StillParsesOwnServiceKeys()
    {
        var ok = KeyMapper.TryParseStoreKey("svc:A:B", "svc", out var scope, out var path, hasGlobalScope: false);

        Assert.True(ok);
        Assert.Equal(ConfigScope.Service, scope);
        Assert.Equal("A:B", path);
    }

    [Theory]
    [InlineData(ConfigScope.Global)]
    [InlineData(ConfigScope.Service)]
    public void ToStoreKey_ThenTryParse_RoundTrips(ConfigScope expectedScope)
    {
        var key = KeyMapper.ToStoreKey(expectedScope, "svc", "A:B:C");

        var ok = KeyMapper.TryParseStoreKey(key, "svc", out var scope, out var path);

        Assert.True(ok);
        Assert.Equal(expectedScope, scope);
        Assert.Equal("A:B:C", path);
    }

    // ToKeyVaultSecretName

    [Theory]
    [InlineData("crm-api-service:ConnectionStrings:Crm", "crm-api-service--ConnectionStrings--Crm")]
    [InlineData("a:b", "a--b")]
    [InlineData("NoColons", "NoColons")]
    [InlineData("a::b", "a----b")]
    public void ToKeyVaultSecretName_ReplacesColonsWithDoubleDash(string key, string expected)
    {
        Assert.Equal(expected, KeyMapper.ToKeyVaultSecretName(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ToKeyVaultSecretName_Blank_Throws(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.ToKeyVaultSecretName(key!));
    }

    // Local settings

    [Theory]
    [InlineData("Dataverse:ConnString", new[] { "Dataverse", "ConnString" })]
    [InlineData("Simple", new[] { "Simple" })]
    [InlineData("A:B:C", new[] { "A", "B", "C" })]
    public void ToLocalJsonPath_SplitsPathIntoNestedObjectSegments(string path, string[] expected)
    {
        Assert.Equal(expected, KeyMapper.ToLocalJsonPath(path));
    }

    [Theory]
    [InlineData(new[] { "Dataverse", "ConnString" }, "Dataverse:ConnString")]
    [InlineData(new[] { "Simple" }, "Simple")]
    [InlineData(new[] { "A", "B", "C" }, "A:B:C")]
    public void FromLocalJsonPath_JoinsSegmentsWithColon(string[] segments, string expected)
    {
        Assert.Equal(expected, KeyMapper.FromLocalJsonPath(segments));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A:B")]
    [InlineData("Dataverse:ConnString:Primary")]
    public void LocalJsonPath_RoundTrips(string path)
    {
        Assert.Equal(path, KeyMapper.FromLocalJsonPath(KeyMapper.ToLocalJsonPath(path)));
    }

    [Theory]
    [InlineData("global", ConfigScope.Global)]
    [InlineData("svc", ConfigScope.Service)]
    public void StoreKey_ToLocalJsonPath_DropsScopePrefix(string prefix, ConfigScope expectedScope)
    {
        var storeKey = $"{prefix}:Dataverse:ConnString";

        Assert.True(KeyMapper.TryParseStoreKey(storeKey, "svc", out var scope, out var path));
        Assert.Equal(expectedScope, scope);
        Assert.Equal(new[] { "Dataverse", "ConnString" }, KeyMapper.ToLocalJsonPath(path));
    }

    [Theory]
    [InlineData(ConfigScope.Global, "global:Dataverse:ConnString")]
    [InlineData(ConfigScope.Service, "svc:Dataverse:ConnString")]
    public void LocalJsonPath_ToStoreKey_AddsScopePrefix(ConfigScope scope, string expected)
    {
        var path = KeyMapper.FromLocalJsonPath(new[] { "Dataverse", "ConnString" });

        Assert.Equal(expected, KeyMapper.ToStoreKey(scope, "svc", path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A::B")]
    [InlineData(":A")]
    [InlineData("A:")]
    public void ToLocalJsonPath_InvalidPath_Throws(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.ToLocalJsonPath(value!));
    }

    [Fact]
    public void FromLocalJsonPath_InvalidSegments_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.FromLocalJsonPath(null!));
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.FromLocalJsonPath(Array.Empty<string>()));
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.FromLocalJsonPath(new[] { "A", "" }));
        Assert.ThrowsAny<ArgumentException>(() => KeyMapper.FromLocalJsonPath(new[] { "A:B" }));
    }
}
