using AzCfg.Core.Abstractions;
using AzCfg.Core.Health;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeAzureAccountProbe(AzureAccountCheck? result = null) : IAzureAccountProbe
{
    public AzureAccountCheck Result { get; set; } = result ?? new(true, "dev@contoso.com", null);
    public bool Throws { get; set; }

    public Task<AzureAccountCheck> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Throws ? throw new InvalidOperationException("boom, value=CANARY") : Task.FromResult(Result);
    }
}
