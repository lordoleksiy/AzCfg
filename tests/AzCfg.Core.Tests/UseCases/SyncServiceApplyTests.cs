using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceApplyTests
{
    private const string SecretText = "n3w-secret";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri Vault = new("https://vault.vault.azure.net/");
    private static readonly LocalTargetBinding Local = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Local, Local, new Dictionary<string, Uri>());

    private static ConfigEnvironment Env(string name = "dev", bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);
    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value), "etag");
    private static ConfigEntry Ref(string key) => new(key, ConfigScope.Service, new ConfigValue.KeyVaultRef(new($"{Vault}secrets/{key.Replace(':', '-')}")));

    private static Change CreateSecret(string key, ConfigEntry? before = null) => new(
        ChangeKind.CreateSecret, before, Ref(key), key.Replace(':', '-'), new SecretString(SecretText));

    private static SyncPlan Plan(Change[] changes, ConfigEnvironment? env = null, bool global = false, bool vaultChoice = false, Conflict[]? conflicts = null) =>
        new(Binding, env ?? Env(), changes, conflicts ?? [], vaultChoice, global);

    private sealed record Setup(SyncService Service, FakeConfigStore Store, FakeSecretStore Secrets, FakeUserStateStore State, List<string> Calls);

    private static Setup Create(params string[] failKeys)
    {
        List<string> calls = [];
        var secrets = new FakeSecretStore(new() { [new($"{Vault}secrets/Db-Password")] = SecretText }, calls);
        var store = new FakeConfigStore([], new() { ["dev"] = [Plain("A", "1"), Ref("Db:Password")], ["prod"] = [Plain("A", "1")] }, calls) { FailKeys = failKeys };
        var state = new FakeUserStateStore();
        return new(new(store, secrets, state, new HmacSecretFingerprinter(new byte[32]), []), store, secrets, state, calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev")]
    public async Task ApplyAsync_ProtectedWithoutMatchingConfirmation_ThrowsBeforeAnyWrite(string? confirmed)
    {
        var s = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.Service.ApplyAsync(Plan([CreateSecret("Db:Password")], Env("prod", true)), new(confirmed), Ct));

        Assert.Empty(s.Calls);
    }

    [Fact]
    public async Task ApplyAsync_ProtectedWithEnvironmentName_Applies()
    {
        var s = Create();

        var result = await s.Service.ApplyAsync(Plan([new(ChangeKind.Add, null, Plain("B", "2"))], Env("prod", true)), new("PROD"), Ct);

        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public async Task ApplyAsync_TouchesGlobalWithoutConfirmation_ThrowsBeforeAnyWrite()
    {
        var s = Create();
        var plan = Plan([CreateSecret("Db:Password")], global: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.ApplyAsync(plan, new(), Ct));
        Assert.Empty(s.Calls);

        await s.Service.ApplyAsync(plan, new(ConfirmGlobal: true), Ct);
        Assert.NotEmpty(s.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyAsync_ConflictsOrMissingVault_Throws(bool conflict)
    {
        var s = Create();
        var plan = Plan([CreateSecret("Db:Password") with { After = null }], vaultChoice: !conflict, conflicts: conflict ? [new("A", null, null, null)] : []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.ApplyAsync(plan, new(), Ct));

        Assert.Empty(s.Calls);
    }

    [Fact]
    public async Task ApplyAsync_CreatesSecretsInVaultBeforeAppConfig_WithNewReferenceUri()
    {
        var s = Create();
        var before = Plain("Db:Password", "old");

        await s.Service.ApplyAsync(Plan([new(ChangeKind.Add, null, Plain("B", "2")), CreateSecret("Db:Password", before), CreateSecret("Api:Key")]), new(), Ct);

        Assert.Equal(["secret:Db-Password", "secret:Api-Key", "apply"], s.Calls);
        Assert.Equal((Vault, "Db-Password", SecretText), s.Secrets.Sets[0]);
        var applied = s.Store.Applied!;
        Assert.Equal([ChangeKind.Add, ChangeKind.Update, ChangeKind.Add], applied.Select(c => c.Kind));
        Assert.Equal(before, applied[1].Before);
        Assert.Equal(new ConfigValue.KeyVaultRef(new($"{Vault}secrets/Db-Password/v2")), applied[1].After!.Value);
        Assert.All(applied, c => Assert.Null(c.SecretValue));
    }

    [Fact]
    public async Task ApplyAsync_PartialFailure_ReturnsFailedKeysAndKeepsSnapshot()
    {
        var s = Create("B");

        var result = await s.Service.ApplyAsync(Plan([new(ChangeKind.Add, null, Plain("B", "2")), new(ChangeKind.Add, null, Plain("C", "3"))]), new(), Ct);

        Assert.Equal(1, result.Applied);
        Assert.Equal(["B"], result.Failed);
        Assert.Null(s.State.SavedSnapshot);
    }

    [Fact]
    public async Task ApplyAsync_Success_UpdatesSnapshotOnlyForTouchedKeys()
    {
        var state = new FakeUserStateStore();
        state.Snapshots["dev"] = new(new Dictionary<string, StateItem>
        {
            ["X"] = new("X", ConfigScope.Service, new StateValue.Plain("old")),
            ["Y"] = new("Y", ConfigScope.Service, new StateValue.Plain("1")),
        });
        var target = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal) { Contents = [new("X", "old"), new("Y", "2")] };
        var service = new SyncService(
            new FakeConfigStore([], new() { ["dev"] = [Plain("X", "new"), Plain("Y", "2")] }),
            new FakeSecretStore([]), state, new HmacSecretFingerprinter(new byte[32]), [target]);

        await service.ApplyAsync(Plan([new(ChangeKind.Update, Plain("Y", "1"), Plain("Y", "2"))]), new(), Ct);
        state.Snapshots["dev"] = state.SavedSnapshot!.Value.State;
        var diff = await service.DiffAsync(Binding, Env(), Ct);

        Assert.Equal([("X", DiffKind.RemoteModified), ("Y", DiffKind.Unchanged)], diff.Items.Select(i => (i.Key, i.Kind)));
    }

    [Fact]
    public async Task ApplyAsync_Success_SavesSnapshotWithoutSecretValues()
    {
        var s = Create();

        await s.Service.ApplyAsync(Plan([CreateSecret("Db:Password")]), new(), Ct);

        var (environment, snapshot) = s.State.SavedSnapshot!.Value;
        Assert.Equal("dev", environment);
        Assert.IsType<StateValue.Secret>(snapshot.Items["Db:Password"].Value);
        Assert.DoesNotContain(SecretText, JsonSerializer.Serialize(snapshot));
    }
}
