using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.UseCases;

public interface IConfigComparisonService
{
    /// <summary>
    /// Compares two sides. Local to environment: local files vs remote, secrets equal by fingerprint. Environment to
    /// environment: effective remote configurations, secrets equal by secret name. Local to Local throws
    /// <see cref="ArgumentException"/>. Secret values are never returned.
    /// </summary>
    Task<ConfigComparison> CompareAsync(ProjectBinding binding, CompareSide left, CompareSide right, CancellationToken ct);
}
