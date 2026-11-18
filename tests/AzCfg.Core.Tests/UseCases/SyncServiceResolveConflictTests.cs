using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceResolveConflictTests
{
    private const string SecretText = "AZCFG-TEST-SECRET-7f3a";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment Env = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly HmacSecretFingerprinter Fingerprinter = new(new byte[32]);

    private static ConfigEntry Plain(string key, string value) => new(key, ConfigScope.Service, new ConfigValue.Plain(value), "e");
    private static StateItem Item(string key, string value) => new(key, ConfigScope.Service, new StateValue.Plain(value));

    private static ProjectBinding Binding(string? active = "dev") => new("/repo", "orders", Values, Secrets, new Dictionary<string, Uri>(), active);

    private sealed record Setup(SyncService Service, FakeUserStateStore State, FakeLocalTarget ValuesTarget, FakeLocalTarget SecretsTarget);

    /// <summary>Conflict on A: snapshot 1, local "mine", remote "theirs"; B unchanged.</summary>
    private static Setup Create(ConfigEntry[]? remote = null, LocalValue[]? values = null, LocalValue[]? secrets = null)
    {
        var state = new FakeUserStateStore();
        state.Snapshots["dev"] = new(new Dictionary<string, StateItem>(StringComparer.Ordinal) { ["A"] = Item("A", "1"), ["B"] = Item("B", "2") });
        var valuesTarget = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal) { Contents = values ?? [new("A", "mine"), new("B", "2")] };
        var secretsTarget = new FakeLocalTarget(LocalTargetKind.UserSecrets) { Contents = secrets ?? [] };
        var service = new SyncService(
            new FakeConfigStore([], new() { ["dev"] = remote ?? [Plain("A", "theirs"), Plain("B", "2")] }),
            new FakeSecretStore(new() { [SecretUri] = SecretText }),
            state,
            Fingerprinter,
            [valuesTarget, secretsTarget]);
        return new(service, state, valuesTarget, secretsTarget);
    }

    /// <summary>Makes the saved snapshot the current one, as the real state store does.</summary>
    private static void Persist(Setup s) => s.State.Snapshots["dev"] = s.State.SavedSnapshot!.Value.State;

    [Fact]
    public async Task ResolveConflictAsync_Conflict_IsPlannedAsConflictBefore()
    {
        var s = Create();

        var plan = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.LocalFiles(), Ct);

        Assert.Equal("A", Assert.Single(plan.Conflicts).Key);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepRemote_PullsTheKey()
    {
        var s = Create();

        await s.Service.ResolveConflictAsync(Binding(), Env, "A", ConflictResolution.KeepRemote, Ct);

        Assert.Equal([new LocalValue("A", "theirs"), new LocalValue("B", "2")], Assert.Single(s.ValuesTarget.Writes).Values);
        Assert.Equal(new StateValue.Plain("theirs"), s.State.SavedSnapshot!.Value.State.Items["A"].Value);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepLocal_NextPlanUpdatesWithLocalValue_LocalFilesUntouched()
    {
        var s = Create();

        await s.Service.ResolveConflictAsync(Binding(), Env, "A", ConflictResolution.KeepLocal, Ct);
        Persist(s);
        var plan = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.LocalFiles(), Ct);

        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Empty(s.SecretsTarget.Writes);
        Assert.Empty(plan.Conflicts);
        var change = Assert.Single(plan.Changes);
        Assert.Equal((ChangeKind.Update, "A", (ConfigValue)new ConfigValue.Plain("mine")), (change.Kind, change.After!.Key, change.After.Value));
        Assert.Equal(new StateValue.Plain("2"), s.State.SavedSnapshot!.Value.State.Items["B"].Value);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepLocal_MissingRemotely_RemovesSnapshotItemAndPlansAdd()
    {
        var s = Create(remote: [Plain("B", "2")]);

        await s.Service.ResolveConflictAsync(Binding(), Env, "A", ConflictResolution.KeepLocal, Ct);
        Persist(s);
        var plan = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.LocalFiles(), Ct);

        Assert.False(s.State.SavedSnapshot!.Value.State.Items.ContainsKey("A"));
        Assert.Empty(plan.Conflicts);
        Assert.Equal(ChangeKind.Add, Assert.Single(plan.Changes).Kind);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepLocal_LocalDeletion_IsDeleteSkippedUnlessDeleteRequested()
    {
        var s = Create(values: [new("B", "2")]);

        await s.Service.ResolveConflictAsync(Binding(), Env, "A", ConflictResolution.KeepLocal, Ct);
        Persist(s);
        var upsert = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.LocalFiles(), Ct);
        var delete = await s.Service.PlanPushAsync(Binding(), Env, new PushSource.LocalFiles(Delete: true), Ct);

        Assert.Empty(upsert.Changes);
        Assert.Equal(SyncWarningKind.DeleteSkipped, Assert.Single(upsert.Warnings).Kind);
        Assert.Equal(ChangeKind.Delete, Assert.Single(delete.Changes).Kind);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepLocal_SecretStoredAsFingerprint()
    {
        var s = Create(remote: [Plain("B", "2"), new("A", ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri))], secrets: [new("A", "mine")], values: [new("B", "2")]);

        await s.Service.ResolveConflictAsync(Binding(), Env, "A", ConflictResolution.KeepLocal, Ct);

        Assert.Equal(new StateValue.Secret(SecretUri, Fingerprinter.Fingerprint(new SecretString(SecretText))), s.State.SavedSnapshot!.Value.State.Items["A"].Value);
    }

    [Theory]
    [InlineData(ConflictResolution.KeepLocal, null)]
    [InlineData(ConflictResolution.KeepLocal, "stage")]
    [InlineData(ConflictResolution.KeepRemote, null)]
    [InlineData(ConflictResolution.KeepRemote, "stage")]
    public async Task ResolveConflictAsync_EnvironmentNotActive_Throws(ConflictResolution resolution, string? active)
    {
        var s = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.ResolveConflictAsync(Binding(active), Env, "A", resolution, Ct));

        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Null(s.State.SavedSnapshot);
    }
}
