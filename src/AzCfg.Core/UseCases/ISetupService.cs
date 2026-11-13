using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

public interface ISetupService
{
    Task<IReadOnlyList<ConfigEnvironment>> DiscoverEnvironmentsAsync(CancellationToken ct);
    Task SaveProfileAsync(UserProfile profile, CancellationToken ct);
}
