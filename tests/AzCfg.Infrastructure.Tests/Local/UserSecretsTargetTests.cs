using AzCfg.Core.Model;
using AzCfg.Infrastructure.Local;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace AzCfg.Infrastructure.Tests.Local;

public sealed class UserSecretsTargetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("azcfg-user-secrets-tests-").FullName;
    private readonly string _secretsId = $"azcfg-tests-{Guid.NewGuid():N}";
    private readonly UserSecretsTarget _target = new();

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        DeleteSecretsDirectory(_secretsId);
    }

    [Fact]
    public void Kind_IsUserSecrets() => Assert.Equal(LocalTargetKind.UserSecrets, _target.Kind);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public void CanHandle_AppOrProjectWithSecretsId_ReturnsTrue(bool isLibrary, bool hasSecretsId, bool expected) =>
        Assert.Equal(expected, _target.CanHandle(new ProjectInfo(_root, CsprojPath(), "Service", false, hasSecretsId ? _secretsId : null, isLibrary)));

    [Fact]
    public async Task ReadAsync_FileMissing_ReturnsEmptyList() =>
        Assert.Empty(await _target.ReadAsync(Binding(WithId()), CancellationToken.None));

    [Fact]
    public async Task ReadAsync_NoSecretsId_ReturnsEmptyAndDoesNotModifyCsproj()
    {
        var values = await _target.ReadAsync(Binding(WithoutId()), CancellationToken.None);

        Assert.Empty(values);
        Assert.DoesNotContain("UserSecretsId", await File.ReadAllTextAsync(CsprojPath()));
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsNestedKeys()
    {
        var binding = Binding(WithId());
        LocalValue[] written = [new("ConnectionStrings:Crm", "crm"), new("Logging:LogLevel:Default", "Information"), new("Simple", "v")];

        await _target.WriteAsync(binding, written, CancellationToken.None);
        var read = await _target.ReadAsync(binding, CancellationToken.None);

        Assert.Equal(written.OrderBy(v => v.Key), read.OrderBy(v => v.Key));
    }

    [Fact]
    public async Task WriteAsync_WritesNestedJsonToSecretsFile()
    {
        await _target.WriteAsync(Binding(WithId()), [new LocalValue("A:B", "v")], CancellationToken.None);

        Assert.Equal("""{"A":{"B":"v"}}""", await File.ReadAllTextAsync(PathHelper.GetSecretsPathFromSecretsId(_secretsId)));
    }

    [Fact]
    public async Task WriteAsync_CalledTwice_FullyOverwritesPreviousContent()
    {
        var binding = Binding(WithId());
        await _target.WriteAsync(binding, [new LocalValue("Old", "1")], CancellationToken.None);

        await _target.WriteAsync(binding, [new LocalValue("New", "2")], CancellationToken.None);

        Assert.Equal([new LocalValue("New", "2")], await _target.ReadAsync(binding, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectAsync_ReportsExistsAndIsAlwaysSafe(bool fileExists)
    {
        var binding = Binding(WithId());
        if (fileExists)
        {
            await _target.WriteAsync(binding, [new LocalValue("A", "1")], CancellationToken.None);
        }

        var option = await _target.InspectAsync(binding, CancellationToken.None);

        Assert.Equal(fileExists, option.Exists);
        Assert.True(option.IsSafeForSecrets);
    }

    [Fact]
    public async Task InspectAsync_NoSecretsId_DoesNotModifyCsprojAndIsSafe()
    {
        var option = await _target.InspectAsync(Binding(WithoutId()), CancellationToken.None);

        Assert.False(option.Exists);
        Assert.True(option.IsSafeForSecrets);
        Assert.DoesNotContain("UserSecretsId", await File.ReadAllTextAsync(CsprojPath()));
    }

    [Fact]
    public async Task WriteAsync_NoSecretsId_InitializesUserSecretsIdInCsproj()
    {
        var binding = Binding(WithoutId());

        await _target.WriteAsync(binding, [new LocalValue("A:B", "v")], CancellationToken.None);

        var id = (await File.ReadAllTextAsync(CsprojPath())).Split("<UserSecretsId>")[1].Split("</UserSecretsId>")[0];
        try
        {
            Assert.Equal([new LocalValue("A:B", "v")], await _target.ReadAsync(binding, CancellationToken.None));
        }
        finally
        {
            DeleteSecretsDirectory(id);
        }
    }

    private static void DeleteSecretsDirectory(string secretsId)
    {
        var directory = Path.GetDirectoryName(PathHelper.GetSecretsPathFromSecretsId(secretsId))!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static LocalTargetBinding Binding(string csproj) => new(LocalTargetKind.UserSecrets, csproj, LocalFileFormat.NestedJson);

    private string CsprojPath() => Path.Combine(_root, "Service.csproj");

    private string WithId() => WriteCsproj($"<PropertyGroup><UserSecretsId>{_secretsId}</UserSecretsId></PropertyGroup>");

    private string WithoutId() => WriteCsproj("");

    private string WriteCsproj(string content)
    {
        File.WriteAllText(CsprojPath(), $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>{content}</Project>""");
        return CsprojPath();
    }
}
