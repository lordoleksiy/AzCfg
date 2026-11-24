using AzCfg.Core.Manifest;

namespace AzCfg.Core.Tests.Manifest;

public class ManifestValidatorTests
{
    private static ProjectManifest Valid() => new(
        ManifestValidator.SupportedSchemaVersion,
        "orders-api",
        new ManifestTarget("appsettings-local", "src/Orders.Api/Orders.Api.csproj"),
        new ManifestTarget("user-secrets", "src/Orders.Api/Orders.Api.csproj"),
        new Dictionary<string, Uri> { ["dev"] = new("https://orders-dev-kv.vault.azure.net/") });

    [Fact]
    public void Valid_HasNoErrors()
    {
        Assert.Empty(ManifestValidator.Validate(Valid()));
    }

    [Theory]
    [InlineData("user-secrets")]
    [InlineData("appsettings-local")]
    [InlineData("local-settings")]
    public void KnownTargets_AreAccepted(string target)
    {
        var manifest = Valid() with { Values = new(target, "a.csproj"), Secrets = new(target, "a.csproj") };

        Assert.Empty(ManifestValidator.Validate(manifest));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("UserSecrets")]
    [InlineData("")]
    public void UnknownTarget_IsError(string target)
    {
        var errors = ManifestValidator.Validate(Valid() with { Secrets = new(target, "a.csproj") });

        var error = Assert.Single(errors);
        Assert.Equal(ManifestErrorKind.Schema, error.Kind);
        Assert.Equal("secrets.target", error.Field);
    }

    [Fact]
    public void NewerSchemaVersion_IsUnsupportedVersion()
    {
        var errors = ManifestValidator.Validate(Valid() with { SchemaVersion = ManifestValidator.SupportedSchemaVersion + 1 });

        var error = Assert.Single(errors);
        Assert.Equal(ManifestErrorKind.UnsupportedVersion, error.Kind);
        Assert.Equal("schemaVersion", error.Field);
        Assert.Contains("update azcfg", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveSchemaVersion_IsSchemaError(int version)
    {
        var error = Assert.Single(ManifestValidator.Validate(Valid() with { SchemaVersion = version }));

        Assert.Equal(ManifestErrorKind.Schema, error.Kind);
        Assert.Equal("schemaVersion", error.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("orders:api")]
    [InlineData("global")]
    [InlineData("Global")]
    public void InvalidService_IsError(string service)
    {
        var error = Assert.Single(ManifestValidator.Validate(Valid() with { Service = service }));

        Assert.Equal("service", error.Field);
    }

    [Theory]
    [InlineData("a.csproj")]
    [InlineData("src/Orders.Api/Orders.Api.csproj")]
    [InlineData("src\\Orders.Api\\Orders.Api.csproj")]
    [InlineData("src/../Orders.Api/Orders.Api.csproj")]
    [InlineData("./src/Orders.Api")]
    public void RelativeProjectInsideRoot_IsAccepted(string project)
    {
        Assert.Empty(ManifestValidator.Validate(Valid() with { Values = new("appsettings-local", project) }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../a.csproj")]
    [InlineData("src/../../a.csproj")]
    [InlineData("..\\a.csproj")]
    [InlineData("/abs/a.csproj")]
    [InlineData("\\abs\\a.csproj")]
    [InlineData("C:\\repo\\a.csproj")]
    [InlineData("C:a.csproj")]
    [InlineData("\\\\server\\share\\a.csproj")]
    public void ProjectOutsideRootOrAbsolute_IsError(string project)
    {
        var error = Assert.Single(ManifestValidator.Validate(Valid() with { Values = new("appsettings-local", project) }));

        Assert.Equal(ManifestErrorKind.Schema, error.Kind);
        Assert.Equal("values.project", error.Field);
    }

    [Theory]
    [InlineData("http://orders-dev-kv.vault.azure.net/")]
    [InlineData("ftp://orders-dev-kv.vault.azure.net/")]
    public void NonHttpsVault_IsError(string uri)
    {
        var manifest = Valid() with { Vaults = new Dictionary<string, Uri> { ["dev"] = new(uri) } };

        var error = Assert.Single(ManifestValidator.Validate(manifest));

        Assert.Equal("vaults.dev", error.Field);
    }

    [Fact]
    public void RelativeVault_IsError()
    {
        var manifest = Valid() with { Vaults = new Dictionary<string, Uri> { ["dev"] = new("kv/dev", UriKind.Relative) } };

        Assert.Equal("vaults.dev", Assert.Single(ManifestValidator.Validate(manifest)).Field);
    }

    [Fact]
    public void BlankVaultEnvironment_IsError()
    {
        var manifest = Valid() with { Vaults = new Dictionary<string, Uri> { [" "] = new("https://kv.vault.azure.net/") } };

        Assert.StartsWith("vaults", Assert.Single(ManifestValidator.Validate(manifest)).Field, StringComparison.Ordinal);
    }

    [Fact]
    public void VaultEnvironmentsDifferingByCase_IsError()
    {
        var manifest = Valid() with
        {
            Vaults = new Dictionary<string, Uri>
            {
                ["dev"] = new("https://a.vault.azure.net/"),
                ["Dev"] = new("https://b.vault.azure.net/"),
            },
        };

        var error = Assert.Single(ManifestValidator.Validate(manifest));

        Assert.Equal(ManifestErrorKind.Schema, error.Kind);
    }

    [Fact]
    public void Equality_ComparesVaultsByContent()
    {
        var a = Valid() with
        {
            Vaults = new Dictionary<string, Uri> { ["dev"] = new("https://a.vault.azure.net/"), ["prod"] = new("https://p.vault.azure.net/") },
        };
        var b = Valid() with
        {
            Vaults = new Dictionary<string, Uri> { ["prod"] = new("https://p.vault.azure.net/"), ["dev"] = new("https://a.vault.azure.net/") },
        };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Vaults = new Dictionary<string, Uri> { ["dev"] = new("https://a.vault.azure.net/") } });
        Assert.NotEqual(a, b with { Vaults = new Dictionary<string, Uri> { ["dev"] = new("https://x.vault.azure.net/"), ["prod"] = new("https://p.vault.azure.net/") } });
        Assert.NotEqual(a, a with { Vaults = null });
        Assert.Equal(a with { Vaults = null }, b with { Vaults = null });
        Assert.NotEqual(a, a with { Service = "billing-api" });
    }

    [Fact]
    public void NullVaults_IsValid()
    {
        Assert.Empty(ManifestValidator.Validate(Valid() with { Vaults = null }));
    }

    [Fact]
    public void ReportsAllErrors()
    {
        var manifest = Valid() with
        {
            Service = "",
            Values = new("nope", "../x.csproj"),
        };

        var errors = ManifestValidator.Validate(manifest);

        Assert.Equal(["service", "values.target", "values.project"], errors.Select(e => e.Field));
    }
}
