namespace AzCfg.Infrastructure.Azure;

public enum AzureErrorKind
{
    Auth,
    Forbidden,
    NotFound,
    PreconditionFailed,
    Network,
    Validation,
    Other,
}
