using AzCfg.Ui.Application.Operations;
using Azure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AzCfg.Ui.Tests.Application.Operations;

public sealed class OperationRunnerTests
{
    private readonly StubLifetime _lifetime = new();
    private readonly OperationRunner _runner;

    public OperationRunnerTests() => _runner = new OperationRunner(_lifetime, NullLogger<OperationRunner>.Instance, new FakeTimeProvider());

    [Fact]
    public async Task RunAsync_Success_ReturnsValueAndRecordsOutcome()
    {
        var result = await _runner.RunAsync("Load", OperationKind.Read, _ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, Assert.IsType<OperationResult<int>.Success>(result).Value);
        var outcome = Assert.Single(_runner.Recent);
        Assert.True(outcome.Succeeded);
        Assert.Equal("Load", outcome.Info.Title);
        Assert.Empty(_runner.Running);
    }

    [Fact]
    public async Task RunAsync_Read_UiTokenCancelled_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var task = _runner.RunAsync("Load", OperationKind.Read, async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return 1;
        }, cts.Token);
        await started.Task;

        await cts.CancelAsync();

        Assert.IsType<OperationResult<int>.Cancelled>(await task);
        Assert.False(Assert.Single(_runner.Recent).Succeeded);
    }

    [Fact]
    public async Task RunAsync_Write_UiTokenCancelled_StillCompletes()
    {
        using var cts = new CancellationTokenSource();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var task = _runner.RunAsync("Apply", OperationKind.Write, async ct =>
        {
            started.SetResult();
            await release.Task;
            ct.ThrowIfCancellationRequested();
            return "done";
        }, cts.Token);
        await started.Task;

        await cts.CancelAsync();
        release.SetResult();

        Assert.Equal("done", Assert.IsType<OperationResult<string>.Success>(await task).Value);
    }

    [Fact]
    public async Task RunAsync_Write_ApplicationStopping_ReturnsCancelled()
    {
        var started = new TaskCompletionSource();
        var task = _runner.RunAsync("Apply", OperationKind.Write, async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return 1;
        }, CancellationToken.None);
        await started.Task;

        await _lifetime.StopAsync();

        Assert.IsType<OperationResult<int>.Cancelled>(await task);
    }

    [Fact]
    public async Task RunAsync_AzureException_ReturnsFailedWithAzureErrorsText()
    {
        var result = await _runner.RunAsync<int>("Load", OperationKind.Read, _ => throw new RequestFailedException(403, "no"), CancellationToken.None);

        var failed = Assert.IsType<OperationResult<int>.Failed>(result);
        Assert.Equal(OperationErrorKind.Forbidden, failed.Kind);
        Assert.Contains("403", failed.Message);
        Assert.False(Assert.Single(_runner.Recent).Succeeded);
    }

    [Fact]
    public async Task RunAsync_NonGeneric_ReturnsTrueOnSuccess()
    {
        var result = await _runner.RunAsync("Save", OperationKind.Write, _ => Task.CompletedTask, CancellationToken.None);

        Assert.True(Assert.IsType<OperationResult<bool>.Success>(result).Value);
    }

    [Fact]
    public async Task Running_WhileExecuting_ListsOperationAndRaisesChanged()
    {
        var changes = 0;
        _runner.Changed += () => changes++;
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var task = _runner.RunAsync("Load", OperationKind.Read, async _ =>
        {
            started.SetResult();
            await release.Task;
            return 1;
        }, CancellationToken.None);
        await started.Task;

        var running = Assert.Single(_runner.Running);
        release.SetResult();
        await task;

        Assert.Equal("Load", running.Title);
        Assert.Empty(_runner.Running);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Recent_MoreThanTwenty_KeepsNewestTwentyFirst()
    {
        for (var i = 0; i < 25; i++)
        {
            await _runner.RunAsync($"Op {i}", OperationKind.Read, _ => Task.FromResult(i), CancellationToken.None);
        }

        Assert.Equal(20, _runner.Recent.Count);
        Assert.Equal("Op 24", _runner.Recent[0].Info.Title);
        Assert.Equal("Op 5", _runner.Recent[^1].Info.Title);
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public Task StopAsync() => _stopping.CancelAsync();

        public void StopApplication() => _stopping.Cancel();
    }
}
