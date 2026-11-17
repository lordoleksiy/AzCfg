using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

public interface ISetupService
{
    /// <summary>Returns one candidate per App Configuration store; the environment comes from the <c>environment</c> tag, else the name suffix, else <c>null</c>.</summary>
    Task<IReadOnlyList<EnvironmentCandidate>> DiscoverEnvironmentsAsync(CancellationToken ct);
    Task SaveProfileAsync(UserProfile profile, CancellationToken ct);

    /// <summary>Distinct non-empty labels used in <paramref name="store"/> (offered by <c>init</c>, ADR 0008).</summary>
    Task<IReadOnlyList<string>> ListLabelsAsync(Uri store, CancellationToken ct);
}
