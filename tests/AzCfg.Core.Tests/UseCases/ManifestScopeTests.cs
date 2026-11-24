using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

/// <summary>
/// Which projects the nearest <c>azcfg.json</c> applies to (read through <c>project.shared</c>): the target's project folder is the
/// project folder, lies inside it (repository root), or contains it with no other <c>.csproj</c> in between.
/// </summary>
public sealed class ManifestScopeTests
{
    private const string Root = "/w";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static readonly ProjectInfo[] Projects =
    [
        new(Root, "/w/Orders/Orders.csproj", "Orders", false, null),
        new(Root, "/w/Orders/tests/Orders.Tests/Orders.Tests.csproj", "Orders.Tests", false, null),
        new(Root, "/w/Billing/Billing.csproj", "Billing", false, null),
    ];

    private static async Task<bool> SharedAsync(string manifestFolder, string targetProject, string projectPath)
    {
        var target = new ManifestTarget("user-secrets", targetProject);
        var manifests = new FakeProjectManifestStore();
        manifests.Files[Path.Combine(manifestFolder, "azcfg.json")] = new ProjectManifest(1, "orders", target, target, null);
        var csproj = Projects[0].CsprojPath;
        var state = new FakeUserStateStore
        {
            Profile = new UserProfile([]),
            Binding = new(projectPath, "orders", new(LocalTargetKind.UserSecrets, csproj, LocalFileFormat.NestedJson), new(LocalTargetKind.UserSecrets, csproj, LocalFileFormat.NestedJson), new Dictionary<string, Uri>()),
        };
        var service = new ProjectSettingsService(state, new FakeProjectDetector(Projects), [new FakeLocalTarget(LocalTargetKind.UserSecrets)], manifests);
        return (await service.GetAsync(projectPath, ProjectSettingKeys.Shared, Ct)).Value == "true";
    }

    [Theory]
    [InlineData(Root, "Orders/Orders.csproj", "/w/Orders", true)]                       // target folder == project folder
    [InlineData(Root, "Orders", "/w/Orders", true)]                                     // target named by folder
    [InlineData(Root, "Orders/Orders.csproj", "/w", true)]                              // repository root: target inside the project path
    [InlineData(Root, "Orders/Orders.csproj", "/w/Orders/Orders.csproj", true)]         // a .csproj as the project path
    [InlineData(Root, "Orders/Orders.csproj", "/w/Order", false)]                       // prefix sibling
    [InlineData(Root, "Orders/Orders.csproj", "/w/Orders2", false)]                     // prefix sibling
    [InlineData(Root, "Orders/Orders.csproj", "/w/Billing", false)]                     // monorepo root manifest of another service
    [InlineData("/w/Orders", ".", "/w/Orders/Properties", true)]                        // subfolder of the target project, no csproj of its own
    [InlineData("/w/Orders", ".", "/w/Orders/tests/Orders.Tests", false)]               // nested project with its own csproj
    [InlineData("/w/Orders", ".", "/w/Orders/tests/Orders.Tests/Sub", false)]           // inside the nested project
    [InlineData("/w/Orders", ".", "/w/Orders/tests", true)]                             // a plain folder above the nested project
    public async Task AppliesOnlyToItsOwnProject(string manifestFolder, string targetProject, string projectPath, bool expected)
    {
        Assert.Equal(expected, await SharedAsync(manifestFolder, targetProject, projectPath));
    }

    [Fact]
    public async Task ProjectFolderCase_FollowsTheFileSystem()
    {
        Assert.Equal(OperatingSystem.IsWindows(), await SharedAsync(Root, "Orders/Orders.csproj", "/w/orders"));
    }

    [Fact]
    public async Task AbsoluteTargetOnAnotherDrive_NeverApplies()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // drive letters only exist on Windows
        }

        var otherDrive = Path.GetPathRoot(Path.GetFullPath(Root))!.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ? "Y:/other/X.csproj" : "Z:/other/X.csproj";
        Assert.False(await SharedAsync(Root, otherDrive, "/w/Orders"));
        Assert.False(await SharedAsync(Root, otherDrive, "/w"));
    }
}
