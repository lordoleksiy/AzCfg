using AzCfg.Ui.Application.Operations;

namespace AzCfg.Ui.Tests.Features.Sync.Fakes;

/// <summary>Runs the work inline and lists it in <see cref="Running"/> while it runs; exceptions become <c>Failed</c>.</summary>
internal sealed class FakeOperationRunner : IOperationRunner
{
    private readonly List<OperationInfo> _running = [];

    public IReadOnlyList<OperationInfo> Running => _running;

    public IReadOnlyList<OperationOutcome> Recent => [];

    public List<(string Title, OperationKind Kind)> Runs { get; } = [];

    public event Action? Changed;

    public async Task<OperationResult<T>> RunAsync<T>(string title, OperationKind kind, Func<CancellationToken, Task<T>> work, CancellationToken uiToken)
    {
        Runs.Add((title, kind));
        var info = new OperationInfo(Guid.NewGuid(), title, kind, DateTimeOffset.UnixEpoch);
        _running.Add(info);
        Changed?.Invoke();
        try
        {
            return new OperationResult<T>.Success(await work(uiToken));
        }
        catch (OperationCanceledException)
        {
            return new OperationResult<T>.Cancelled();
        }
        catch (Exception e)
        {
            return new OperationResult<T>.Failed(e.GetType().Name, OperationErrorKind.Other);
        }
        finally
        {
            _running.Remove(info);
            Changed?.Invoke();
        }
    }

    public async Task<OperationResult<bool>> RunAsync(string title, OperationKind kind, Func<CancellationToken, Task> work, CancellationToken uiToken) =>
        await RunAsync(title, kind, async ct =>
        {
            await work(ct);
            return true;
        }, uiToken);
}
