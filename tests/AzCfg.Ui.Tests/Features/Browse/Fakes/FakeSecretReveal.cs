using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Tests.Features.Browse.Fakes;

/// <summary>Reveals <see cref="BrowseFixtures.Sentinel"/> for every secret and counts the calls.</summary>
internal sealed class FakeSecretReveal : ISecretRevealService
{
    public int Remote { get; private set; }

    public int Local { get; private set; }

    public Task<SecretString> RevealRemoteAsync(Uri secretUri, CancellationToken ct)
    {
        Remote++;
        return Task.FromResult(new SecretString(BrowseFixtures.Sentinel));
    }

    public Task<SecretString?> RevealLocalAsync(ProjectBinding binding, string key, CancellationToken ct)
    {
        Local++;
        return Task.FromResult<SecretString?>(new SecretString(BrowseFixtures.Sentinel));
    }
}
