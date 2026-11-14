using AzCfg.Core.Abstractions;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeSecretStore(Dictionary<Uri, string> values) : ISecretStore
{
    public Task<string> GetAsync(Uri secretUri, CancellationToken ct) => Task.FromResult(values[secretUri]);
    public Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct) => throw new NotSupportedException();
}
