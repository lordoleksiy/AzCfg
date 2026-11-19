using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceUnmanagedLocalKeysTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);

    private static ProjectBinding Binding(LocalTargetBinding values, LocalTargetBinding secrets, string? active = null) =>
        new("/repo", "orders", values, secrets, new Dictionary<string, Uri>(), active);

    private static SyncService Create(params FakeLocalTarget[] targets) =>
        new(new FakeConfigStore([]), new FakeSecretStore([]), new FakeUserStateStore(), new HmacSecretFingerprinter(new byte[32]), targets);

    [Fact]
    public async Task NoActiveEnvironment_TwoTargets_ReturnsKeysOfBoth()
    {
        var user = new FakeLocalTarget(LocalTargetKind.UserSecrets) { Contents = [new("B:One", "1"), new("A", "2")] };
        var app = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal) { Contents = [new("Db:Password", "x"), new("A", "3")] };

        var keys = await Create(user, app).UnmanagedLocalKeysAsync(Binding(Values, Secrets), Ct);

        Assert.Equal(["A", "B:One", "Db:Password"], keys);
    }

    [Fact]
    public async Task NoActiveEnvironment_SharedTarget_ReadsOnce()
    {
        var user = new FakeLocalTarget(LocalTargetKind.UserSecrets) { Contents = [new("A", "1"), new("B", "2")] };

        var keys = await Create(user).UnmanagedLocalKeysAsync(Binding(Values, Values), Ct);

        Assert.Equal(["A", "B"], keys);
        Assert.Equal(1, user.Reads);
    }

    [Fact]
    public async Task ActiveEnvironment_ReturnsEmptyWithoutReading()
    {
        var user = new FakeLocalTarget(LocalTargetKind.UserSecrets) { Contents = [new("A", "1")] };

        var keys = await Create(user).UnmanagedLocalKeysAsync(Binding(Values, Values, "dev"), Ct);

        Assert.Empty(keys);
        Assert.Equal(0, user.Reads);
    }

    [Fact]
    public async Task NoActiveEnvironment_EmptyTargets_ReturnsEmpty() =>
        Assert.Empty(await Create(new FakeLocalTarget(LocalTargetKind.UserSecrets)).UnmanagedLocalKeysAsync(Binding(Values, Values), Ct));
}
