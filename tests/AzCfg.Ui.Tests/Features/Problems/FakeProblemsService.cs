using AzCfg.Ui.Application.Health;

namespace AzCfg.Ui.Tests.Features.Problems;

internal sealed class FakeProblemsService : IProblemsService
{
    public List<ProblemRow> Rows { get; } = [];

    public List<string> Calls { get; } = [];

    public Task<ProblemsReport> CheckAsync(string repoPath, CancellationToken ct)
    {
        Calls.Add(repoPath);
        return Task.FromResult(new ProblemsReport([.. Rows]));
    }
}
