using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServicePullTests
{
    private const string SecretText = "p@ss-value";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment Env = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value));
    private static ConfigEntry Secret(string key) => new(key, ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri));

    private static ProjectBinding Binding(LocalTargetBinding values, LocalTargetBinding secrets, string? active = null, string[]? ignore = null) =>
        new("/repo", "orders", values, secrets, new Dictionary<string, Uri>(), active, ignore);

    private sealed record Setup(
        SyncService Service, FakeUserStateStore State, FakeLocalTarget ValuesTarget, FakeLocalTarget SecretsTarget);

    private static Setup Create(ConfigEntry[] entries, bool secretsSafe = true)
    {
        var state = new FakeUserStateStore();
        var valuesTarget = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal);
        var secretsTarget = new FakeLocalTarget(LocalTargetKind.UserSecrets, isSafeForSecrets: secretsSafe);
        var service = new SyncService(
            new FakeConfigStore([], new() { ["dev"] = entries }),
            new FakeSecretStore(new() { [SecretUri] = SecretText }),
            state,
            new HmacSecretFingerprinter(new byte[32]),
            [valuesTarget, secretsTarget]);
        return new(service, state, valuesTarget, secretsTarget);
    }

    [Fact]
    public async Task PullAsync_SplitsValuesAndSecretsIntoTheirTargets()
    {
        var s = Create([Plain("Logging:Level", "Info"), Secret("Db:Password")]);

        var result = await s.Service.PullAsync(Binding(Values, Secrets), Env, null, false, Ct);

        var (valuesTarget, values) = Assert.Single(s.ValuesTarget.Writes);
        Assert.Equal(Values, valuesTarget);
        Assert.Equal([new LocalValue("Logging:Level", "Info")], values);
        var (secretsTarget, secrets) = Assert.Single(s.SecretsTarget.Writes);
        Assert.Equal(Secrets, secretsTarget);
        Assert.Equal([new LocalValue("Db:Password", SecretText)], secrets);
        Assert.Equal(new PullResult("dev", 1, 1, false, []), result);
    }

    [Fact]
    public async Task PullAsync_DuplicateLabels_WritesPreferredCopyAndWarns()
    {
        var s = Create([
            new("Logging:Level", ConfigScope.Service, new ConfigValue.Plain("Info"), Label: "Development"),
            new("Logging:Level", ConfigScope.Service, new ConfigValue.Plain("Debug")),
        ]);

        var result = await s.Service.PullAsync(Binding(Values, Secrets), Env, null, false, Ct);

        Assert.Equal([new LocalValue("Logging:Level", "Debug")], Assert.Single(s.ValuesTarget.Writes).Values);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(SyncWarningKind.DuplicateLabels, warning.Kind);
        Assert.Equal("Logging:Level", warning.Key);
    }

    [Fact]
    public async Task PullAsync_SameTarget_WritesOnce()
    {
        var s = Create([Plain("Logging:Level", "Info"), Secret("Db:Password")]);

        await s.Service.PullAsync(Binding(Secrets, Secrets), Env, null, false, Ct);

        var (_, written) = Assert.Single(s.SecretsTarget.Writes);
        Assert.Equal(["Logging:Level", "Db:Password"], written.Select(v => v.Key));
        Assert.Empty(s.ValuesTarget.Writes);
    }

    [Fact]
    public async Task PullAsync_ServiceOverridesGlobal()
    {
        var s = Create([Plain("A", "global", ConfigScope.Global), Plain("A", "service"), Plain("B", "g", ConfigScope.Global)]);

        await s.Service.PullAsync(Binding(Values, Secrets), Env, null, false, Ct);

        Assert.Equal([new LocalValue("A", "service"), new LocalValue("B", "g", ConfigScope.Global)], s.ValuesTarget.Writes.Single().Values);
    }

    [Fact]
    public async Task PullAsync_WritesEntryScope_ForValuesAndSecrets()
    {
        var s = Create([Plain("G", "g", ConfigScope.Global), Plain("S", "s"), new("Db:Password", ConfigScope.Global, new ConfigValue.KeyVaultRef(SecretUri))]);

        await s.Service.PullAsync(Binding(Values, Secrets), Env, null, false, Ct);

        Assert.Equal([new LocalValue("G", "g", ConfigScope.Global), new LocalValue("S", "s")], s.ValuesTarget.Writes.Single().Values);
        Assert.Equal([new LocalValue("Db:Password", SecretText, ConfigScope.Global)], s.SecretsTarget.Writes.Single().Values);
    }

    [Fact]
    public async Task PullAsync_UnsafeSecretsTarget_ThrowsAndWritesNothing()
    {
        var s = Create([Plain("A", "1"), Secret("Db:Password")], secretsSafe: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.PullAsync(Binding(Values, Secrets), Env, null, false, Ct));

        Assert.DoesNotContain(SecretText, ex.Message);
        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Empty(s.SecretsTarget.Writes);
        Assert.Null(s.State.SavedSnapshot);
        Assert.Null(s.State.Binding);
    }

    [Fact]
    public async Task PullAsync_SavesSnapshotWithFingerprintInsteadOfSecretValue()
    {
        var s = Create([Plain("A", "1"), Secret("Db:Password")]);

        await s.Service.PullAsync(Binding(Values, Secrets), Env, null, false, Ct);

        var (environment, snapshot) = s.State.SavedSnapshot!.Value;
        Assert.Equal("dev", environment);
        Assert.Equal(new StateValue.Plain("1"), snapshot.Items["A"].Value);
        var secret = Assert.IsType<StateValue.Secret>(snapshot.Items["Db:Password"].Value);
        Assert.Equal(SecretUri, secret.SecretUri);
        Assert.False(string.IsNullOrEmpty(secret.Fingerprint));
        Assert.DoesNotContain(SecretText, JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task PullAsync_SetsActiveEnvironment()
    {
        var s = Create([]);

        await s.Service.PullAsync(Binding(Values, Secrets), Env, null, false, Ct);

        Assert.Equal("dev", s.State.Binding!.ActiveEnvironment);
    }

    private static ConfigState Snapshot(params StateItem[] items) => new(items.ToDictionary(i => i.Key, StringComparer.Ordinal));
    private static StateItem PlainItem(string key, string value) => new(key, ConfigScope.Service, new StateValue.Plain(value));

    [Theory]
    [InlineData("A", "changed", "A")]
    [InlineData("B", "1", "A,B")]
    public async Task PullAsync_LocalModifiedAddedOrDeleted_BlocksWithoutWriting(string key, string value, string expected)
    {
        var s = Create([Plain("A", "remote")]);
        s.State.Snapshots["dev"] = Snapshot(PlainItem("A", "1"));
        s.ValuesTarget.Contents = [new(key, value)];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, "dev"), Env, null, false, Ct);

        Assert.True(result.Blocked);
        Assert.Equal(expected.Split(','), result.LocalChanges);
        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Empty(s.SecretsTarget.Writes);
        Assert.Null(s.State.SavedSnapshot);
    }

    [Fact]
    public async Task PullAsync_LocalSecretChange_BlocksWithoutLeakingValue()
    {
        var s = Create([Secret("Db:Password")]);
        var fingerprint = new HmacSecretFingerprinter(new byte[32]).Fingerprint(new SecretString("old"));
        s.State.Snapshots["dev"] = Snapshot(new StateItem("Db:Password", ConfigScope.Service, new StateValue.Secret(SecretUri, fingerprint)));
        s.SecretsTarget.Contents = [new("Db:Password", SecretText)];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, "dev"), Env, null, false, Ct);

        Assert.Equal(["Db:Password"], result.LocalChanges);
        Assert.DoesNotContain(SecretText, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task PullAsync_LocalChangesWithForce_Overwrites()
    {
        var s = Create([Plain("A", "remote")]);
        s.State.Snapshots["dev"] = Snapshot(PlainItem("A", "1"));
        s.ValuesTarget.Contents = [new("A", "local")];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, "dev"), Env, null, true, Ct);

        Assert.False(result.Blocked);
        Assert.Equal([new LocalValue("A", "remote")], s.ValuesTarget.Writes.Single().Values);
    }

    [Fact]
    public async Task PullAsync_NoLocalChanges_DoesNotBlock()
    {
        var s = Create([Plain("A", "remote")]);
        s.State.Snapshots["dev"] = Snapshot(PlainItem("A", "1"));
        s.ValuesTarget.Contents = [new("A", "1")];

        Assert.False((await s.Service.PullAsync(Binding(Values, Secrets, "dev"), Env, null, false, Ct)).Blocked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dev")]
    public async Task PullAsync_NoActiveEnvironmentOrSnapshot_DoesNotBlock(string? active)
    {
        var s = Create([Plain("A", "remote")]);
        s.ValuesTarget.Contents = [new("A", "local")];

        Assert.False((await s.Service.PullAsync(Binding(Values, Secrets, active), Env, null, false, Ct)).Blocked);
        Assert.Single(s.ValuesTarget.Writes);
    }

    [Fact]
    public async Task PullAsync_SameTarget_ReadsOnce()
    {
        var s = Create([Plain("A", "remote")]);
        s.State.Snapshots["dev"] = Snapshot(PlainItem("A", "1"));
        s.SecretsTarget.Contents = [new("A", "1")];

        await s.Service.PullAsync(Binding(Secrets, Secrets, "dev"), Env, null, false, Ct);

        Assert.Equal(1, s.SecretsTarget.Reads);
    }

    [Fact]
    public async Task PullAsync_IgnoredKeys_KeepLocalValuesInTheirTargetsAndSkipRemote()
    {
        var s = Create([Plain("A", "1"), Plain("Logging:Level", "Info"), Secret("Db:Password")]);
        s.ValuesTarget.Contents = [new("Logging:Level", "Debug"), new("Local:Only", "x")];
        s.SecretsTarget.Contents = [new("Db:Password", "mine")];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, ignore: ["logging:*", "Db:*"]), Env, null, false, Ct);

        Assert.Equal([new LocalValue("A", "1"), new LocalValue("Logging:Level", "Debug")], s.ValuesTarget.Writes.Single().Values);
        Assert.Equal([new LocalValue("Db:Password", "mine")], s.SecretsTarget.Writes.Single().Values);
        Assert.Equal(["Db:Password", "Logging:Level"], result.Ignored);
        Assert.Equal((1, 0), (result.ValuesWritten, result.SecretsWritten));
        Assert.Equal(["A"], s.State.SavedSnapshot!.Value.State.Items.Keys);
    }

    [Fact]
    public async Task PullAsync_IgnoredRemoteKeyMissingLocally_IsNotWritten()
    {
        var s = Create([Plain("A", "1"), Secret("Db:Password")]);

        var result = await s.Service.PullAsync(Binding(Values, Secrets, ignore: ["Db:Password"]), Env, null, false, Ct);

        Assert.Empty(s.SecretsTarget.Writes.Single().Values);
        Assert.Equal(["Db:Password"], result.Ignored);
        Assert.DoesNotContain("Db:Password", s.State.SavedSnapshot!.Value.State.Items.Keys);
    }

    [Fact]
    public async Task PullAsync_GlobalPattern_IgnoresOnlyKeysFromGlobal()
    {
        var s = Create([Plain("G", "1", ConfigScope.Global), Plain("S", "2"), Plain("O", "g", ConfigScope.Global), Plain("O", "s")]);
        s.ValuesTarget.Contents = [new("G", "mine"), new("S", "old"), new("O", "old")];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, ignore: ["global:*"]), Env, null, false, Ct);

        Assert.Equal([new LocalValue("O", "s"), new LocalValue("S", "2"), new LocalValue("G", "mine")], s.ValuesTarget.Writes.Single().Values);
        Assert.Equal(["G"], result.Ignored);
    }

    [Fact]
    public async Task PullAsync_SameTarget_KeepsIgnoredLocalValue()
    {
        var s = Create([Plain("A", "r"), Plain("C", "c")]);
        s.SecretsTarget.Contents = [new("A", "mine"), new("B", "x")];

        await s.Service.PullAsync(Binding(Secrets, Secrets, ignore: ["A"]), Env, null, false, Ct);

        Assert.Equal([new LocalValue("C", "c"), new LocalValue("A", "mine")], Assert.Single(s.SecretsTarget.Writes).Values);
        Assert.Equal(1, s.SecretsTarget.Reads);
    }

    [Fact]
    public async Task PullAsync_IgnoredLocalChange_DoesNotBlock()
    {
        var s = Create([Plain("A", "remote"), Plain("L", "r")]);
        s.State.Snapshots["dev"] = Snapshot(PlainItem("A", "1"), PlainItem("L", "1"));
        s.ValuesTarget.Contents = [new("A", "1"), new("L", "changed"), new("N", "new")];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, "dev", ["L", "N"]), Env, null, false, Ct);

        Assert.False(result.Blocked);
        Assert.Equal([new LocalValue("A", "remote"), new LocalValue("L", "changed"), new LocalValue("N", "new")], s.ValuesTarget.Writes.Single().Values);
    }

    [Fact]
    public async Task PullAsync_IgnoredGlobalKeyMissingFromSnapshot_DoesNotBlock()
    {
        var s = Create([Plain("G", "1", ConfigScope.Global)]);
        s.State.Snapshots["dev"] = Snapshot();
        s.ValuesTarget.Contents = [new("G", "mine")];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, "dev", ["global:G"]), Env, null, false, Ct);

        Assert.False(result.Blocked);
        Assert.Equal([new LocalValue("G", "mine")], s.ValuesTarget.Writes.Single().Values);
    }

    [Fact]
    public async Task PullAsync_NotIgnoredLocalChange_StillBlocks()
    {
        var s = Create([Plain("A", "remote")]);
        s.State.Snapshots["dev"] = Snapshot(PlainItem("A", "1"));
        s.ValuesTarget.Contents = [new("A", "changed"), new("L", "x")];

        var result = await s.Service.PullAsync(Binding(Values, Secrets, "dev", ["L"]), Env, null, false, Ct);

        Assert.True(result.Blocked);
        Assert.Equal(["A"], result.LocalChanges);
    }
}
