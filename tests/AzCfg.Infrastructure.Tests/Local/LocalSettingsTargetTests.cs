using System.Text.Json.Nodes;
using AzCfg.Core.Model;
using AzCfg.Infrastructure.Local;

namespace AzCfg.Infrastructure.Tests.Local;

public sealed class LocalSettingsTargetTests : IDisposable
{
    private readonly string _root;
    private readonly LocalSettingsTarget _target = new();

    public LocalSettingsTargetTests()
    {
        _root = Directory.CreateTempSubdirectory("azcfg-local-settings-tests-").FullName;
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
    public void Kind_IsLocalSettings()
    {
        Assert.Equal(LocalTargetKind.LocalSettings, _target.Kind);
    }

    [Fact]
    public void CanHandle_AzureFunctionsProject_ReturnsTrue()
    {
        var project = new ProjectInfo(_root, CsprojPath(), "Func", IsAzureFunctions: true, UserSecretsId: null);

        Assert.True(_target.CanHandle(project));
    }

    [Fact]
    public void CanHandle_NonFunctionsApp_ReturnsFalse()
    {
        File.WriteAllText(FilePath(), "{}");
        var project = new ProjectInfo(_root, CsprojPath(), "Service", IsAzureFunctions: false, UserSecretsId: null);

        Assert.False(_target.CanHandle(project));
    }

    [Fact]
    public async Task ReadAsync_FileMissing_ReturnsEmptyList()
    {
        Assert.Empty(await _target.ReadAsync(Binding(), CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_ReadsOnlyValuesWithFlatKeysAsIs()
    {
        await File.WriteAllTextAsync(FilePath(), """
            {
              "IsEncrypted": false,
              "Values": { "AzureWebJobsStorage": "UseDevelopmentStorage=true", "Api:Url": "https://x" },
              "Host": { "LocalHttpPort": 7071 },
              "ConnectionStrings": { "Db": "db-value" }
            }
            """);

        var read = await _target.ReadAsync(Binding(), CancellationToken.None);

        Assert.Equal(
            [new("Api:Url", "https://x"), new("AzureWebJobsStorage", "UseDevelopmentStorage=true")],
            read.OrderBy(v => v.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_HandWrittenGlobalMarker_ReadsAsServiceScope()
    {
        await File.WriteAllTextAsync(FilePath(), """
            {
              "Values": {
                "Api:Url": "https://x" //@global
              }
            }
            """);

        var read = await _target.ReadAsync(Binding(), CancellationToken.None);

        Assert.Equal([new LocalValue("Api:Url", "https://x")], read);
    }

    [Fact]
    public async Task WriteAsync_FileMissing_CreatesFileWithIsEncryptedAndFlatValues()
    {
        await _target.WriteAsync(Binding(), [new("Api:Url", "https://x"), new("A", "1")], CancellationToken.None);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(FilePath()))!.AsObject();
        Assert.Equal(["IsEncrypted", "Values"], root.Select(p => p.Key));
        Assert.False(root["IsEncrypted"]!.GetValue<bool>());
        Assert.Equal(["A", "Api:Url"], root["Values"]!.AsObject().Select(p => p.Key));
        Assert.Equal("https://x", root["Values"]!["Api:Url"]!.GetValue<string>());
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_ReplacesValuesAndKeepsOtherSections()
    {
        await File.WriteAllTextAsync(FilePath(), """
            {
              "IsEncrypted": false,
              "Values": { "Old": "1" },
              "Host": { "LocalHttpPort": 7071, "CORS": "*" },
              "ConnectionStrings": { "Db": "db-value" }
            }
            """);

        await _target.WriteAsync(Binding(), [new("New", "2")], CancellationToken.None);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(FilePath()))!.AsObject();
        Assert.Equal(["IsEncrypted", "Values", "Host", "ConnectionStrings"], root.Select(p => p.Key));
        Assert.Equal(["New"], root["Values"]!.AsObject().Select(p => p.Key));
        Assert.Equal(7071, root["Host"]!["LocalHttpPort"]!.GetValue<int>());
        Assert.Equal("*", root["Host"]!["CORS"]!.GetValue<string>());
        Assert.Equal("db-value", root["ConnectionStrings"]!["Db"]!.GetValue<string>());
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsFlatKeys()
    {
        LocalValue[] written = [new("A", "1"), new("Api:Url", "https://x"), new("Logging:LogLevel:Default", "Information")];

        await _target.WriteAsync(Binding(), written, CancellationToken.None);
        var read = await _target.ReadAsync(Binding(), CancellationToken.None);

        Assert.Equal(written, read.OrderBy(v => v.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WriteAsync_GlobalValue_WritesNoMarkerAndReadsAsService()
    {
        await _target.WriteAsync(Binding(), [new("Api:Url", "https://x", ConfigScope.Global)], CancellationToken.None);

        Assert.DoesNotContain("@global", await File.ReadAllTextAsync(FilePath()));
        Assert.Equal([new LocalValue("Api:Url", "https://x")], await _target.ReadAsync(Binding(), CancellationToken.None));
    }

    [Fact]
    public async Task InspectAsync_FileMissing_ExistsIsFalse()
    {
        Assert.False((await _target.InspectAsync(Binding(), CancellationToken.None)).Exists);
    }

    [Fact]
    public async Task InspectAsync_FileExists_ExistsIsTrue()
    {
        await _target.WriteAsync(Binding(), [new("A", "1")], CancellationToken.None);

        Assert.True((await _target.InspectAsync(Binding(), CancellationToken.None)).Exists);
    }

    [Fact]
    public async Task InspectAsync_OutsideGitRepo_IsSafeForSecretsIsFalse()
    {
        Assert.False((await _target.InspectAsync(Binding(), CancellationToken.None)).IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_InGitRepoNotIgnored_IsSafeForSecretsIsFalse()
    {
        RunGit("init");

        Assert.False((await _target.InspectAsync(Binding(), CancellationToken.None)).IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_InGitRepoIgnoredAndUntracked_IsSafeForSecretsIsTrue()
    {
        RunGit("init");
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "local.settings.json\n");

        Assert.True((await _target.InspectAsync(Binding(), CancellationToken.None)).IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_InGitRepoIgnoredButTracked_IsSafeForSecretsIsFalse()
    {
        RunGit("init");
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "local.settings.json\n");
        await File.WriteAllTextAsync(FilePath(), "{}");
        RunGit("add", "-f", "local.settings.json");

        Assert.False((await _target.InspectAsync(Binding(), CancellationToken.None)).IsSafeForSecrets);
    }

    [Fact]
    public async Task EnsureGitIgnoredAsync_CalledTwice_AddsLineOnceAndMakesFileSafe()
    {
        RunGit("init");

        await _target.EnsureGitIgnoredAsync(Binding(), CancellationToken.None);
        await _target.EnsureGitIgnoredAsync(Binding(), CancellationToken.None);

        Assert.Equal(["local.settings.json"], File.ReadAllLines(Path.Combine(_root, ".gitignore")));
        Assert.True((await _target.InspectAsync(Binding(), CancellationToken.None)).IsSafeForSecrets);
    }

    private LocalTargetBinding Binding() =>
        new(LocalTargetKind.LocalSettings, CsprojPath(), LocalFileFormat.FlatDictionary);

    private string CsprojPath() => Path.Combine(_root, "Func.csproj");

    private string FilePath() => Path.Combine(_root, "local.settings.json");

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
