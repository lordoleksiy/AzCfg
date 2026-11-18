using AzCfg.Core.Model;
using AzCfg.Infrastructure.Local;
using Microsoft.Extensions.Logging;

namespace AzCfg.Infrastructure.Tests.Local;

public sealed class WorkspaceScannerTests : IDisposable
{
    private const string Csproj = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "azcfg-tests", Guid.NewGuid().ToString("N"));
    private readonly WorkspaceScanner _scanner;
    private readonly WarningCountingLogger _logger = new();

    public WorkspaceScannerTests()
    {
        Directory.CreateDirectory(_root);
        _scanner = new WorkspaceScanner(new CsprojDetector(), _logger);
    }

    [Fact]
    public async Task ScanAsync_SlnSlnxLooseProjectAndSkippedFolders_ReturnsExpectedStructure()
    {
        Write("src/A/A.csproj", Csproj);
        Write("src/B/B.csproj", Csproj);
        Write("Loose/Loose.csproj", Csproj);
        Write("node_modules/x/x.csproj", Csproj);
        Write(".git/x/x.csproj", Csproj);
        Write("Old.sln", Sln("src\\A\\A.csproj", "src\\B\\B.csproj"));
        Write("New.slnx", "<Solution><Project Path=\"src/A/A.csproj\" /><Project Path=\"docs/readme.shproj\" /></Solution>");
        Write("node_modules/y/Skipped.sln", Sln("x.csproj"));

        var scan = await _scanner.ScanAsync(_root, CancellationToken.None);

        Assert.Equal(_root, scan.RootPath);
        Assert.Equal(["New", "Old"], scan.Solutions.Select(s => s.Name));
        var slnx = scan.Solutions[0];
        Assert.Equal(SolutionFormat.Slnx, slnx.Format);
        Assert.Equal([Full("src/A/A.csproj")], slnx.ProjectPaths);
        var sln = scan.Solutions[1];
        Assert.Equal(SolutionFormat.Sln, sln.Format);
        Assert.Equal([Full("src/A/A.csproj"), Full("src/B/B.csproj")], sln.ProjectPaths);
        Assert.Equal([Full("Loose/Loose.csproj"), Full("src/A/A.csproj"), Full("src/B/B.csproj")], scan.Projects.Select(p => p.Project.CsprojPath));
    }

    [Fact]
    public async Task ScanAsync_BrokenSolution_IsSkippedAndLogged()
    {
        Write("A/A.csproj", Csproj);
        Write("Good.sln", Sln("A\\A.csproj"));
        Write("Broken.sln", "this is not a solution \0\0 <<<");
        Write("Broken.slnx", "<Solution><Project");

        var scan = await _scanner.ScanAsync(_root, CancellationToken.None);

        Assert.Equal(["Good"], scan.Solutions.Select(s => s.Name));
        Assert.Single(scan.Projects);
        Assert.Equal(2, _logger.Warnings);
    }

    [Fact]
    public async Task ScanAsync_ConfigFiles_AreClassified()
    {
        Write("Api/Api.csproj", Csproj);
        Write("Api/appsettings.json", "{}");
        Write("Api/appsettings.Development.json", "{}");
        Write("Api/appsettings.Local.json", "{}");
        Write("Api/local.settings.json", "{}");
        Write("Api/other.json", "{}");

        var files = (await _scanner.ScanAsync(_root, CancellationToken.None)).Projects.Single().ConfigFiles;

        Assert.Equal(
            [
                (ConfigFileKind.AppSettings, null),
                (ConfigFileKind.AppSettingsEnvironment, "Development"),
                (ConfigFileKind.AppSettingsLocal, null),
                (ConfigFileKind.LocalSettings, null),
            ],
            files.Select(f => (f.Kind, f.EnvironmentName)));
        Assert.All(files, f => Assert.True(f.Exists));
    }

    [Fact]
    public async Task ScanAsync_UserSecretsId_AddsUserSecretsFileWithExistsFlag()
    {
        var id = "azcfg-scan-" + Guid.NewGuid().ToString("N");
        Write("Api/Api.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UserSecretsId>{id}</UserSecretsId></PropertyGroup></Project>");

        var file = (await _scanner.ScanAsync(_root, CancellationToken.None)).Projects.Single().ConfigFiles.Single();

        Assert.Equal(ConfigFileKind.UserSecrets, file.Kind);
        Assert.False(file.Exists);
        Assert.Contains(id, file.Path);
        Assert.Null(file.EnvironmentName);
    }

    [Fact]
    public async Task ScanAsync_NoUserSecretsId_NoUserSecretsFile()
    {
        Write("Api/Api.csproj", Csproj);

        var files = (await _scanner.ScanAsync(_root, CancellationToken.None)).Projects.Single().ConfigFiles;

        Assert.Empty(files);
    }

    [Fact]
    public async Task ScanAsync_CancelledToken_Throws()
    {
        Write("A/A.csproj", Csproj);
        Write("A.sln", Sln("A\\A.csproj"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _scanner.ScanAsync(_root, cts.Token));
    }

    [Fact]
    public async Task ScanAsync_MissingRoot_ReturnsEmptyScan()
    {
        var scan = await _scanner.ScanAsync(Path.Combine(_root, "missing"), CancellationToken.None);

        Assert.Empty(scan.Solutions);
        Assert.Empty(scan.Projects);
    }

    private static string Sln(params string[] projects) =>
        "\nMicrosoft Visual Studio Solution File, Format Version 12.00\n"
        + string.Concat(projects.Select((p, i) =>
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"P{i}\", \"{p}\", \"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}\"\nEndProject\n"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Full(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content)
    {
        var path = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class WarningCountingLogger : ILogger<WorkspaceScanner>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
            }
        }
    }
}
