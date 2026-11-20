using System.Text;
using AzCfg.Infrastructure.Azure;
using Azure.Core;

namespace AzCfg.Infrastructure.Tests.Azure;

public sealed class AzureAccountProbeTests
{
    private sealed class StubCredential(Func<AccessToken> token) : TokenCredential
    {
        public List<string[]> Scopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Get(requestContext);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Get(requestContext));

        private AccessToken Get(TokenRequestContext context)
        {
            Scopes.Add(context.Scopes);
            return token();
        }
    }

    private static string Jwt(string payloadJson)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(payloadJson)}.sig";
    }

    [Fact]
    public async Task CheckAsync_ReadsAccountAndTenant_AndUsesManagementScope()
    {
        var credential = new StubCredential(() => new(Jwt("""{"upn":"dev@contoso.com","tid":"t-1"}"""), DateTimeOffset.UtcNow.AddHours(1)));

        var check = await new AzureAccountProbe(credential).CheckAsync(default);

        Assert.True(check.SignedIn);
        Assert.Equal("dev@contoso.com (tenant t-1)", check.Account);
        Assert.Null(check.Error);
        Assert.Equal(["https://management.azure.com/.default"], Assert.Single(credential.Scopes));
    }

    [Theory]
    [InlineData("""{"unique_name":"u@x.com"}""", "u@x.com")]
    [InlineData("""{"preferred_username":"p@x.com","tid":"t"}""", "p@x.com (tenant t)")]
    [InlineData("""{"tid":"t"}""", "tenant t")]
    [InlineData("""{"other":1}""", null)]
    public void ReadAccount_PicksTheAccountClaim(string payload, string? expected) =>
        Assert.Equal(expected, AzureAccountProbe.ReadAccount(Jwt(payload)));

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.%%%.c")]
    [InlineData("a.bm90LWpzb24.c")]
    public void ReadAccount_UnreadableToken_IsNull(string token) => Assert.Null(AzureAccountProbe.ReadAccount(token));

    [Fact]
    public async Task CheckAsync_CredentialFails_IsSignedOutWithoutTheMessage()
    {
        var credential = new StubCredential(() => throw new InvalidOperationException("secret-detail-123"));

        var check = await new AzureAccountProbe(credential).CheckAsync(default);

        Assert.False(check.SignedIn);
        Assert.Null(check.Account);
        Assert.DoesNotContain("secret-detail-123", check.Error);
    }

    [Fact]
    public async Task CheckAsync_UnreadableToken_IsStillSignedIn()
    {
        var credential = new StubCredential(() => new("opaque", DateTimeOffset.UtcNow.AddHours(1)));

        var check = await new AzureAccountProbe(credential).CheckAsync(default);

        Assert.True(check.SignedIn);
        Assert.Null(check.Account);
    }
}
