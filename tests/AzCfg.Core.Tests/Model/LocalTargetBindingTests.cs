using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Model;

public class LocalTargetBindingTests
{
    [Theory]
    [InlineData(LocalTargetKind.UserSecrets, LocalFileFormat.NestedJson)]
    [InlineData(LocalTargetKind.AppSettingsLocal, LocalFileFormat.NestedJson)]
    [InlineData(LocalTargetKind.LocalSettings, LocalFileFormat.FlatDictionary)]
    public void Constructor_ValidCombination_Succeeds(LocalTargetKind kind, LocalFileFormat format)
    {
        var binding = new LocalTargetBinding(kind, "src/Service/Service.csproj", format);

        Assert.Equal(kind, binding.Kind);
        Assert.Equal(format, binding.Format);
        Assert.Equal("src/Service/Service.csproj", binding.ProjectPath);
    }

    [Fact]
    public void Constructor_LocalSettingsWithNestedJson_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new LocalTargetBinding(LocalTargetKind.LocalSettings, "Service.csproj", LocalFileFormat.NestedJson));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankProjectPath_Throws(string? projectPath)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new LocalTargetBinding(LocalTargetKind.UserSecrets, projectPath!, LocalFileFormat.NestedJson));
    }

    [Fact]
    public void Constructor_UserSecretsWithFlatDictionary_Succeeds()
    {
        // Allowed by the contract; MVP never produces this combination, but it is not forbidden.
        var binding = new LocalTargetBinding(LocalTargetKind.UserSecrets, "Service.csproj", LocalFileFormat.FlatDictionary);

        Assert.Equal(LocalFileFormat.FlatDictionary, binding.Format);
    }

    [Fact]
    public void Constructor_AppSettingsLocalWithFlatDictionary_Succeeds()
    {
        var binding = new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, "Service.csproj", LocalFileFormat.FlatDictionary);

        Assert.Equal(LocalFileFormat.FlatDictionary, binding.Format);
    }
}
