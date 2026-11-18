using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Security;

namespace AzCfg.Ui.Tests.Application.Security;

public sealed class SecretPolicyTests
{
    private static readonly Uri SecretUri = new("https://kv.vault.azure.net/secrets/crm--Api--Key");
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);

    private static ProjectBinding Binding(LocalTargetBinding values, LocalTargetBinding secrets) =>
        new("C:/src/Crm", "crm", values, secrets, new Dictionary<string, Uri>());

    private static StateItem Item(StateValue value) => new("Api:Key", ConfigScope.Service, value);

    [Fact]
    public void IsSecret_PlainEverywhere_False() =>
        Assert.False(SecretPolicy.IsSecret(
            new DiffItem("Api:Key", DiffKind.Unchanged, Item(new StateValue.Plain("a")), Item(new StateValue.Plain("a")), Item(new StateValue.Plain("a"))),
            new ConfigEntry("Api:Key", ConfigScope.Service, new ConfigValue.Plain("a")),
            Values,
            Binding(Values, Secrets)));

    [Fact]
    public void IsSecret_RemoteDiffSideSecret_True() =>
        Assert.True(SecretPolicy.IsSecret(
            new DiffItem("Api:Key", DiffKind.RemoteModified, null, null, Item(new StateValue.Secret(SecretUri, "fp"))), null, null, Binding(Values, Secrets)));

    [Fact]
    public void IsSecret_BaseDiffSideSecret_True() =>
        Assert.True(SecretPolicy.IsSecret(
            new DiffItem("Api:Key", DiffKind.RemoteDeleted, Item(new StateValue.Secret(SecretUri, "fp")), null, null), null, null, Binding(Values, Secrets)));

    [Fact]
    public void IsSecret_RemoteKeyVaultRef_True() =>
        Assert.True(SecretPolicy.IsSecret(null, new ConfigEntry("Api:Key", ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri)), null, Binding(Values, Secrets)));

    [Fact]
    public void IsSecret_RemoteResolvedSecret_True() =>
        Assert.True(SecretPolicy.IsSecret(
            null, new ConfigEntry("Api:Key", ConfigScope.Service, new ConfigValue.ResolvedSecret(SecretUri, new SecretString("s"))), null, Binding(Values, Secrets)));

    [Fact]
    public void IsSecret_LocalFromSeparateSecretsTarget_True() =>
        Assert.True(SecretPolicy.IsSecret(null, null, Secrets, Binding(Values, Secrets)));

    [Fact]
    public void IsSecret_LocalFromSharedTarget_False() =>
        Assert.False(SecretPolicy.IsSecret(null, null, Secrets, Binding(Secrets, Secrets)));

    [Fact]
    public void IsSecret_LocalFromValuesTarget_False() =>
        Assert.False(SecretPolicy.IsSecret(null, null, Values, Binding(Values, Secrets)));
}
