using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServicePullSelectedTests
{
    private const string SecretText = "AZCFG-TEST-SECRET-7f3a";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment Env = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly HmacSecretFingerprinter Fingerprinter = new(new byte[32]);

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value));
    private static ConfigEntry Secret(string key) => new(key, ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri));
    private static StateItem Item(string key, string value) => new(key, ConfigScope.Service, new StateValue.Plain(value));

    private static ProjectBinding Binding(string? active = "dev", string[]? ignore = null, LocalTargetBinding? values = null) =>
        new("/repo", "orders", values ?? Values, Secrets, new Dictionary<string, Uri>(), active, ignore);

    private sealed record Setup(SyncService Service, FakeUserStateStore State, FakeLocalTarget ValuesTarget, FakeLocalTarget SecretsTarget);

    private static Setup Create(ConfigEntry[] remote, LocalValue[]? values = null, LocalValue[]? secrets = null, StateItem[]? snapshot = null, bool secretsSafe = true)
    {
        var state = new FakeUserStateStore();
        if (snapshot is not null)
        {
            state.Snapshots["dev"] = new(snapshot.ToDictionary(i => i.Key, StringComparer.Ordinal));
        }

        var valuesTarget = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal) { Contents = values ?? [] };
        var secretsTarget = new FakeLocalTarget(LocalTargetKind.UserSecrets, isSafeForSecrets: secretsSafe) { Contents = secrets ?? [] };
        var service = new SyncService(
            new FakeConfigStore([], new() { ["dev"] = remote, ["stage"] = remote }),
            new FakeSecretStore(new() { [SecretUri] = SecretText }),
            state,
            Fingerprinter,
            [valuesTarget, secretsTarget]);
        return new(service, state, valuesTarget, secretsTarget);
    }

    [Fact]
    public async Task PullSelectedAsync_WritesSelectedRemoteChange_KeepsOtherLocalValues()
    {
        var s = Create([Plain("A", "9"), Plain("B", "2"), Plain("C", "3")], values: [new("A", "1"), new("B", "mine"), new("Local", "x")]);

        var result = await s.Service.PullSelectedAsync(Binding(), Env, ["A"], Ct);

        Assert.Equal([new LocalValue("A", "9"), new LocalValue("B", "mine"), new LocalValue("Local", "x")], Assert.Single(s.ValuesTarget.Writes).Values);
        Assert.Empty(s.SecretsTarget.Writes);
        Assert.Equal(new PullResult("dev", 1, 0, false, []), result);
    }

    [Fact]
    public async Task PullSelectedAsync_KeyMissingLocally_IsAppended()
    {
        var s = Create([Plain("A", "1"), Plain("New", "n")], values: [new("A", "mine")]);

        await s.Service.PullSelectedAsync(Binding(), Env, ["New"], Ct);

        Assert.Equal([new LocalValue("A", "mine"), new LocalValue("New", "n")], Assert.Single(s.ValuesTarget.Writes).Values);
    }

    [Fact]
    public async Task PullSelectedAsync_RemoteDeletion_RemovesLocally()
    {
        var s = Create([Plain("A", "1")], values: [new("A", "1"), new("Gone", "x")], snapshot: [Item("A", "1"), Item("Gone", "x")]);

        await s.Service.PullSelectedAsync(Binding(), Env, ["Gone"], Ct);

        Assert.Equal([new LocalValue("A", "1")], Assert.Single(s.ValuesTarget.Writes).Values);
        Assert.False(s.State.SavedSnapshot!.Value.State.Items.ContainsKey("Gone"));
    }

    [Fact]
    public async Task PullSelectedAsync_PlainBecameSecret_MovesToSecretsTarget()
    {
        var s = Create([Secret("Db:Password")], values: [new("Db:Password", "plain"), new("A", "1")]);

        var result = await s.Service.PullSelectedAsync(Binding(), Env, ["Db:Password"], Ct);

        Assert.Equal([new LocalValue("A", "1")], Assert.Single(s.ValuesTarget.Writes).Values);
        Assert.Equal([new LocalValue("Db:Password", SecretText)], Assert.Single(s.SecretsTarget.Writes).Values);
        Assert.Equal((0, 1), (result.ValuesWritten, result.SecretsWritten));
    }

    [Fact]
    public async Task PullSelectedAsync_SecretBecamePlain_MovesToValuesTarget()
    {
        var s = Create([Plain("Db:Password", "plain")], values: [new("A", "1")], secrets: [new("Db:Password", "old"), new("Other", "o")]);

        await s.Service.PullSelectedAsync(Binding(), Env, ["Db:Password"], Ct);

        Assert.Equal([new LocalValue("A", "1"), new LocalValue("Db:Password", "plain")], Assert.Single(s.ValuesTarget.Writes).Values);
        Assert.Equal([new LocalValue("Other", "o")], Assert.Single(s.SecretsTarget.Writes).Values);
    }

    [Fact]
    public async Task PullSelectedAsync_SameTarget_WritesOnce()
    {
        var s = Create([Secret("Db:Password"), Plain("A", "9")], secrets: [new("A", "1"), new("B", "2")]);

        await s.Service.PullSelectedAsync(Binding(values: Secrets), Env, ["A", "Db:Password"], Ct);

        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Equal([new LocalValue("A", "9"), new LocalValue("B", "2"), new LocalValue("Db:Password", SecretText)], Assert.Single(s.SecretsTarget.Writes).Values);
    }

    [Fact]
    public async Task PullSelectedAsync_ScopeMarkerFollowsRemoteEntry()
    {
        var s = Create([Plain("G", "g", ConfigScope.Global)], values: [new("G", "old")]);

        await s.Service.PullSelectedAsync(Binding(), Env, ["G"], Ct);

        Assert.Equal([new LocalValue("G", "g", ConfigScope.Global)], Assert.Single(s.ValuesTarget.Writes).Values);
        Assert.Equal(ConfigScope.Global, s.State.SavedSnapshot!.Value.State.Items["G"].Scope);
    }

    [Fact]
    public async Task PullSelectedAsync_IgnoredKeys_AreSkippedAndListed()
    {
        var s = Create([Plain("A", "9"), Plain("B", "9")], values: [new("A", "1"), new("B", "1")]);

        var result = await s.Service.PullSelectedAsync(Binding(ignore: ["A"]), Env, ["B", "A"], Ct);

        Assert.Equal([new LocalValue("A", "1"), new LocalValue("B", "9")], Assert.Single(s.ValuesTarget.Writes).Values);
        Assert.Equal(["A"], result.Ignored);
        Assert.False(s.State.SavedSnapshot!.Value.State.Items.ContainsKey("A"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stage")]
    public async Task PullSelectedAsync_EnvironmentNotActive_ThrowsAndWritesNothing(string? active)
    {
        var s = Create([Plain("A", "9")], values: [new("A", "1")]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.PullSelectedAsync(Binding(active), Env, ["A"], Ct));

        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Null(s.State.SavedSnapshot);
    }

    [Fact]
    public async Task PullSelectedAsync_ActiveEnvironmentComparedIgnoringCase()
    {
        var s = Create([Plain("A", "9")], values: [new("A", "1")]);

        await s.Service.PullSelectedAsync(Binding("DEV"), Env, ["A"], Ct);

        Assert.Single(s.ValuesTarget.Writes);
    }

    [Fact]
    public async Task PullSelectedAsync_UnsafeSecretsTarget_ThrowsAndWritesNothing()
    {
        var s = Create([Plain("A", "9"), Secret("Db:Password")], values: [new("A", "1")], secretsSafe: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.PullSelectedAsync(Binding(), Env, ["A", "Db:Password"], Ct));

        Assert.DoesNotContain(SecretText, ex.Message);
        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Empty(s.SecretsTarget.Writes);
        Assert.Null(s.State.SavedSnapshot);
    }

    [Fact]
    public async Task PullSelectedAsync_GitIgnoreMode_EnsuresBothTargetsIgnored()
    {
        var s = Create([Plain("A", "9")], values: [new("A", "1")]);
        s.State.Profile = new UserProfile([], GitIgnoreLocalFiles: true);

        await s.Service.PullSelectedAsync(Binding(), Env, ["A"], Ct);

        Assert.Equal([Values], s.ValuesTarget.EnsureGitIgnoredCalls);
        Assert.Equal([Secrets], s.SecretsTarget.EnsureGitIgnoredCalls);
    }

    [Fact]
    public async Task PullSelectedAsync_UpdatesSnapshotOnlyForSelectedKeys_SecretsAsFingerprint()
    {
        var s = Create([Plain("A", "9"), Plain("B", "9"), Secret("Db:Password")], values: [new("A", "1"), new("B", "1")], snapshot: [Item("A", "1"), Item("B", "1")]);

        await s.Service.PullSelectedAsync(Binding(), Env, ["A", "Db:Password"], Ct);

        var (environment, snapshot) = s.State.SavedSnapshot!.Value;
        Assert.Equal("dev", environment);
        Assert.Equal(new StateValue.Plain("9"), snapshot.Items["A"].Value);
        Assert.Equal(new StateValue.Plain("1"), snapshot.Items["B"].Value);
        Assert.Equal(new StateValue.Secret(SecretUri, Fingerprinter.Fingerprint(new SecretString(SecretText))), snapshot.Items["Db:Password"].Value);
        Assert.DoesNotContain(SecretText, JsonSerializer.Serialize(snapshot));
        Assert.Null(s.State.Binding);
    }

    [Fact]
    public async Task PullSelectedAsync_FollowingPullIsNotBlockedBySelectedKeys()
    {
        var s = Create([Plain("A", "9"), Secret("Db:Password")], values: [new("A", "1")], snapshot: [Item("A", "1")]);

        await s.Service.PullSelectedAsync(Binding(), Env, ["A", "Db:Password"], Ct);
        s.ValuesTarget.Contents = s.ValuesTarget.Writes.Single().Values;
        s.SecretsTarget.Contents = s.SecretsTarget.Writes.Single().Values;
        s.State.Snapshots["dev"] = s.State.SavedSnapshot!.Value.State;
        var pull = await s.Service.PullAsync(Binding(), Env, null, false, Ct);

        Assert.False(pull.Blocked);
    }

    [Fact]
    public async Task PullSelectedAsync_UnselectedLocalChangeStillBlocksFollowingPull()
    {
        var s = Create([Plain("A", "9"), Plain("B", "1")], values: [new("A", "1"), new("B", "mine")], snapshot: [Item("A", "1"), Item("B", "1")]);

        await s.Service.PullSelectedAsync(Binding(), Env, ["A"], Ct);
        s.ValuesTarget.Contents = s.ValuesTarget.Writes.Single().Values;
        s.State.Snapshots["dev"] = s.State.SavedSnapshot!.Value.State;
        var pull = await s.Service.PullAsync(Binding(), Env, null, false, Ct);

        Assert.True(pull.Blocked);
        Assert.Equal(["B"], pull.LocalChanges);
    }
}
