using AzCfg.Core.Model;
using AzCfg.Infrastructure.Local;

namespace AzCfg.Infrastructure.Tests.Local;

public sealed class AppSettingsLocalTargetTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettingsLocalTarget _target = new();

    public AppSettingsLocalTargetTests()
    {
        _root = Directory.CreateTempSubdirectory("azcfg-appsettings-local-tests-").FullName;
    }

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
            // Best effort cleanup; a lingering file handle on Windows must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup; a lingering file handle on Windows must not fail the test run.
        }
    }

    [Fact]
    public void Kind_IsAppSettingsLocal()
    {
        Assert.Equal(LocalTargetKind.AppSettingsLocal, _target.Kind);
    }

    [Fact]
    public void CanHandle_AnyProject_ReturnsTrue()
    {
        var project = new ProjectInfo(_root, CsprojPath(), "Service", IsAzureFunctions: true, UserSecretsId: null);

        Assert.True(_target.CanHandle(project));
    }

    [Fact]
    public async Task ReadAsync_FileMissing_ReturnsEmptyList()
    {
        var values = await _target.ReadAsync(Binding(), CancellationToken.None);

        Assert.Empty(values);
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsNestedKeys()
    {
        var binding = Binding();
        var written = new[]
        {
            new LocalValue("ConnectionStrings:Crm", "crm-value"),
            new LocalValue("Logging:LogLevel:Default", "Information"),
            new LocalValue("Simple", "v"),
        };

        await _target.WriteAsync(binding, written, CancellationToken.None);
        var read = await _target.ReadAsync(binding, CancellationToken.None);

        Assert.Equal(
            written.Select(v => (v.Key, v.Value)).OrderBy(v => v.Key).ToArray(),
            read.Select(v => (v.Key, v.Value)).OrderBy(v => v.Key).ToArray());
    }

    [Fact]
    public async Task WriteAsync_WritesIndentedNestedJsonToFile()
    {
        var binding = Binding();

        await _target.WriteAsync(binding, [new LocalValue("A:B", "v")], CancellationToken.None);

        var text = await File.ReadAllTextAsync(FilePath());
        Assert.Equal("{\n  \"A\": {\n    \"B\": \"v\"\n  }\n}".ReplaceLineEndings(Environment.NewLine), text);
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsGlobalMarker()
    {
        var binding = Binding();
        LocalValue[] written = [new("Api:Timeout", "30"), new("Api:Url", "https://x", ConfigScope.Global), new("Logging:LogLevel", "Information", ConfigScope.Global)];

        await _target.WriteAsync(binding, written, CancellationToken.None);
        var read = await _target.ReadAsync(binding, CancellationToken.None);

        Assert.Contains("\"LogLevel\": \"Information\" //@global", await File.ReadAllTextAsync(FilePath()));
        Assert.Equal(written, read.OrderBy(v => v.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_HandWrittenMarkers_MarkKeysGlobalIgnoringCase()
    {
        await File.WriteAllTextAsync(FilePath(), """
            {
              "api": { "url": "u", /* @global */ "Timeout": "30" },
              "Items": [ "a", "b" // @global
              ]
            }
            """);

        var read = await _target.ReadAsync(Binding(), CancellationToken.None);

        Assert.Equal(
            [new("Items:0", "a"), new("Items:1", "b", ConfigScope.Global), new("api:Timeout", "30"), new("api:url", "u", ConfigScope.Global)],
            read.OrderBy(v => v.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WriteAsync_CalledTwice_FullyOverwritesPreviousContent()
    {
        var binding = Binding();
        await _target.WriteAsync(binding, [new LocalValue("Old", "1")], CancellationToken.None);

        await _target.WriteAsync(binding, [new LocalValue("New", "2")], CancellationToken.None);
        var read = await _target.ReadAsync(binding, CancellationToken.None);

        Assert.Equal([new LocalValue("New", "2")], read);
    }

    [Fact]
    public async Task InspectAsync_FileMissing_ExistsIsFalse()
    {
        var option = await _target.InspectAsync(Binding(), CancellationToken.None);

        Assert.False(option.Exists);
    }

    [Fact]
    public async Task InspectAsync_FileExists_ExistsIsTrue()
    {
        var binding = Binding();
        await _target.WriteAsync(binding, [new LocalValue("A", "1")], CancellationToken.None);

        var option = await _target.InspectAsync(binding, CancellationToken.None);

        Assert.True(option.Exists);
    }

    [Fact]
    public async Task InspectAsync_OutsideGitRepo_IsSafeForSecretsIsFalse()
    {
        var option = await _target.InspectAsync(Binding(), CancellationToken.None);

        Assert.False(option.IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_InGitRepoNotIgnored_IsSafeForSecretsIsFalse()
    {
        RunGit("init");

        var option = await _target.InspectAsync(Binding(), CancellationToken.None);

        Assert.False(option.IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_InGitRepoIgnoredAndUntracked_IsSafeForSecretsIsTrue()
    {
        RunGit("init");
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "appsettings.local.json\n");

        var option = await _target.InspectAsync(Binding(), CancellationToken.None);

        Assert.True(option.IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_InGitRepoIgnoredButTracked_IsSafeForSecretsIsFalse()
    {
        RunGit("init");
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "appsettings.local.json\n");
        await File.WriteAllTextAsync(FilePath(), "{}");
        RunGit("add", "-f", "appsettings.local.json");

        var option = await _target.InspectAsync(Binding(), CancellationToken.None);

        Assert.False(option.IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_RelativeProjectPathIgnoredAndUntracked_IsSafeForSecretsIsTrue()
    {
        // ProjectPath is relative (e.g. "src/Api/Api.csproj"), so GetFilePath() produces a relative path too;
        // git must still be asked about the full path, not one resolved against the file's own directory.
        RunGit("init");
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "appsettings.local.json\n");
        Directory.CreateDirectory(Path.Combine(_root, "src", "Api"));
        var relativeProjectPath = Path.Combine("src", "Api", "Api.csproj");
        var binding = new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, relativeProjectPath, LocalFileFormat.NestedJson);

        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _root;

            var option = await _target.InspectAsync(binding, CancellationToken.None);

            Assert.True(option.IsSafeForSecrets);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    private LocalTargetBinding Binding() =>
        new(LocalTargetKind.AppSettingsLocal, CsprojPath(), LocalFileFormat.NestedJson);

    private string CsprojPath() => Path.Combine(_root, "Service.csproj");

    private string FilePath() => Path.Combine(_root, "appsettings.local.json");

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
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
        }
    }
}
