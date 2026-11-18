using System.Buffers.Text;
using System.Text.Json;

namespace AzCfg.Ui.Application.Environments;

/// <summary>Reads display-only claims from a JWT payload without validating it. Never throws; the token is not retained.</summary>
internal static class JwtClaims
{
    private static readonly string[] AccountClaims = ["upn", "unique_name", "preferred_username"];

    public static (string? Account, string? TenantId) Read(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return (null, null);
            }

            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            var root = payload.RootElement;
            return (AccountClaims.Select(c => Claim(root, c)).FirstOrDefault(v => v is not null), Claim(root, "tid"));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    private static string? Claim(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
