using AzCfg.Infrastructure.Azure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AzCfg.Ui.Application.Operations;

/// <summary>
/// Default <see cref="IOperationRunner"/>. Reads run with the caller's token; writes run with <c>ApplicationStopping</c> so a
/// closed browser tab cannot leave a partial apply. Failures are mapped with <see cref="AzureErrors"/>; logs carry only the
/// operation title and the exception type.
/// </summary>
public sealed class OperationRunner(IHostApplicationLifetime lifetime, ILogger<OperationRunner> logger, TimeProvider time) : IOperationRunner
{
    private const int RecentLimit = 20;

    private readonly object _gate = new();
    private readonly List<OperationInfo> _running = [];
    private readonly List<OperationOutcome> _recent = [];

    public IReadOnlyList<OperationInfo> Running
    {
        get
        {
            lock (_gate)
            {
                return [.. _running];
            }
        }
    }

    public IReadOnlyList<OperationOutcome> Recent
    {
        get
        {
            lock (_gate)
            {
                return [.. _recent];
            }
        }
    }

    public event Action? Changed;

    public async Task<OperationResult<T>> RunAsync<T>(string title, OperationKind kind, Func<CancellationToken, Task<T>> work, CancellationToken uiToken)
    {
        var info = new OperationInfo(Guid.NewGuid(), title, kind, time.GetUtcNow());
        Update(() => _running.Add(info));
        var token = kind == OperationKind.Read ? uiToken : lifetime.ApplicationStopping;
        try
        {
            var value = await work(token);
            Finish(info, true, null);
            return new OperationResult<T>.Success(value);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Finish(info, false, "Cancelled");
            return new OperationResult<T>.Cancelled();
        }
        catch (Exception ex)
        {
            var message = AzureErrors.Describe(ex);
            logger.LogWarning("Operation '{Title}' failed: {ExceptionType}", title, ex.GetType().Name);
            Finish(info, false, message);
            return new OperationResult<T>.Failed(message, Map(AzureErrors.Classify(ex)));
        }
    }

    public async Task<OperationResult<bool>> RunAsync(string title, OperationKind kind, Func<CancellationToken, Task> work, CancellationToken uiToken) =>
        await RunAsync(title, kind, async ct =>
        {
            await work(ct);
            return true;
        }, uiToken);

    private static OperationErrorKind Map(AzureErrorKind kind) => kind switch
    {
        AzureErrorKind.Auth => OperationErrorKind.Auth,
        AzureErrorKind.Forbidden => OperationErrorKind.Forbidden,
        AzureErrorKind.NotFound => OperationErrorKind.NotFound,
        AzureErrorKind.PreconditionFailed => OperationErrorKind.PreconditionFailed,
        AzureErrorKind.Network => OperationErrorKind.Network,
        AzureErrorKind.Validation => OperationErrorKind.Validation,
        _ => OperationErrorKind.Other,
    };

    private void Finish(OperationInfo info, bool succeeded, string? message) =>
        Update(() =>
        {
            _running.Remove(info);
            _recent.Insert(0, new OperationOutcome(info, succeeded, message, time.GetUtcNow()));
            if (_recent.Count > RecentLimit)
            {
                _recent.RemoveRange(RecentLimit, _recent.Count - RecentLimit);
            }
        });

    private void Update(Action change)
    {
        lock (_gate)
        {
            change();
        }

        Changed?.Invoke();
    }
}
