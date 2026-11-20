using AzCfg.Ui.Application.Operations;

namespace AzCfg.Ui.Tests.Features.Compare.Fakes;

/// <summary>Runs the work inline and records titles; exceptions become <c>Failed</c> with the exception type as the message.</summary>
internal sealed class FakeOperationRunner : IOperationRunner
{
    public IReadOnlyList<OperationInfo> Running => [];

    public IReadOnlyList<OperationOutcome> Recent => [];

    public List<string> Titles { get; } = [];

    public event Action? Changed;

    public async Task<OperationResult<T>> RunAsync<T>(string title, OperationKind kind, Func<CancellationToken, Task<T>> work, CancellationToken uiToken)
    {
        Titles.Add(title);
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
    }

    public async Task<OperationResult<bool>> RunAsync(string title, OperationKind kind, Func<CancellationToken, Task> work, CancellationToken uiToken) =>
        await RunAsync(title, kind, async ct =>
        {
            await work(ct);
            return true;
        }, uiToken);
}
