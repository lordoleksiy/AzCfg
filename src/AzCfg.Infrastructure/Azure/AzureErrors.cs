using System.Text.Json;
using Azure;
using Azure.Identity;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// Turns Azure and validation exceptions into user-facing messages and error kinds; shared by the CLI and the UI.
/// Auth failures and 401/403 name the role or step needed.
/// </summary>
public static class AzureErrors
{
    private const string NetworkText = "Cannot reach Azure. Check the network connection, proxy or VPN and retry.";

    /// <summary>Today's CLI texts unchanged, plus 404, 412 and network texts; anything else returns the exception message.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        AuthenticationFailedException => "Azure authentication failed. Sign in with 'az login' (or configure DefaultAzureCredential) and retry.",
        RequestFailedException { Status: 401 } => "Azure rejected the credentials (401). Sign in again with 'az login'.",
        RequestFailedException { Status: 403 } => "Access denied (403). Required roles: 'App Configuration Data Owner' on the store; 'Key Vault Secrets User' (read) or 'Key Vault Secrets Officer' (write) on the vault.",
        RequestFailedException { Status: 404 } => $"Not found (404). Check the store endpoint, vault and key names. {ex.Message}",
        RequestFailedException { Status: 412 } => "The setting was changed in Azure after it was read (412). Reload and retry.",
        _ when Classify(ex) == AzureErrorKind.Network => NetworkText,
        _ => ex.Message,
    };

    /// <summary>
    /// <see cref="AuthenticationFailedException"/>, <see cref="CredentialUnavailableException"/>, 401 → Auth; 403 → Forbidden;
    /// 404 → NotFound; 412 → PreconditionFailed; <see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/> not
    /// caused by the token, <see cref="RequestFailedException"/> with status 0 → Network; <see cref="ArgumentException"/>,
    /// <see cref="JsonException"/>, <see cref="InvalidOperationException"/> → Validation; anything else → Other.
    /// </summary>
    public static AzureErrorKind Classify(Exception ex) => ex switch
    {
        AuthenticationFailedException or RequestFailedException { Status: 401 } => AzureErrorKind.Auth,
        RequestFailedException { Status: 403 } => AzureErrorKind.Forbidden,
        RequestFailedException { Status: 404 } => AzureErrorKind.NotFound,
        RequestFailedException { Status: 412 } => AzureErrorKind.PreconditionFailed,
        RequestFailedException { Status: 0 } or HttpRequestException => AzureErrorKind.Network,
        TaskCanceledException { CancellationToken.IsCancellationRequested: false } => AzureErrorKind.Network,
        ArgumentException or JsonException or InvalidOperationException => AzureErrorKind.Validation,
        _ => AzureErrorKind.Other,
    };
}
