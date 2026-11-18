namespace AzCfg.Ui.Application.Operations;

public sealed record OperationInfo(Guid Id, string Title, OperationKind Kind, DateTimeOffset StartedAt);
