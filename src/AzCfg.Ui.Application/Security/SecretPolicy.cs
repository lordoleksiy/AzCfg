using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Security;

/// <summary>Secret rules of the UI (AD-6).</summary>
public static class SecretPolicy
{
    public const string Mask = "••••••";

    /// <summary>
    /// AD-6 (3): <c>true</c> when the diff's Remote/Base value is <see cref="StateValue.Secret"/>, or <paramref name="remote"/>
    /// is a <see cref="ConfigValue.KeyVaultRef"/>/<see cref="ConfigValue.ResolvedSecret"/>, or <paramref name="localSource"/>
    /// == <c>binding.SecretsTarget</c> while <c>binding.SecretsTarget</c> != <c>binding.ValuesTarget</c>.
    /// </summary>
    public static bool IsSecret(DiffItem? diff, ConfigEntry? remote, LocalTargetBinding? localSource, ProjectBinding binding) =>
        diff?.Remote?.Value is StateValue.Secret
        || diff?.Base?.Value is StateValue.Secret
        || remote?.Value is ConfigValue.KeyVaultRef or ConfigValue.ResolvedSecret
        || (localSource is not null && localSource == binding.SecretsTarget && binding.SecretsTarget != binding.ValuesTarget);
}
