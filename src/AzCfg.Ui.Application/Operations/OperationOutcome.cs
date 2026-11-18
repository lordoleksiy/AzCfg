namespace AzCfg.Ui.Application.Operations;

public sealed record OperationOutcome(OperationInfo Info, bool Succeeded, string? Message, DateTimeOffset FinishedAt);
