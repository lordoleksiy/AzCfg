using AzCfg.Core.Health;

namespace AzCfg.Core.Abstractions;

public interface IAzureAccountProbe
{
    /// <summary>Whether a token can be obtained for Azure, and for which account. Never throws for a failed sign-in; never exposes the token.</summary>
    Task<AzureAccountCheck> CheckAsync(CancellationToken ct);
}
