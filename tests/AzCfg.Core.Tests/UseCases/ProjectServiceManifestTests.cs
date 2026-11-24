using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

/// <summary><c>ProjectService.ResolveAsync</c> with a shared <c>azcfg.json</c> (ADR 0012 amendment 2): the manifest's shared fields win.</summary>
public sealed class ProjectServiceManifestTests
{
    private const string Repo = "/repo";
    private const string Orders = "/repo/Orders/Orders.csproj";
    private const string Func = "/repo/Func/Func.csproj";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string ManifestPath = Path.Combine(Repo, IProjectManifestStore.FileName);
    private static readonly LocalTargetBinding UserSecrets = new(LocalTargetKind.UserSecrets, Orders, LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding AppSettings = new(LocalTargetKind.AppSettingsLocal, Orders, LocalFileFormat.NestedJson);

    private static readonly ProjectManifest Shared = new(
        1,
        "orders",
        new("appsettings-local", "Orders/Orders.csproj"),
        new("user-secrets", "Orders"),
        new Dictionary<string, Uri> { ["dev"] = new("https://kv-dev.vault.azure.net/") });

    private static ConfigEnvironment Env(string name) => new(name, new Uri($"https://{name}.azconfig.io"), null, false);

    private static (ProjectService Service, FakeUserStateStore State, FakeProjectManifestStore Manifests) Create(
        ProjectBinding? binding, ProjectManifest? manifest, params FakeLocalTarget[] targets)
    {
        var state = new FakeUserStateStore { Profile = new UserProfile([Env("dev")]), Binding = binding };
        state.Snapshots["dev"] = new ConfigState(new Dictionary<string, StateItem>());
        var manifests = new FakeProjectManifestStore();
        if (manifest is not null)
        {
            manifests.Files[ManifestPath] = manifest;
        }

        var detector = new FakeProjectDetector(new(Repo, Orders, "Orders", false, null), new(Repo, Func, "Func", true, null));
        var store = new FakeConfigStore(new() { ["dev"] = ["orders", "billing"] });
        FakeLocalTarget[] all = targets.Length > 0 ? targets : [new(LocalTargetKind.UserSecrets), new(LocalTargetKind.AppSettingsLocal)];
        return (new(state, detector, store, new SeparateStoresResolver(), all, manifests), state, manifests);
    }

    private static ProjectBinding Bound(string service = "orders") => new(
        Repo, service, AppSettings, UserSecrets, new Dictionary<string, Uri> { ["dev"] = new("https://kv-dev.vault.azure.net") }, "dev", ["Logging:*"]);

    [Fact]
    public async Task NoBinding_WithManifest_BindsFromManifestWithoutWizard()
    {
        var (service, state, manifests) = Create(null, Shared);

        var result = await service.ResolveAsync(Repo, Ct);

        var binding = Assert.IsType<ProjectBinding>(result.Existing);
        Assert.Same(binding, state.Binding);
        Assert.Equal((Repo, "orders", AppSettings, UserSecrets), (binding.RepoPath, binding.Service, binding.ValuesTarget, binding.SecretsTarget));
        Assert.Equal(new Uri("https://kv-dev.vault.azure.net"), binding.VaultByEnvironment["dev"]);
        Assert.Null(binding.ActiveEnvironment);
        Assert.Null(binding.Ignore);
        Assert.Equal(ManifestPath, result.ManifestPath);
        Assert.Null(result.PreviousService);
        Assert.Empty(manifests.Saved);
    }

    [Fact]
    public async Task NoBinding_WithoutManifest_StillOffersTheWizard()
    {
        var (service, state, _) = Create(null, null);

        var result = await service.ResolveAsync(Repo, Ct);

        Assert.Null(result.Existing);
        Assert.Null(result.ManifestPath);
        Assert.NotEmpty(result.TargetOptions);
        Assert.Null(state.Binding);
    }

    [Fact]
    public async Task NoBinding_ManifestSecretsTargetUnsafe_ThrowsAndSavesNothing()
    {
        var (service, state, _) = Create(
            null,
            Shared with { Secrets = new("appsettings-local", "Orders") },
            new FakeLocalTarget(LocalTargetKind.UserSecrets),
            new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, isSafeForSecrets: false));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(Repo, Ct));

        Assert.Contains("not safe for secrets", ex.Message);
        Assert.Null(state.Binding);
    }

    [Theory]
    [InlineData("Missing/Missing.csproj")]
    [InlineData("Func")]
    public async Task NoBinding_ManifestProjectNotFoundOrNotHandled_ThrowsNamingTheManifest(string project)
    {
        var (service, state, _) = Create(
            null,
            Shared with { Values = new("appsettings-local", project) },
            new FakeLocalTarget(LocalTargetKind.UserSecrets),
            new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, canHandle: project != "Func"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(Repo, Ct));

        Assert.Contains(ManifestPath, ex.Message);
        Assert.Contains("values", ex.Message);
        Assert.Null(state.Binding);
    }

    [Fact]
    public async Task Binding_MatchingManifest_IsReturnedUnchanged()
    {
        var binding = Bound();
        var (service, state, _) = Create(binding, Shared);

        var result = await service.ResolveAsync(Repo, Ct);

        Assert.Same(binding, result.Existing);
        Assert.Same(binding, state.Binding);
        Assert.Equal(ManifestPath, result.ManifestPath);
        Assert.Equal(0, state.DeleteSnapshotsCalls);
    }

    [Fact]
    public async Task Binding_WithAnotherService_TakesManifestServiceResetsActiveAndDropsSnapshots()
    {
        var (service, state, _) = Create(Bound("billing"), Shared);

        var result = await service.ResolveAsync(Repo, Ct);

        var binding = Assert.IsType<ProjectBinding>(result.Existing);
        Assert.Equal(("orders", (string?)null), (binding.Service, binding.ActiveEnvironment));
        Assert.Equal(["Logging:*"], binding.Ignore!);
        Assert.Same(binding, state.Binding);
        Assert.Equal(1, state.DeleteSnapshotsCalls);
        Assert.Empty(state.Snapshots);
        Assert.Equal("billing", result.PreviousService);
    }

    [Fact]
    public async Task Binding_WithOtherTargetsAndVaults_TakesManifestFieldsAndKeepsActiveIgnoreAndSnapshots()
    {
        var binding = Bound() with
        {
            ValuesTarget = UserSecrets,
            VaultByEnvironment = new Dictionary<string, Uri> { ["dev"] = new("https://old.vault.azure.net"), ["mine"] = new("https://mine.vault.azure.net") },
        };
        var (service, state, _) = Create(binding, Shared);

        var result = await service.ResolveAsync(Repo, Ct);

        var updated = Assert.IsType<ProjectBinding>(result.Existing);
        Assert.Equal(AppSettings, updated.ValuesTarget);
        Assert.Equal(new Uri("https://kv-dev.vault.azure.net"), updated.VaultByEnvironment["dev"]);
        Assert.Equal(new Uri("https://mine.vault.azure.net"), updated.VaultByEnvironment["mine"]);
        Assert.Equal(("dev", "Logging:*"), (updated.ActiveEnvironment, Assert.Single(updated.Ignore!)));
        Assert.Same(updated, state.Binding);
        Assert.Equal(0, state.DeleteSnapshotsCalls);
        Assert.Null(result.PreviousService);
    }

    [Fact]
    public async Task ManifestVaults_TakeTheProfileOrBindingEnvironmentSpellingAndTheStoredUriForm()
    {
        var binding = Bound() with { VaultByEnvironment = new Dictionary<string, Uri> { ["Mine"] = new("https://old.vault.azure.net") } };
        var manifest = Shared with
        {
            Vaults = new Dictionary<string, Uri> { ["DEV"] = new("https://kv-dev.vault.azure.net/"), ["mine"] = new("https://mine.vault.azure.net/") },
        };
        var (service, state, _) = Create(binding, manifest);

        var result = await service.ResolveAsync(Repo, Ct);

        var vaults = result.Existing!.VaultByEnvironment;
        Assert.Equal(["Mine", "dev"], vaults.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("https://kv-dev.vault.azure.net", vaults["dev"].OriginalString);
        Assert.Equal("https://mine.vault.azure.net", vaults["Mine"].OriginalString);
        Assert.Same(result.Existing, state.Binding);
    }

    [Fact]
    public async Task ManifestVaultWithPath_ThrowsNamingTheManifest()
    {
        var (service, state, _) = Create(null, Shared with { Vaults = new Dictionary<string, Uri> { ["dev"] = new("https://kv-dev.vault.azure.net/secrets") } });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(Repo, Ct));

        Assert.Contains(ManifestPath, ex.Message);
        Assert.Contains("vaults.dev", ex.Message);
        Assert.Null(state.Binding);
    }

    [Fact]
    public async Task ManifestOfAnotherProject_IsIgnored_AndTheWizardIsOffered()
    {
        // Root manifest names Orders; resolving the Func project (its own folder) must not take Orders' service and targets.
        var (service, state, _) = Create(null, Shared);

        var result = await service.ResolveAsync("/repo/Func", Ct);

        Assert.Null(result.Existing);
        Assert.Null(result.ManifestPath);
        Assert.NotEmpty(result.TargetOptions);
        Assert.Null(state.Binding);
    }

    [Fact]
    public async Task ManifestOfAnotherProject_LeavesAnExistingBindingAlone()
    {
        var binding = Bound("billing");
        var (service, state, _) = Create(binding, Shared);

        var result = await service.ResolveAsync("/repo/Func", Ct);

        Assert.Same(binding, result.Existing);
        Assert.Null(result.ManifestPath);
        Assert.Equal(0, state.DeleteSnapshotsCalls);
    }

    [Fact]
    public async Task ManifestAbove_NamingThisProjectFolder_Applies()
    {
        var (service, _, _) = Create(null, Shared);

        var result = await service.ResolveAsync("/repo/Orders", Ct);

        Assert.Equal(("orders", ManifestPath), (result.Existing!.Service, result.ManifestPath));
    }

    [Fact]
    public async Task CorruptedManifest_ThrowsTheManifestError()
    {
        var binding = Bound();
        var (service, state, manifests) = Create(binding, null);
        manifests.FindError = new ManifestException([new(ManifestErrorKind.Unreadable, null, "The file is not valid JSON.")], ManifestPath);

        var ex = await Assert.ThrowsAsync<ManifestException>(() => service.ResolveAsync(Repo, Ct));

        Assert.Equal(ManifestPath, ex.FilePath);
        Assert.Same(binding, state.Binding);
    }
}
