using System.Text.Json;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Health;
using Azure.Core;
using Azure.Identity;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// Checks the Azure sign-in by requesting a management-scope token from the credential. Only the account name and tenant id are
/// read from the token's claims; the token itself is never stored, logged or returned. Any failure is a signed-out result.
/// </summary>
public sealed class AzureAccountProbe(TokenCredential credential) : IAzureAccountProbe
{
    private const string ManagementScope = "https://management.azure.com/.default";
    private static readonly string[] AccountClaims = ["upn", "unique_name", "preferred_username"];

    public async Task<AzureAccountCheck> CheckAsync(CancellationToken ct)
    {
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext([ManagementScope]), ct);
            return new(true, ReadAccount(token.Token), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Type names only: exception messages of credential chains can be long and may carry environment details.
            return new(false, null, ex is AuthenticationFailedException or CredentialUnavailableException
                ? "no Azure credential available (run 'az login')"
                : ex.GetType().Name);
        }
    }

    /// <summary>Account name (and tenant id) from the claims of a JWT, or <c>null</c> when it is not a readable JWT.</summary>
    internal static string? ReadAccount(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = doc.RootElement;
            var name = AccountClaims
                .Select(c => root.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            var tenant = root.TryGetProperty("tid", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            return (name, tenant) switch
            {
                (null, null) => null,
                (null, _) => $"tenant {tenant}",
                (_, null) => name,
                _ => $"{name} (tenant {tenant})",
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Convert.FromBase64String(s);
    }
}
