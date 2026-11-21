using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Configuration;

public interface ICompareService
{
    /// <summary>
    /// Remote↔Remote: two <c>ReadRemoteConfigAsync</c> + <c>EnvironmentComparer</c> (secrets by name). Local↔Remote (either
    /// order): <c>DiffAsync(binding, env)</c>, Local vs Remote ignoring Base; a local secret equals a remote one when
    /// <c>ISecretFingerprinter.Fingerprint(local)</c> == remote fingerprint; ignored keys excluded (<c>IgnoredCount</c>).
    /// Local↔Local → <see cref="ArgumentException"/>. JSON per <see cref="IConfigurationService.ToJson"/> rules.
    /// </summary>
    Task<ComparisonView> CompareAsync(ProjectBinding binding, CompareSide left, CompareSide right, CancellationToken ct);

    /// <summary>
    /// Consistency matrix: <c>ReadRemoteConfigAsync</c> per environment (ignored keys dropped, as <c>diff --missing --all</c>) +
    /// <c>MissingKeysReport.Compute</c> for the gaps. Every key is a row; cells hold plain values or the secret name, never a
    /// secret value. A row is global when any of its entries is global (such keys are not copied).
    /// </summary>
    Task<MissingKeysView> MissingKeysAsync(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> environments, CancellationToken ct);
}
