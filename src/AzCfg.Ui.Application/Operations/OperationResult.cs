namespace AzCfg.Ui.Application.Operations;

/// <summary>Outcome of <see cref="IOperationRunner.RunAsync{T}"/>: a value, a mapped failure, or a cancellation.</summary>
public abstract record OperationResult<T>
{
    public sealed record Success(T Value) : OperationResult<T>;

    public sealed record Failed(string Message, OperationErrorKind Kind) : OperationResult<T>;

    public sealed record Cancelled : OperationResult<T>;
}
