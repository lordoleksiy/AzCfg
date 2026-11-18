namespace AzCfg.Ui.Application.Operations;

public enum OperationErrorKind
{
    Auth,
    Forbidden,
    NotFound,
    PreconditionFailed,
    Network,
    Validation,
    Other,
}
