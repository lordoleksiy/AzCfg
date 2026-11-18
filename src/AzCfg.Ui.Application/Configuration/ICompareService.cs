using AzCfg.Core.Model;

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
}
