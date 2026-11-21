using AzCfg.Core.Health;
using AzCfg.Core.Model;
using AzCfg.Ui.Application.Health;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Application.Health;

public sealed class ProblemsServiceTests
{
    [Fact]
    public async Task CheckAsync_MapsFindingsToRowsErrorsFirst()
    {
        var health = new StubHealth(
            new HealthFinding(HealthCheckKind.KeyMissingInEnvironments, HealthSeverity.Warning, "Key 'A' is missing in prod.", "prod", "A", "azcfg promote dev prod"),
            new HealthFinding(HealthCheckKind.DeadSecretReference, HealthSeverity.Error, "Secret of 'B' does not exist.", "dev", "B", "azcfg edit --env dev"),
            new HealthFinding(HealthCheckKind.AzureSignIn, HealthSeverity.Error, "Not signed in.", Fix: "az login"));

        var report = await Service(health).CheckAsync("C:/repo", default);

        Assert.Equal("C:/repo", health.RepoPath);
        Assert.Equal(2, report.Errors);
        Assert.Equal(1, report.Warnings);
        Assert.Equal(
            [HealthSeverity.Error, HealthSeverity.Error, HealthSeverity.Warning],
            report.Rows.Select(r => r.Severity));
        var missing = report.Rows.Single(r => r.Kind == HealthCheckKind.KeyMissingInEnvironments);
        Assert.Equal(new ProblemRow(HealthSeverity.Warning, HealthCheckKind.KeyMissingInEnvironments, "Key 'A' is missing in prod.", "prod", "A", "azcfg promote dev prod"), missing);
    }

    [Fact]
    public async Task CheckAsync_NoFindings_EmptyReport()
    {
        var report = await Service(new StubHealth()).CheckAsync("C:/repo", default);

        Assert.Empty(report.Rows);
        Assert.Equal(0, report.Errors);
    }

    [Fact]
    public async Task CheckAsync_Cancelled_PassesTokenToCore()
    {
        using var cts = new CancellationTokenSource();
        var health = new StubHealth();

        await Service(health).CheckAsync("C:/repo", cts.Token);

        Assert.Equal(cts.Token, health.Token);
    }

    private static IProblemsService Service(IHealthService health) =>
        new ServiceCollection().AddHealthServices().AddSingleton(health).BuildServiceProvider().GetRequiredService<IProblemsService>();

    private sealed class StubHealth(params HealthFinding[] findings) : IHealthService
    {
        public string? RepoPath { get; private set; }

        public CancellationToken Token { get; private set; }

        public Task<HealthReport> CheckAsync(string repoPath, CancellationToken ct)
        {
            (RepoPath, Token) = (repoPath, ct);
            return Task.FromResult(new HealthReport(new AzureAccountCheck(true, null, null), [], findings));
        }

        public Task<EnvironmentCheck> CheckEnvironmentAsync(ConfigEnvironment env, string? service, CancellationToken ct) => throw new NotSupportedException();
    }
}
