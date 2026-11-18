using System.Buffers.Text;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;

namespace AzCfg.Infrastructure.InMemory;

/// <summary>
/// Credential for tests and <c>--demo</c>: every request returns a fresh unsigned JWT (<c>alg: none</c>, empty signature) whose
/// payload has <c>upn</c> and <c>preferred_username</c> = <paramref name="account"/>, <c>tid</c> = <paramref name="tenantId"/>,
/// <c>iat</c>/<c>nbf</c> = now and <c>exp</c> = now + 1 hour (also <see cref="AccessToken.ExpiresOn"/>). Never calls Azure.
/// </summary>
public sealed class StaticTokenCredential(string account = "demo@azcfg.local", string tenantId = "00000000-0000-0000-0000-000000000000") : TokenCredential
{
    private static readonly string Header = Base64Url.EncodeToString("""{"alg":"none","typ":"JWT"}"""u8);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Create();

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Create());

    private AccessToken Create()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresOn = now.AddHours(1);
        var payload = new JsonObject
        {
            ["upn"] = account,
            ["preferred_username"] = account,
            ["tid"] = tenantId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresOn.ToUnixTimeSeconds(),
        };
        return new AccessToken($"{Header}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload.ToJsonString()))}.", expiresOn);
    }
}
