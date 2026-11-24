using AzCfg.Core.Manifest;

namespace AzCfg.Core.Tests.Manifest;

public class ManifestSerializerTests
{
    private const string Sample = """
        // azcfg.json — git-tracked, value-free
        {
          "schemaVersion": 1,
          "service": "orders-api",
          /* targets */
          "values":  { "target": "appsettings-local", "project": "src/Orders.Api/Orders.Api.csproj", },
          "secrets": { "target": "user-secrets",      "project": "src/Orders.Api/Orders.Api.csproj" },
          "vaults":  { "dev": "https://orders-dev-kv.vault.azure.net/", }, // optional
        }
        """;

    private static ProjectManifest Manifest(IReadOnlyDictionary<string, Uri>? vaults = null) => new(
        1,
        "orders-api",
        new ManifestTarget("appsettings-local", "src/Orders.Api/Orders.Api.csproj"),
        new ManifestTarget("user-secrets", "src/Orders.Api/Orders.Api.csproj"),
        vaults);

    [Fact]
    public void Deserialize_AcceptsCommentsAndTrailingCommas()
    {
        var manifest = ManifestSerializer.Deserialize(Sample);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("orders-api", manifest.Service);
        Assert.Equal(new ManifestTarget("appsettings-local", "src/Orders.Api/Orders.Api.csproj"), manifest.Values);
        Assert.Equal(new ManifestTarget("user-secrets", "src/Orders.Api/Orders.Api.csproj"), manifest.Secrets);
        Assert.NotNull(manifest.Vaults);
        Assert.Equal(new Uri("https://orders-dev-kv.vault.azure.net/"), manifest.Vaults["dev"]);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = Manifest(new Dictionary<string, Uri>
        {
            ["dev"] = new("https://orders-dev-kv.vault.azure.net/"),
            ["prod"] = new("https://orders-prod-kv.vault.azure.net/"),
        });

        var json = ManifestSerializer.Serialize(original);
        var copy = ManifestSerializer.Deserialize(json);

        Assert.Equal(original.SchemaVersion, copy.SchemaVersion);
        Assert.Equal(original.Service, copy.Service);
        Assert.Equal(original.Values, copy.Values);
        Assert.Equal(original.Secrets, copy.Secrets);
        Assert.Equal(original.Vaults, copy.Vaults);
    }

    [Fact]
    public void RoundTrip_WithoutVaults_OmitsVaults()
    {
        var json = ManifestSerializer.Serialize(Manifest());

        Assert.DoesNotContain("vaults", json, StringComparison.Ordinal);
        Assert.Null(ManifestSerializer.Deserialize(json).Vaults);
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var json = ManifestSerializer.Serialize(Manifest());

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"service\": \"orders-api\"", json, StringComparison.Ordinal);
        Assert.Contains("\"target\": \"user-secrets\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_InvalidManifest_Throws()
    {
        var ex = Assert.Throws<ManifestException>(() =>
            ManifestSerializer.Serialize(Manifest() with { Service = "" }));

        Assert.Equal(ManifestErrorKind.Schema, ex.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"schemaVersion\": 1, ")]
    [InlineData("not json")]
    public void Deserialize_MalformedJson_IsUnreadable(string json)
    {
        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Equal(ManifestErrorKind.Unreadable, ex.Kind);
    }

    [Fact]
    public void Deserialize_RootNotObject_IsSchemaError()
    {
        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize("[1]"));

        Assert.Equal(ManifestErrorKind.Schema, ex.Kind);
    }

    [Fact]
    public void Deserialize_CanaryValue_IsRejectedNamingTheField()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "service": "orders-api",
              "values":  { "target": "appsettings-local", "project": "a.csproj", "value": "canary-7f3a" },
              "secrets": { "target": "user-secrets", "project": "a.csproj" }
            }
            """;

        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Equal(ManifestErrorKind.Schema, ex.Kind);
        var error = Assert.Single(ex.Errors);
        Assert.Equal("values.value", error.Field);
        Assert.Contains("values.value", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("canary-7f3a", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("value")]
    [InlineData("Service")]
    public void Deserialize_UnknownTopLevelField_IsSchemaError(string field)
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "service": "orders-api",
              "values":  { "target": "appsettings-local", "project": "a.csproj" },
              "secrets": { "target": "user-secrets", "project": "a.csproj" },
              "{{field}}": "canary-7f3a"
            }
            """;

        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Equal(ManifestErrorKind.Schema, ex.Kind);
        Assert.Contains(ex.Errors, e => e.Field == field);
        Assert.DoesNotContain("canary-7f3a", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_MissingRequiredFields_ReportsEach()
    {
        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize("{ \"schemaVersion\": 1 }"));

        Assert.Equal(ManifestErrorKind.Schema, ex.Kind);
        Assert.Contains(ex.Errors, e => e.Field == "service");
        Assert.Contains(ex.Errors, e => e.Field == "values");
        Assert.Contains(ex.Errors, e => e.Field == "secrets");
    }

    [Fact]
    public void Deserialize_WrongType_IsSchemaError()
    {
        const string json = """
            {
              "schemaVersion": "1",
              "service": "orders-api",
              "values":  { "target": "appsettings-local", "project": "a.csproj" },
              "secrets": { "target": "user-secrets", "project": "a.csproj" }
            }
            """;

        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Equal(ManifestErrorKind.Schema, ex.Kind);
        Assert.Contains(ex.Errors, e => e.Field == "schemaVersion");
    }

    [Fact]
    public void Deserialize_DuplicateField_IsSchemaError()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "service": "orders-api",
              "service": "billing-api",
              "values":  { "target": "appsettings-local", "project": "a.csproj" },
              "secrets": { "target": "user-secrets", "project": "a.csproj" }
            }
            """;

        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Contains(ex.Errors, e => e.Field == "service");
    }

    [Fact]
    public void Deserialize_NewerSchemaVersion_IsUnsupportedVersionWithUpgradeHint()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "service": "orders-api",
              "values":  { "target": "appsettings-local", "project": "a.csproj" },
              "secrets": { "target": "user-secrets", "project": "a.csproj" },
              "futureField": { "x": 1 }
            }
            """;

        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Equal(ManifestErrorKind.UnsupportedVersion, ex.Kind);
        var error = Assert.Single(ex.Errors);
        Assert.Equal("schemaVersion", error.Field);
        Assert.Contains("update azcfg", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_InvalidVaultUri_IsSchemaErrorNamingTheEnvironment()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "service": "orders-api",
              "values":  { "target": "appsettings-local", "project": "a.csproj" },
              "secrets": { "target": "user-secrets", "project": "a.csproj" },
              "vaults":  { "dev": "not a uri" }
            }
            """;

        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Equal(ManifestErrorKind.Schema, ex.Kind);
        Assert.Contains(ex.Errors, e => e.Field == "vaults.dev");
    }

    [Fact]
    public void Deserialize_RunsValidatorRules()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "service": "orders-api",
              "values":  { "target": "appsettings.json", "project": "a.csproj" },
              "secrets": { "target": "user-secrets", "project": "../outside/a.csproj" }
            }
            """;

        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize(json));

        Assert.Contains(ex.Errors, e => e.Field == "values.target");
        Assert.Contains(ex.Errors, e => e.Field == "secrets.project");
    }

    [Fact]
    public void ManifestException_WithFilePath_KeepsErrorsAndNamesThePath()
    {
        var ex = Assert.Throws<ManifestException>(() => ManifestSerializer.Deserialize("not json"));

        var withPath = ex.WithFilePath("/repo/azcfg.json");

        Assert.Equal("/repo/azcfg.json", withPath.FilePath);
        Assert.Equal(ex.Kind, withPath.Kind);
        Assert.Equal(ex.Errors, withPath.Errors);
        Assert.Contains("/repo/azcfg.json", withPath.Message, StringComparison.Ordinal);
        Assert.Same(ex, withPath.InnerException);
    }
}
