using AzCfg.Core.Abstractions;
using AzCfg.Core.Health;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>Reports a signed-in account unless a scenario signs it out (no token, no Azure).</summary>
public sealed class FixedAzureAccountProbe : IAzureAccountProbe
{
    public AzureAccountCheck Result { get; set; } = new(true, "dev@contoso.com", null);

    public Task<AzureAccountCheck> CheckAsync(CancellationToken ct) => Task.FromResult(Result);
}
