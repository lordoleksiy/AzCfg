using AzCfg.Infrastructure.Local;

namespace AzCfg.Infrastructure.Tests.Local;

public sealed class CsprojDetectorTests : IDisposable
{
    private const string MinimalCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>";

    private readonly List<string> _tempDirs = [];
    private readonly CsprojDetector _detector = new();

    [Fact]
    public async Task DetectAsync_FindsCsprojFiles_AcrossSubdirectories()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Service.Core/Service.Core.csproj", MinimalCsproj);
        WriteCsproj(repo, "Service.Api/Service.Api.csproj", MinimalCsproj);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "Service.Core");
        Assert.Contains(result, p => p.Name == "Service.Api");
    }

    [Fact]
    public async Task DetectAsync_ExcludesBinAndObjDirectories()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Service/Service.csproj", MinimalCsproj);
        WriteCsproj(repo, "Service/bin/Debug/Service.csproj", MinimalCsproj);
        WriteCsproj(repo, "Service/obj/Service.csproj", MinimalCsproj);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Equal([Path.Combine(repo, "Service", "Service.csproj")], result.Select(p => p.CsprojPath));
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>", false)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web/9.0.0\"></Project>", false)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Worker\"></Project>", false)]
    [InlineData("<Project><Sdk Name=\"Microsoft.NET.Sdk.Web\" Version=\"9.0.0\" /></Project>", false)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>", false)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>winexe</OutputType></PropertyGroup></Project>", false)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AzureFunctionsVersion>v4</AzureFunctionsVersion></PropertyGroup></Project>", false)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>", true)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Library</OutputType></PropertyGroup></Project>", true)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /></ItemGroup></Project>", true)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><IsTestProject>true</IsTestProject></PropertyGroup></Project>", true)]
    public async Task DetectAsync_ClassifiesLibraries(string csproj, bool expectedIsLibrary)
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Service/Service.csproj", csproj);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Equal(expectedIsLibrary, result[0].IsLibrary);
    }

    [Fact]
    public async Task DetectAsync_UserSecretsIdPresent_ParsesIt()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Service/Service.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UserSecretsId>abc-123</UserSecretsId>
              </PropertyGroup>
            </Project>
            """);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Equal("abc-123", result[0].UserSecretsId);
    }

    [Fact]
    public async Task DetectAsync_UserSecretsIdAbsent_IsNull()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Service/Service.csproj", MinimalCsproj);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Null(result[0].UserSecretsId);
    }

    [Fact]
    public async Task DetectAsync_AzureFunctionsVersionPresent_SetsIsAzureFunctions()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Functions/Functions.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <AzureFunctionsVersion>v4</AzureFunctionsVersion>
              </PropertyGroup>
            </Project>
            """);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.True(result[0].IsAzureFunctions);
    }

    [Fact]
    public async Task DetectAsync_AzureFunctionsVersionAbsent_IsNotAzureFunctions()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Service/Service.csproj", MinimalCsproj);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.False(result[0].IsAzureFunctions);
    }

    [Fact]
    public async Task DetectAsync_SetsRepoPathAndCsprojPath_OnEveryProject()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "A/A.csproj", MinimalCsproj);
        WriteCsproj(repo, "B/B.csproj", MinimalCsproj);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.All(result, p => Assert.Equal(repo, p.RepoPath));
        Assert.Contains(result, p => p.CsprojPath == Path.Combine(repo, "A", "A.csproj"));
        Assert.Contains(result, p => p.CsprojPath == Path.Combine(repo, "B", "B.csproj"));
    }

    [Fact]
    public async Task DetectAsync_ReturnsProjects_OrderedByCsprojPathOrdinal()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "C/C.csproj", MinimalCsproj);
        WriteCsproj(repo, "A/A.csproj", MinimalCsproj);
        WriteCsproj(repo, "B/B.csproj", MinimalCsproj);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        var expected = result.Select(p => p.CsprojPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, result.Select(p => p.CsprojPath));
    }

    [Fact]
    public async Task DetectAsync_NonSdkCsprojWithMsBuildNamespace_ParsesUserSecretsIdAndAzureFunctionsVersion()
    {
        var repo = CreateRepo();
        WriteCsproj(repo, "Legacy/Legacy.csproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <UserSecretsId>legacy-456</UserSecretsId>
                <AzureFunctionsVersion>v4</AzureFunctionsVersion>
              </PropertyGroup>
            </Project>
            """);

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Equal("legacy-456", result[0].UserSecretsId);
        Assert.True(result[0].IsAzureFunctions);
    }

    [Fact]
    public async Task DetectAsync_NoCsprojFiles_ReturnsEmpty()
    {
        var repo = CreateRepo();

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectAsync_RepoPathDoesNotExist_ReturnsEmpty()
    {
        var repo = Path.Combine(Path.GetTempPath(), "azcfg-tests-missing-" + Guid.NewGuid().ToString("N"));

        var result = await _detector.DetectAsync(repo, CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectAsync_BlankRepoPath_Throws(string? repoPath)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _detector.DetectAsync(repoPath!, CancellationToken.None));
    }

    private string CreateRepo()
    {
        var path = Path.Combine(Path.GetTempPath(), "azcfg-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    private static void WriteCsproj(string repo, string relativePath, string content)
    {
        var fullPath = Path.Combine(repo, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
