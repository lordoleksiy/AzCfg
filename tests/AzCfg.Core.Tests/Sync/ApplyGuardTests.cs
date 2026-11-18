using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.Sync;

public sealed class ApplyGuardTests
{
    private static readonly LocalTargetBinding Local = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Local, Local, new Dictionary<string, Uri>());
    private static readonly Change Add = new(ChangeKind.Add, null, new("A", ConfigScope.Service, new ConfigValue.Plain("1")));
    private static readonly Conflict Conflict = new("A", null, null, null);

    private static ConfigEnvironment Env(bool isProtected = false) => new(isProtected ? "prod" : "dev", new Uri("https://x.azconfig.io"), null, isProtected);

    private static SyncPlan Plan(bool conflicts = false, bool vaultChoice = false, bool isProtected = false, bool global = false) =>
        new(Binding, Env(isProtected), [Add], conflicts ? [Conflict] : [], vaultChoice, global);

    [Fact]
    public void Check_CleanPlan_ReturnsNoBlockers() => Assert.Empty(ApplyGuard.Check(Plan(), new()));

    [Fact]
    public void Check_EveryBlocker_InOrder() => Assert.Equal(
        [ApplyBlocker.Conflicts, ApplyBlocker.VaultChoiceRequired, ApplyBlocker.EnvironmentNotConfirmed, ApplyBlocker.GlobalNotConfirmed],
        ApplyGuard.Check(Plan(conflicts: true, vaultChoice: true, isProtected: true, global: true), new()));

    [Theory]
    [InlineData(true, false, false, false, ApplyBlocker.Conflicts)]
    [InlineData(false, true, false, false, ApplyBlocker.VaultChoiceRequired)]
    [InlineData(false, false, true, false, ApplyBlocker.EnvironmentNotConfirmed)]
    [InlineData(false, false, false, true, ApplyBlocker.GlobalNotConfirmed)]
    public void Check_SingleCondition_ReturnsItsBlocker(bool conflicts, bool vaultChoice, bool isProtected, bool global, ApplyBlocker expected) =>
        Assert.Equal([expected], ApplyGuard.Check(Plan(conflicts, vaultChoice, isProtected, global), new()));

    [Theory]
    [InlineData("PROD")]
    [InlineData("prod")]
    public void Check_ProtectedConfirmedIgnoringCase_NotBlocked(string confirmed) =>
        Assert.Empty(ApplyGuard.Check(Plan(isProtected: true), new(confirmed)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev")]
    public void Check_ProtectedWithoutMatchingName_Blocked(string? confirmed) =>
        Assert.Equal([ApplyBlocker.EnvironmentNotConfirmed], ApplyGuard.Check(Plan(isProtected: true), new(confirmed)));

    [Fact]
    public void Check_GlobalConfirmed_NotBlocked() => Assert.Empty(ApplyGuard.Check(Plan(global: true), new(ConfirmGlobal: true)));

    [Theory]
    [InlineData(true, false, false, false, "Plan has conflicts; resolve them before applying.")]
    [InlineData(false, true, false, false, "Choose a Key Vault first.")]
    [InlineData(false, false, true, false, "Applying to protected environment 'prod' requires typing its name.")]
    [InlineData(false, false, false, true, "Plan changes global keys; confirm global changes.")]
    [InlineData(true, true, true, true, "Plan has conflicts; resolve them before applying.")]
    [InlineData(false, true, true, true, "Choose a Key Vault first.")]
    public async Task ApplyAsync_Blocked_ThrowsMessageOfFirstBlockerBeforeAnyWrite(bool conflicts, bool vaultChoice, bool isProtected, bool global, string message)
    {
        List<string> calls = [];
        var service = new SyncService(
            new FakeConfigStore([], new() { ["dev"] = [], ["prod"] = [] }, calls),
            new FakeSecretStore([], calls),
            new FakeUserStateStore(),
            new HmacSecretFingerprinter(new byte[32]),
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(Plan(conflicts, vaultChoice, isProtected, global), new(), CancellationToken.None));

        Assert.Equal(message, ex.Message);
        Assert.Empty(calls);
    }
}
