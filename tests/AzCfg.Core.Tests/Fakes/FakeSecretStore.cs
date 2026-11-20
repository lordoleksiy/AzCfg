using AzCfg.Core.Abstractions;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeSecretStore(Dictionary<Uri, string> values, List<string>? calls = null) : ISecretStore
{
    public List<(Uri Vault, string Name, string Value)> Sets { get; } = [];
    public List<Uri> ExistsCalls { get; } = [];
    public HashSet<Uri> ExistsThrows { get; } = [];
    public HashSet<Uri> Missing { get; } = [];

    public Task<string> GetAsync(Uri secretUri, CancellationToken ct) => Task.FromResult(values[secretUri]);

    public Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct)
    {
        calls?.Add($"secret:{name}");
        Sets.Add((vaultUri, name, value));
        return Task.FromResult(new Uri($"{vaultUri.AbsoluteUri}secrets/{name}/v2"));
    }

    public Task<bool> ExistsAsync(Uri secretUri, CancellationToken ct)
    {
        ExistsCalls.Add(secretUri);
        return ExistsThrows.Contains(secretUri)
            ? throw new UnauthorizedAccessException("no access, value=CANARY")
            : Task.FromResult(!Missing.Contains(secretUri));
    }
}
