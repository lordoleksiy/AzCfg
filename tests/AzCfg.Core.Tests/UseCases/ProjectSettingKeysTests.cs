using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class ProjectSettingKeysTests
{
    [Theory]
    [InlineData(LocalTargetKind.UserSecrets, "user-secrets:/repo/A/A.csproj")]
    [InlineData(LocalTargetKind.AppSettingsLocal, "appsettings-local:/repo/A/A.csproj")]
    [InlineData(LocalTargetKind.LocalSettings, "local-settings:/repo/A/A.csproj")]
    public void TargetValue_IsTargetNameAndProjectPath(LocalTargetKind kind, string expected) =>
        Assert.Equal(expected, ProjectSettingKeys.TargetValue(new LocalTargetBinding(kind, "/repo/A/A.csproj", kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson)));
}
