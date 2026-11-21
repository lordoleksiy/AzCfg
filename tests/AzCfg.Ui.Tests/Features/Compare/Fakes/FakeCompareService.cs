using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Tests.Features.Compare.Fakes;

/// <summary>Records every call; answers with <see cref="Rows"/> and the JSON texts, or throws <see cref="Failure"/>.</summary>
internal sealed class FakeCompareService : ICompareService
{
    public List<(CompareSide Left, CompareSide Right)> Calls { get; } = [];

    public IReadOnlyList<CompareRow> Rows { get; set; } = [];

    public int IgnoredCount { get; set; }

    public string LeftJson { get; set; } = "{}";

    public string RightJson { get; set; } = "{}";

    public Exception? Failure { get; set; }

    public MissingKeysView? Matrix { get; set; }

    public int MatrixCalls { get; private set; }

    public Task<MissingKeysView> MissingKeysAsync(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> environments, CancellationToken ct)
    {
        MatrixCalls++;
        return Task.FromResult(Matrix ?? new MissingKeysView(binding, environments, [], 0, DateTimeOffset.UnixEpoch));
    }

    public Task<ComparisonView> CompareAsync(ProjectBinding binding, CompareSide left, CompareSide right, CancellationToken ct)
    {
        Calls.Add((left, right));
        return Failure is { } failure
            ? Task.FromException<ComparisonView>(failure)
            : Task.FromResult(new ComparisonView(binding, left, right, Rows, IgnoredCount, LeftJson, RightJson, DateTimeOffset.UnixEpoch));
    }
}
