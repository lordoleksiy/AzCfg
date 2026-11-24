using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

/// <summary><c>config project.*</c> with a shared <c>azcfg.json</c> and the <c>project.shared</c> key (ADR 0012 amendment 2, ADR 0014 amendment).</summary>
public sealed class ProjectSettingsServiceManifestTests
{
    private const string Repo = "/repo";
    private const string Orders = "/repo/Orders/Orders.csproj";
    private const string Func = "/repo/Func/Func.csproj";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string ManifestPath = Path.Combine(Repo, IProjectManifestStore.FileName);
    private static readonly UserProfile Profile = new(
        [new("dev", new Uri("https://dev.azconfig.io"), null, false), new("prod", new Uri("https://prod.azconfig.io"), null, true)]);

    private static ProjectBinding Bound() => new(
        Repo,
        "orders",
        new(LocalTargetKind.AppSettingsLocal, Orders, LocalFileFormat.NestedJson),
        new(LocalTargetKind.UserSecrets, Orders, LocalFileFormat.NestedJson),
        new Dictionary<string, Uri> { ["dev"] = new("https://kv-dev.vault.azure.net") },
        "dev",
        ["Logging:*"]);

    private static readonly ProjectManifest Shared = new(
        1,
        "orders",
        new("appsettings-local", "Orders/Orders.csproj"),
        new("user-secrets", "Orders/Orders.csproj"),
        new Dictionary<string, Uri> { ["dev"] = new("https://kv-dev.vault.azure.net/") });

    private static (ProjectSettingsService Service, FakeUserStateStore State, FakeProjectManifestStore Manifests) Create(ProjectManifest? manifest)
    {
        var state = new FakeUserStateStore { Profile = Profile, Binding = Bound() };
        var manifests = new FakeProjectManifestStore();
        if (manifest is not null)
        {
            manifests.Files[ManifestPath] = manifest;
        }

        var detector = new FakeProjectDetector(new(Repo, Orders, "Orders", false, null), new(Repo, Func, "Func", true, null));
        FakeLocalTarget[] targets = [new(LocalTargetKind.UserSecrets), new(LocalTargetKind.AppSettingsLocal), new(LocalTargetKind.LocalSettings)];
        return (new(state, detector, targets, manifests), state, manifests);
    }

    [Fact]
    public async Task Shared_NoManifest_IsFalse()
    {
        var (service, _, _) = Create(null);

        var setting = await service.GetAsync(Repo, "project.shared", Ct);

        Assert.Equal(new("project.shared", "false"), setting);
        Assert.Null(setting.Source);
    }

    [Fact]
    public async Task Shared_WithManifest_IsTrueWithItsPath()
    {
        var (service, _, _) = Create(Shared);

        var list = await service.ListAsync(Repo, Ct);

        var shared = Assert.Single(list, s => s.Key == "project.shared");
        Assert.Equal(("true", ManifestPath), (shared.Value, shared.Source));
    }

    [Fact]
    public async Task SetShared_True_WritesManifestFromBindingNextToProjectPath()
    {
        var (service, state, manifests) = Create(null);
        var original = state.Binding;

        var result = await service.SetAsync(Repo, "PROJECT.SHARED", " TRUE ", Ct);

        Assert.Equal(("project.shared", "true", ManifestPath, ProjectSettingKeys.SharedHint), (result.Key, result.Value, result.Source, result.Hint));
        var (path, manifest) = Assert.Single(manifests.Saved);
        Assert.Equal(ManifestPath, path);
        Assert.Equal(Shared, manifest);
        Assert.Same(original, state.Binding);
    }

    [Fact]
    public async Task SetShared_AlreadyShared_RewritesTheFoundManifestOnlyWhenItDiffers()
    {
        var (service, _, manifests) = Create(Shared);

        var result = await service.SetAsync(Repo, "project.shared", "true", Ct);

        Assert.Equal(ManifestPath, result.Source);
        Assert.Empty(manifests.Saved);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SetShared_NotTrue_IsRejectedWithTheUnshareHint(string? value)
    {
        var (service, _, manifests) = Create(Shared);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.shared", value, Ct));

        Assert.Contains("'true'", ex.Message);
        Assert.Contains("delete", ex.Message);
        Assert.Empty(manifests.Saved);
    }

    [Fact]
    public async Task SetShared_TargetOutsideTheManifestFolder_ThrowsAndWritesNothing()
    {
        var (service, state, manifests) = Create(null);
        state.Binding = Bound() with { ValuesTarget = new(LocalTargetKind.AppSettingsLocal, "/elsewhere/X/X.csproj", LocalFileFormat.NestedJson) };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(Repo, "project.shared", "true", Ct));

        Assert.Contains("outside the manifest folder", ex.Message);
        Assert.Empty(manifests.Saved);
    }

    [Fact]
    public async Task SetService_Shared_WritesManifestAndBindingWithHint()
    {
        var (service, state, manifests) = Create(Shared);
        state.Snapshots["dev"] = new(new Dictionary<string, StateItem>());

        var result = await service.SetAsync(Repo, "project.service", "billing", Ct);

        Assert.Equal(("billing", ManifestPath, ProjectSettingKeys.SharedHint), (result.Value, result.Source, result.Hint));
        Assert.Equal(Shared with { Service = "billing" }, Assert.Single(manifests.Saved).Manifest);
        Assert.Equal(("billing", (string?)null), (state.Binding!.Service, state.Binding.ActiveEnvironment));
        Assert.Empty(state.Snapshots);
    }

    [Fact]
    public async Task SetValues_Shared_WritesRelativeTargetToManifest()
    {
        var (service, state, manifests) = Create(Shared);

        var result = await service.SetAsync(Repo, "project.values", "local-settings:Func", Ct);

        Assert.Equal(ManifestPath, result.Source);
        Assert.Equal(Shared with { Values = new("local-settings", "Func/Func.csproj") }, Assert.Single(manifests.Saved).Manifest);
        Assert.Equal(LocalTargetKind.LocalSettings, state.Binding!.ValuesTarget.Kind);
    }

    [Fact]
    public async Task SetSecrets_Shared_WritesManifest()
    {
        var (service, _, manifests) = Create(Shared);

        await service.SetAsync(Repo, "project.secrets", "user-secrets:Func", Ct);

        Assert.Equal(Shared with { Secrets = new("user-secrets", "Func/Func.csproj") }, Assert.Single(manifests.Saved).Manifest);
    }

    [Fact]
    public async Task SetVault_Shared_AddsAndRemovesInManifest()
    {
        var (service, _, manifests) = Create(Shared);

        await service.SetAsync(Repo, "project.vault.prod", "https://kv-prod.vault.azure.net", Ct);
        var removed = await service.SetAsync(Repo, "project.vault.dev", "", Ct);

        Assert.Equal(
            new Dictionary<string, Uri> { ["prod"] = new("https://kv-prod.vault.azure.net") },
            manifests.Files[ManifestPath].Vaults);
        Assert.Equal(ProjectSettingKeys.SharedHint, removed.Hint);
    }

    [Fact]
    public async Task SetVault_RemovingTheLastSharedVault_OmitsVaults()
    {
        var (service, _, manifests) = Create(Shared);

        await service.SetAsync(Repo, "project.vault.dev", null, Ct);

        Assert.Null(manifests.Files[ManifestPath].Vaults);
    }

    [Fact]
    public async Task Set_NotShared_NeverWritesManifestAndHasNoHint()
    {
        var (service, _, manifests) = Create(null);

        var result = await service.SetAsync(Repo, "project.service", "billing", Ct);

        Assert.Equal((null, null), (result.Source, result.Hint));
        Assert.Empty(manifests.Saved);
    }

    [Fact]
    public async Task Set_SameValueButManifestDiffers_WritesManifest()
    {
        var (service, state, manifests) = Create(Shared with { Service = "stale" });
        var original = state.Binding;

        await service.SetAsync(Repo, "project.service", "orders", Ct);

        Assert.Equal("orders", Assert.Single(manifests.Saved).Manifest.Service);
        Assert.Same(original, state.Binding);
    }

    [Fact]
    public async Task CorruptedManifest_FailsListGetAndSet()
    {
        var (service, state, manifests) = Create(null);
        var original = state.Binding;
        manifests.FindError = new ManifestException([new(ManifestErrorKind.Schema, "value", "Unknown field 'value'.")], ManifestPath);

        await Assert.ThrowsAsync<ManifestException>(() => service.ListAsync(Repo, Ct));
        await Assert.ThrowsAsync<ManifestException>(() => service.GetAsync(Repo, "project.shared", Ct));
        await Assert.ThrowsAsync<ManifestException>(() => service.SetAsync(Repo, "project.service", "billing", Ct));
        Assert.Same(original, state.Binding);
    }
}
