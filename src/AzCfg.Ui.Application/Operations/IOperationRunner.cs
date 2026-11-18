namespace AzCfg.Ui.Application.Operations;

/// <summary>Runs every IO call of the UI (singleton, AD-9); exceptions are mapped via <c>AzureErrors</c>.</summary>
public interface IOperationRunner
{
    IReadOnlyList<OperationInfo> Running { get; }

    /// <summary>Last 20 outcomes, newest first.</summary>
    IReadOnlyList<OperationOutcome> Recent { get; }

    event Action? Changed;

    /// <summary>Read: cancelled by <paramref name="uiToken"/>. Write: ignores <paramref name="uiToken"/>, runs with ApplicationStopping (AD-9). Never throws.</summary>
    Task<OperationResult<T>> RunAsync<T>(string title, OperationKind kind, Func<CancellationToken, Task<T>> work, CancellationToken uiToken);

    /// <inheritdoc cref="RunAsync{T}"/>
    Task<OperationResult<bool>> RunAsync(string title, OperationKind kind, Func<CancellationToken, Task> work, CancellationToken uiToken);
}
