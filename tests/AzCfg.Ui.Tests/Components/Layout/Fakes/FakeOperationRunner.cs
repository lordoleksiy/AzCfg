using AzCfg.Ui.Application.Operations;

namespace AzCfg.Ui.Tests.Components.Layout.Fakes;

/// <summary>Runs the work inline; records titles. Exceptions become <c>Failed</c>.</summary>
internal sealed class FakeOperationRunner : IOperationRunner
{
    private readonly List<OperationInfo> _running = [];
    private readonly List<OperationOutcome> _recent = [];

    public IReadOnlyList<OperationInfo> Running => _running;

    public IReadOnlyList<OperationOutcome> Recent => _recent;

    public List<string> Titles { get; } = [];

    public event Action? Changed;

    public void StartRunning(string title)
    {
        _running.Add(new OperationInfo(Guid.NewGuid(), title, OperationKind.Read, DateTimeOffset.UnixEpoch));
        Changed?.Invoke();
    }

    public void AddOutcome(string title, bool succeeded, string? message)
    {
        _recent.Insert(0, new OperationOutcome(new OperationInfo(Guid.NewGuid(), title, OperationKind.Read, DateTimeOffset.UnixEpoch), succeeded, message, DateTimeOffset.UnixEpoch));
        Changed?.Invoke();
    }

    public async Task<OperationResult<T>> RunAsync<T>(string title, OperationKind kind, Func<CancellationToken, Task<T>> work, CancellationToken uiToken)
    {
        Titles.Add(title);
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
    }

    public async Task<OperationResult<bool>> RunAsync(string title, OperationKind kind, Func<CancellationToken, Task> work, CancellationToken uiToken) =>
        await RunAsync(title, kind, async ct =>
        {
            await work(ct);
            return true;
        }, uiToken);
}
