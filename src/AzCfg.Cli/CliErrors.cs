using AzCfg.Infrastructure.Azure;

namespace AzCfg.Cli;

/// <summary>Turns exceptions into user-facing messages (texts live in <see cref="AzureErrors"/>).</summary>
public static class CliErrors
{
    public static string Describe(Exception ex) => AzureErrors.Describe(ex);

    public static string Format(Exception ex, bool json) =>
        json ? CliJson.Serialize(new { error = Describe(ex) }) : $"Error: {Describe(ex)}";
}
