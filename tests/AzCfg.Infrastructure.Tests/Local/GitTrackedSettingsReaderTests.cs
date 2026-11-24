using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Infrastructure.Local;

namespace AzCfg.Infrastructure.Tests.Local;

public sealed class GitTrackedSettingsReaderTests : IDisposable
{
    private const string Canary = "AZCFG-CANARY-tracked-s3cret";
    private readonly string _root = Directory.CreateTempSubdirectory("azcfg-tracked-settings-tests-").FullName;
    private readonly GitTrackedSettingsReader _reader = new();

    public void Dispose()
    {
        try
        {
            // git marks its object files read-only on Windows; clear that before recursive delete.
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public async Task ReadKeysAsync_TrackedFile_FlattensKeysWithHasValue()
    {
        InitRepo();
        Write("Orders/appsettings.json", $$"""
            {
              // comment
              "Db": { "Password": "{{Canary}}", "Empty": "", "Null": null, "Port": 5432 },
              "Hosts": [ "a" ],
              "PasswordRequireDigit": true,
              "MaxTokens": 4000,
            }
            """);
        RunGit("add", ".");

        var keys = await _reader.ReadKeysAsync(_root, CancellationToken.None);

        Assert.Equal(
            [
                new("Orders/appsettings.json", "Db:Password", true),
                new("Orders/appsettings.json", "Db:Empty", false),
                new("Orders/appsettings.json", "Db:Null", false),
                new("Orders/appsettings.json", "Db:Port", false),
                new("Orders/appsettings.json", "Hosts:0", true),
                new("Orders/appsettings.json", "PasswordRequireDigit", false),
                new("Orders/appsettings.json", "MaxTokens", false),
            ],
            keys);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(keys));
    }

    [Theory]
    [InlineData("#{DbPassword}#")]
    [InlineData("${DB_PASSWORD}")]
    [InlineData("__DbPassword__")]
    [InlineData("{DbPassword}")]
    [InlineData("   ")]
    public async Task ReadKeysAsync_Placeholder_HasNoValue(string placeholder)
    {
        InitRepo();
        Write("appsettings.json", JsonSerializer.Serialize(new Dictionary<string, string> { ["Password"] = placeholder }));
        RunGit("add", ".");

        var keys = await _reader.ReadKeysAsync(_root, CancellationToken.None);

        Assert.Equal([new TrackedSettingKey("appsettings.json", "Password", false)], keys);
    }

    [Theory]
    [InlineData("{p@ss}")]
    [InlineData("{\"key\":\"abc123\",\"region\":\"eu\"}")]
    [InlineData("${not an identifier}")]
    [InlineData("__")]
    public async Task ReadKeysAsync_BracedValueThatIsNotAPlaceholder_HasValue(string value)
    {
        InitRepo();
        Write("appsettings.json", JsonSerializer.Serialize(new Dictionary<string, string> { ["ApiKey"] = value }));
        RunGit("add", ".");

        var keys = await _reader.ReadKeysAsync(_root, CancellationToken.None);

        Assert.Equal([new TrackedSettingKey("appsettings.json", "ApiKey", true)], keys);
    }

    [Fact]
    public async Task ReadKeysAsync_NonAsciiFolder_ListsUnquotedPath()
    {
        InitRepo();
        Write("Замовлення/appsettings.json", """{ "Api": { "Token": "t" } }""");
        RunGit("add", ".");

        var keys = await _reader.ReadKeysAsync(_root, CancellationToken.None);

        Assert.Equal([new TrackedSettingKey("Замовлення/appsettings.json", "Api:Token", true)], keys);
    }

    [Fact]
    public async Task ReadKeysAsync_RepoPathIsSubfolder_ListsOnlyItsFilesRelativeToIt()
    {
        InitRepo();
        Write("appsettings.json", """{ "Root": { "Secret": "s" } }""");
        Write("services/Orders/appsettings.json", """{ "Api": { "Token": "t" } }""");
        RunGit("add", ".");

        var keys = await _reader.ReadKeysAsync(Path.Combine(_root, "services"), CancellationToken.None);

        Assert.Equal([new TrackedSettingKey("Orders/appsettings.json", "Api:Token", true)], keys);
    }

    [Fact]
    public async Task ReadKeysAsync_UntrackedOrOtherJson_Skipped()
    {
        InitRepo();
        Write("appsettings.Development.json", """{ "Api": { "Token": "t" } }""");
        Write("other.json", """{ "Secret": "s" }""");
        Write("appsettings.local.json", """{ "Secret": "s" }""");
        RunGit("add", "appsettings.Development.json", "other.json");

        var keys = await _reader.ReadKeysAsync(_root, CancellationToken.None);

        Assert.Equal([new TrackedSettingKey("appsettings.Development.json", "Api:Token", true)], keys);
    }

    [Fact]
    public async Task ReadKeysAsync_InvalidJson_Skipped()
    {
        InitRepo();
        Write("appsettings.json", "{ not json");
        RunGit("add", ".");

        Assert.Empty(await _reader.ReadKeysAsync(_root, CancellationToken.None));
    }

    [Fact]
    public async Task ReadKeysAsync_OutsideGit_Empty()
    {
        Write("appsettings.json", """{ "Db": { "Password": "p" } }""");

        Assert.Empty(await _reader.ReadKeysAsync(_root, CancellationToken.None));
    }

    [Fact]
    public async Task ReadKeysAsync_MissingFolder_Empty() =>
        Assert.Empty(await _reader.ReadKeysAsync(Path.Combine(_root, "missing"), CancellationToken.None));

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void InitRepo() => RunGit("init", "-q");

    private void RunGit(params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = _root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
        }
    }
}
