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

    private static ProjectBinding Binding(LocalTargetBinding values, LocalTargetBinding secrets) =>
        new("/repo", "orders", values, secrets, new Dictionary<string, Uri>());

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

        var result = await s.Service.PullAsync(Binding(Values, Secrets), Env, null, Ct);

        var (valuesTarget, values) = Assert.Single(s.ValuesTarget.Writes);
        Assert.Equal(Values, valuesTarget);
        Assert.Equal([new LocalValue("Logging:Level", "Info")], values);
        var (secretsTarget, secrets) = Assert.Single(s.SecretsTarget.Writes);
        Assert.Equal(Secrets, secretsTarget);
        Assert.Equal([new LocalValue("Db:Password", SecretText)], secrets);
        Assert.Equal(new PullResult("dev", 1, 1, false, []), result);
    }

    [Fact]
    public async Task PullAsync_SameTarget_WritesOnce()
    {
        var s = Create([Plain("Logging:Level", "Info"), Secret("Db:Password")]);

        await s.Service.PullAsync(Binding(Secrets, Secrets), Env, null, Ct);

        var (_, written) = Assert.Single(s.SecretsTarget.Writes);
        Assert.Equal(["Logging:Level", "Db:Password"], written.Select(v => v.Key));
        Assert.Empty(s.ValuesTarget.Writes);
    }

    [Fact]
    public async Task PullAsync_ServiceOverridesGlobal()
    {
        var s = Create([Plain("A", "global", ConfigScope.Global), Plain("A", "service"), Plain("B", "g", ConfigScope.Global)]);

        await s.Service.PullAsync(Binding(Values, Secrets), Env, null, Ct);

        Assert.Equal([new LocalValue("A", "service"), new LocalValue("B", "g")], s.ValuesTarget.Writes.Single().Values);
    }

    [Fact]
    public async Task PullAsync_UnsafeSecretsTarget_ThrowsAndWritesNothing()
    {
        var s = Create([Plain("A", "1"), Secret("Db:Password")], secretsSafe: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.PullAsync(Binding(Values, Secrets), Env, null, Ct));

        Assert.DoesNotContain(SecretText, ex.Message);
        Assert.Empty(s.ValuesTarget.Writes);
        Assert.Empty(s.SecretsTarget.Writes);
        Assert.Null(s.State.SavedSnapshot);
    }

    [Fact]
    public async Task PullAsync_SavesSnapshotWithFingerprintInsteadOfSecretValue()
    {
        var s = Create([Plain("A", "1"), Secret("Db:Password")]);

        await s.Service.PullAsync(Binding(Values, Secrets), Env, null, Ct);

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

        await s.Service.PullAsync(Binding(Values, Secrets), Env, null, Ct);

        Assert.Equal("dev", s.State.Binding!.ActiveEnvironment);
    }
}
