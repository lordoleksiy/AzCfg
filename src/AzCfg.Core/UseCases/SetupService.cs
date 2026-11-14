using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

public sealed class SetupService(IResourceDiscovery discovery, IUserStateStore state) : ISetupService
{
    private static readonly (string Suffix, string Name)[] Suffixes =
        [("-dev", "dev"), ("-stg", "stage"), ("-stage", "stage"), ("-prod", "prod")];

    public async Task<IReadOnlyList<EnvironmentCandidate>> DiscoverEnvironmentsAsync(CancellationToken ct) =>
        [.. (await discovery.FindAppConfigStoresAsync(ct)).Select(ToCandidate)];

    public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) => state.SaveProfileAsync(profile, ct);

    private static EnvironmentCandidate ToCandidate(AzureResource store)
    {
        var name = store.Tags.TryGetValue("environment", out var tag) && !string.IsNullOrWhiteSpace(tag)
            ? tag
            : Suffixes.FirstOrDefault(s => store.Name.EndsWith(s.Suffix, StringComparison.OrdinalIgnoreCase)).Name;
        return new(name, store, name is not null && ConfigEnvironment.IsProtectedByName(name));
    }
}
