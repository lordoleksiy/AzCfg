using AzCfg.Infrastructure.Azure;
using Azure;
using Azure.Security.KeyVault.Secrets;

namespace AzCfg.Infrastructure.Tests.Azure;

public sealed class KeyVaultSecretStoreTests
{
    [Theory]
    [InlineData("https://v1.vault.azure.net/secrets/db--pass/abc123", "https://v1.vault.azure.net/", "db--pass", "abc123")]
    [InlineData("https://v1.vault.azure.net/secrets/db--pass", "https://v1.vault.azure.net/", "db--pass", null)]
    public async Task GetAsync_ParsesVaultNameAndVersion(string uri, string vault, string name, string? version)
    {
        var (store, clients) = Create();

        var value = await store.GetAsync(new(uri), default);

        Assert.Equal("value", value);
        var client = Assert.Single(clients);
        Assert.Equal(new Uri(vault), client.Key);
        Assert.Equal((name, version), Assert.Single(client.Value.Gets));
    }

    [Fact]
    public async Task GetAsync_ReusesClientPerVault()
    {
        var (store, clients) = Create();

        await store.GetAsync(new("https://a.vault.azure.net/secrets/x"), default);
        await store.GetAsync(new("https://a.vault.azure.net/secrets/y/1"), default);
        await store.GetAsync(new("https://b.vault.azure.net/secrets/x"), default);

        Assert.Equal(2, clients.Count);
        Assert.Equal(2, clients[new("https://a.vault.azure.net/")].Gets.Count);
    }

    [Fact]
    public async Task SetAsync_ReturnsUriOfNewVersion()
    {
        var (store, clients) = Create();
        var vault = new Uri("https://v1.vault.azure.net/");

        var uri = await store.SetAsync(vault, "db--pass", "s3cret", default);

        Assert.Equal(new("https://v1.vault.azure.net/secrets/db--pass/v2"), uri);
        Assert.Equal(("db--pass", "s3cret"), Assert.Single(clients[vault].Sets));
    }

    [Theory]
    [InlineData("https://v1.vault.azure.net/secrets/db--pass", true)]
    [InlineData("https://v1.vault.azure.net/secrets/db--pass/v1", true)]
    [InlineData("https://v1.vault.azure.net/secrets/db--pass/old", false)]
    [InlineData("https://v1.vault.azure.net/secrets/none", false)]
    public async Task ExistsAsync_ListsVersionPropertiesOnly(string uri, bool expected)
    {
        var (store, clients) = Create();

        Assert.Equal(expected, await store.ExistsAsync(new(uri), default));

        Assert.Empty(clients.Values.Single().Gets);
    }

    [Fact]
    public async Task ExistsAsync_Other_failures_propagate()
    {
        var (store, _) = Create();

        await Assert.ThrowsAsync<RequestFailedException>(() => store.ExistsAsync(new("https://v1.vault.azure.net/secrets/forbidden"), default));
    }

    private static (KeyVaultSecretStore Store, Dictionary<Uri, FakeSecretClient> Clients) Create()
    {
        Dictionary<Uri, FakeSecretClient> clients = [];
        return (new(vault => clients[vault] = new(vault)), clients);
    }

    private sealed class FakeSecretClient(Uri vault) : SecretClient
    {
        public List<(string Name, string? Version)> Gets { get; } = [];
        public List<(string Name, string Value)> Sets { get; } = [];

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Gets.Add((name, version));
            return Task.FromResult(Response.FromValue(Secret(name, "v1", "value"), null!));
        }

        public override AsyncPageable<SecretProperties> GetPropertiesOfSecretVersionsAsync(string name, CancellationToken cancellationToken = default)
        {
            if (name == "forbidden")
            {
                throw new RequestFailedException(403, "denied");
            }

            if (name == "none")
            {
                throw new RequestFailedException(404, "not found");
            }

            var versions = new List<SecretProperties> { SecretModelFactory.SecretProperties(new($"{vault}secrets/{name}/v1"), vault, name, "v1") };
            return AsyncPageable<SecretProperties>.FromPages([Page<SecretProperties>.FromValues(versions, null, null!)]);
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            Sets.Add((name, value));
            return Task.FromResult(Response.FromValue(Secret(name, "v2", value), null!));
        }

        private KeyVaultSecret Secret(string name, string version, string value) =>
            SecretModelFactory.KeyVaultSecret(SecretModelFactory.SecretProperties(new($"{vault}secrets/{name}/{version}"), vault, name, version), value);
    }
}
