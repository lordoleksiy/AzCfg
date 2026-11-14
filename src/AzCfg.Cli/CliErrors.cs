using Azure;
using Azure.Identity;

namespace AzCfg.Cli;

/// <summary>Turns exceptions into user-facing messages; auth failures and 401/403 name the role or step needed.</summary>
public static class CliErrors
{
    public static string Describe(Exception ex) => ex switch
    {
        AuthenticationFailedException => "Azure authentication failed. Sign in with 'az login' (or configure DefaultAzureCredential) and retry.",
        RequestFailedException { Status: 401 } => "Azure rejected the credentials (401). Sign in again with 'az login'.",
        RequestFailedException { Status: 403 } => "Access denied (403). Required roles: 'App Configuration Data Owner' on the store; 'Key Vault Secrets User' (read) or 'Key Vault Secrets Officer' (write) on the vault.",
        _ => ex.Message,
    };

    public static string Format(Exception ex, bool json) =>
        json ? CliJson.Serialize(new { error = Describe(ex) }) : $"Error: {Describe(ex)}";
}
