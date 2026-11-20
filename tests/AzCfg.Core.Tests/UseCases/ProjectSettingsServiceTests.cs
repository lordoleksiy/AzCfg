using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class ProjectSettingsServiceTests
{
    private const string Repo = "/repo";
    private const string Orders = "/repo/Orders/Orders.csproj";
    private const string Func = "/repo/Func/Func.csproj";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly UserProfile Profile = new(
        [new("dev", new Uri("https://dev.azconfig.io"), null, false), new("prod", new Uri("https://prod.azconfig.io"), null, true)]);

    private static readonly ProjectBinding Original = Build("dev");

    private static ProjectBinding Bound() => Original;

    private static ProjectBinding Build(string? active) => new(
        Repo,
        "orders",
        new(LocalTargetKind.AppSettingsLocal, Orders, LocalFileFormat.NestedJson),
        new(LocalTargetKind.UserSecrets, Orders, LocalFileFormat.NestedJson),
        new Dictionary<string, Uri> { ["dev"] = new("https://kv-dev.vault.azure.net") },
        active,
        ["Logging:*", "Debug"]);

    private static (ProjectSettingsService Service, FakeUserStateStore State) Create(
        ProjectBinding? binding, params FakeLocalTarget[] targets)
    {
        var state = new FakeUserStateStore { Profile = Profile, Binding = binding };
        var detector = new FakeProjectDetector(
            new(Repo, Orders, "Orders", false, null),
            new(Repo, Func, "Func", true, null));
        return (new(state, detector, targets.Length > 0 ? targets : DefaultTargets()), state);
    }

    private static FakeLocalTarget[] DefaultTargets() =>
        [new(LocalTargetKind.UserSecrets), new(LocalTargetKind.AppSettingsLocal), new(LocalTargetKind.LocalSettings)];

    private static string Full(string path) => Path.GetFullPath(path);

    [Fact]
    public async Task ListAsync_NotBound_IsEmpty()
    {
        var (service, _) = Create(null);

        Assert.Empty(await service.ListAsync(Repo, Ct));
    }

    [Fact]
    public async Task ListAsync_ListsEveryProjectKey()
    {
        var (service, _) = Create(Bound());

        var list = await service.ListAsync(Repo, Ct);

        Assert.Equal(
            [
                new("project.service", "orders"),
                new("project.values", "appsettings-local:" + Orders),
                new("project.secrets", "user-secrets:" + Orders),
                new("project.vault.dev", "https://kv-dev.vault.azure.net"),
                new("project.vault.prod", null),
                new("project.active", "dev"),
                new("project.ignore", "Logging:*,Debug"),
            ],
            list);
    }

    [Fact]
    public async Task GetAsync_IsCaseInsensitiveAndReadsEveryKey()
    {
        var (service, _) = Create(Bound());

        Assert.Equal(new("project.service", "orders"), await service.GetAsync(Repo, "PROJECT.Service", Ct));
        Assert.Equal(new("project.vault.dev", "https://kv-dev.vault.azure.net"), await service.GetAsync(Repo, "project.vault.DEV", Ct));
        Assert.Equal(new("project.vault.prod", null), await service.GetAsync(Repo, "project.vault.prod", Ct));
        Assert.Equal(new("project.active", "dev"), await service.GetAsync(Repo, "project.active", Ct));
        Assert.Equal(new("project.ignore", "Logging:*,Debug"), await service.GetAsync(Repo, "project.ignore", Ct));
    }

    [Fact]
    public async Task GetAsync_UnknownKey_ListsKnownKeys()
    {
        var (service, _) = Create(Bound());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Repo, "project.color", Ct));

        Assert.Contains("project.service", ex.Message);
    }

    [Fact]
    public async Task GetAsync_And_SetAsync_NotBound_AskForPull()
    {
        var (service, _) = Create(null);

        var get = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(Repo, "project.service", Ct));
        var set = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(Repo, "project.service", "x", Ct));

        Assert.Contains("azcfg pull", get.Message);
        Assert.Contains("azcfg pull", set.Message);
    }

    [Theory]
    [InlineData("project.active", "azcfg use")]
    [InlineData("project.ignore", "azcfg ignore")]
    public async Task SetAsync_ReadOnlyKey_PointsToTheRightCommand(string key, string command)
    {
        var (service, state) = Create(Bound());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, key, "x", Ct));

        Assert.Contains(command, ex.Message);
        Assert.Equal(Bound(), state.Binding);
    }

    [Fact]
    public async Task SetAsync_UnknownKey_IsRejected()
    {
        var (service, _) = Create(Bound());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.color", "x", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.vault.qa", "https://x.vault.azure.net", Ct));
    }

    [Fact]
    public async Task SetAsync_Service_ResetsActiveEnvironmentAndDropsSnapshots()
    {
        var (service, state) = Create(Bound());
        state.Snapshots["dev"] = new(new Dictionary<string, StateItem>());

        var result = await service.SetAsync(Repo, "project.service", " billing ", Ct);

        Assert.Equal(new("project.service", "billing"), result);
        Assert.Equal("billing", state.Binding!.Service);
        Assert.Null(state.Binding.ActiveEnvironment);
        Assert.Equal(1, state.DeleteSnapshotsCalls);
        Assert.Empty(state.Snapshots);
    }

    [Fact]
    public async Task SetAsync_SameService_IsNoOp()
    {
        var (service, state) = Create(Bound());
        state.Snapshots["dev"] = new(new Dictionary<string, StateItem>());

        await service.SetAsync(Repo, "project.service", "orders", Ct);

        Assert.Equal(Bound(), state.Binding);
        Assert.Equal(0, state.DeleteSnapshotsCalls);
        Assert.Single(state.Snapshots);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a:b")]
    [InlineData("global")]
    public async Task SetAsync_InvalidService_IsRejected(string value)
    {
        var (service, state) = Create(Bound());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.service", value, Ct));

        Assert.Contains("project.service", ex.Message);
        Assert.Equal(Bound(), state.Binding);
    }

    [Fact]
    public async Task SetAsync_Values_ChangesKindAndFormatKeepingPath()
    {
        var (service, state) = Create(Bound());

        var result = await service.SetAsync(Repo, "project.values", "local-settings", Ct);

        Assert.Equal(new("project.values", "local-settings:" + Orders), result);
        Assert.Equal(new(LocalTargetKind.LocalSettings, Orders, LocalFileFormat.FlatDictionary), state.Binding!.ValuesTarget);
        Assert.Equal("dev", state.Binding.ActiveEnvironment);
        Assert.Equal(Bound().SecretsTarget, state.Binding.SecretsTarget);
    }

    [Fact]
    public async Task SetAsync_Values_AcceptsAProjectPathAndItsDirectory()
    {
        var (service, state) = Create(Bound());

        await service.SetAsync(Repo, "project.values", "user-secrets:Func/Func.csproj", Ct);
        Assert.Equal(new(LocalTargetKind.UserSecrets, Func, LocalFileFormat.NestedJson), state.Binding!.ValuesTarget);

        await service.SetAsync(Repo, "project.values", "appsettings-local:" + Full("/repo/Orders"), Ct);
        Assert.Equal(new(LocalTargetKind.AppSettingsLocal, Orders, LocalFileFormat.NestedJson), state.Binding.ValuesTarget);
    }

    [Fact]
    public async Task SetAsync_Secrets_ChangesTarget()
    {
        var (service, state) = Create(Bound());

        await service.SetAsync(Repo, "project.secrets", "appsettings-local:" + Func, Ct);

        Assert.Equal(new(LocalTargetKind.AppSettingsLocal, Func, LocalFileFormat.NestedJson), state.Binding!.SecretsTarget);
    }

    [Fact]
    public async Task SetAsync_SameTarget_IsNoOp()
    {
        var (service, state) = Create(Bound());
        var before = state.Binding;

        var result = await service.SetAsync(Repo, "project.secrets", "user-secrets", Ct);

        Assert.Equal(new("project.secrets", "user-secrets:" + Orders), result);
        Assert.Same(before, state.Binding);
    }

    [Fact]
    public async Task SetAsync_SecretsTargetNotSafe_IsRejected()
    {
        var (service, state) = Create(
            Bound(),
            new FakeLocalTarget(LocalTargetKind.UserSecrets),
            new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, isSafeForSecrets: false));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(Repo, "project.secrets", "appsettings-local", Ct));

        Assert.Contains("not safe for secrets", ex.Message);
        Assert.Equal(Bound(), state.Binding);
    }

    [Fact]
    public async Task SetAsync_UnsafeValuesTarget_IsAllowed()
    {
        var (service, state) = Create(
            Bound(),
            new FakeLocalTarget(LocalTargetKind.UserSecrets),
            new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, isSafeForSecrets: false));

        await service.SetAsync(Repo, "project.values", "user-secrets:" + Func, Ct);
        await service.SetAsync(Repo, "project.values", "appsettings-local", Ct);

        Assert.Equal(LocalTargetKind.AppSettingsLocal, state.Binding!.ValuesTarget.Kind);
    }

    [Fact]
    public async Task SetAsync_GitIgnoreMode_IgnoresTheNewTargetBeforeCheckingSafety()
    {
        var local = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, isSafeForSecrets: false) { MakesSafeWhenGitIgnored = true };
        var (service, state) = Create(Bound(), new FakeLocalTarget(LocalTargetKind.UserSecrets), local);
        state.Profile = Profile with { GitIgnoreLocalFiles = true };

        await service.SetAsync(Repo, "project.secrets", "appsettings-local", Ct);

        Assert.Single(local.EnsureGitIgnoredCalls);
        Assert.Equal(LocalTargetKind.AppSettingsLocal, state.Binding!.SecretsTarget.Kind);
    }

    [Fact]
    public async Task SetAsync_LocalSettingsWithoutRegisteredTarget_IsRejected()
    {
        var (service, state) = Create(
            Bound(), new FakeLocalTarget(LocalTargetKind.UserSecrets), new FakeLocalTarget(LocalTargetKind.AppSettingsLocal));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.values", "local-settings", Ct));

        Assert.Contains("not supported yet", ex.Message);
        Assert.Equal(Bound(), state.Binding);
    }

    [Theory]
    [InlineData("")]
    [InlineData("database")]
    [InlineData("user-secrets:Missing/Missing.csproj")]
    [InlineData("user-secrets:/elsewhere/X.csproj")]
    public async Task SetAsync_InvalidTarget_IsRejected(string value)
    {
        var (service, state) = Create(Bound());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.values", value, Ct));

        Assert.Equal(Bound(), state.Binding);
    }

    [Fact]
    public async Task SetAsync_TargetThatCannotHandleTheProject_IsRejected()
    {
        var (service, _) = Create(
            Bound(), new FakeLocalTarget(LocalTargetKind.UserSecrets), new FakeLocalTarget(LocalTargetKind.LocalSettings, canHandle: false));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.values", "local-settings", Ct));

        Assert.Contains("does not apply", ex.Message);
    }

    [Fact]
    public async Task SetAsync_Vault_SetsAndUnsets()
    {
        var (service, state) = Create(Bound());

        var set = await service.SetAsync(Repo, "project.vault.PROD", "https://kv-prod.vault.azure.net", Ct);
        Assert.Equal(new("project.vault.prod", "https://kv-prod.vault.azure.net"), set);
        Assert.Equal(new Uri("https://kv-prod.vault.azure.net"), state.Binding!.VaultByEnvironment["prod"]);
        Assert.Equal(2, state.Binding.VaultByEnvironment.Count);

        var unset = await service.SetAsync(Repo, "project.vault.dev", "", Ct);
        Assert.Equal(new("project.vault.dev", null), unset);
        Assert.False(state.Binding.VaultByEnvironment.ContainsKey("dev"));
    }

    [Fact]
    public async Task SetAsync_VaultSameValue_IsNoOp()
    {
        var (service, state) = Create(Bound());
        var before = state.Binding;

        await service.SetAsync(Repo, "project.vault.dev", "https://kv-dev.vault.azure.net", Ct);
        await service.SetAsync(Repo, "project.vault.prod", "", Ct);

        Assert.Same(before, state.Binding);
    }

    [Theory]
    [InlineData("kv-dev")]
    [InlineData("http://kv-dev.vault.azure.net")]
    [InlineData("/not/a/uri")]
    public async Task SetAsync_InvalidVaultUri_IsRejected(string value)
    {
        var (service, state) = Create(Bound());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.vault.dev", value, Ct));

        Assert.Equal(Bound(), state.Binding);
    }

    [Fact]
    public void ServiceGuard_OtherService_NamesCurrentServiceAndFix()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ProjectServiceGuard.EnsureSameService(Bound(), "billing"));

        Assert.Contains("'orders'", ex.Message);
        Assert.Contains("azcfg config set project.service billing", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("orders")]
    public void ServiceGuard_NoneOrSameService_IsNoOp(string? requested) =>
        ProjectServiceGuard.EnsureSameService(Bound(), requested);

    [Fact]
    public void ServiceGuard_DifferentCase_IsAnotherService() =>
        Assert.Throws<InvalidOperationException>(() => ProjectServiceGuard.EnsureSameService(Bound(), "ORDERS"));

    [Fact]
    public void ServiceHint_MentionsUseOrPullForceBeforePush()
    {
        var hint = ProjectSettingKeys.Hint("project.service")!;

        Assert.Contains("azcfg use <ENV>", hint);
        Assert.Contains("azcfg pull --force", hint);
        Assert.Contains("azcfg push", hint);
    }

    [Fact]
    public async Task SetAsync_Service_SnapshotDeleteFails_LeavesBindingUnchanged()
    {
        var (service, state) = Create(Bound());
        state.DeleteSnapshotsThrows = true;

        await Assert.ThrowsAsync<IOException>(() => service.SetAsync(Repo, "project.service", "billing", Ct));

        Assert.Equal(Bound(), state.Binding);
    }

    [Theory]
    [InlineData("https://kv-prod.vault.azure.net/", "https://kv-prod.vault.azure.net")]
    [InlineData("  https://kv-prod.vault.azure.net  ", "https://kv-prod.vault.azure.net")]
    public async Task SetAsync_Vault_StoresNormalizedRootUri(string value, string expected)
    {
        var (service, state) = Create(Bound());

        var result = await service.SetAsync(Repo, "project.vault.prod", value, Ct);

        Assert.Equal(expected, result.Value);
        Assert.Equal(new Uri(expected), state.Binding!.VaultByEnvironment["prod"]);
    }

    [Theory]
    [InlineData("https://kv-dev.vault.azure.net/secrets/Db--Password")]
    [InlineData("https://kv-dev.vault.azure.net/?x=1")]
    [InlineData("https://kv-dev.vault.azure.net/#frag")]
    [InlineData("https://user@kv-dev.vault.azure.net")]
    public async Task SetAsync_VaultWithPathQueryFragmentOrUserInfo_IsRejected(string value)
    {
        var (service, state) = Create(Bound());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Repo, "project.vault.prod", value, Ct));

        Assert.Equal(Bound(), state.Binding);
    }

    [Theory]
    [InlineData("project.service", true)]
    [InlineData("PROJECT.VALUES", true)]
    [InlineData("project.secrets", true)]
    [InlineData("project.vault.dev", false)]
    [InlineData("mode", false)]
    public void Hint_ExistsForServiceAndTargets(string key, bool hasHint) =>
        Assert.Equal(hasHint, ProjectSettingKeys.Hint(key) is not null);
}
