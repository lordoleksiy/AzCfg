using AzCfg.Core.Abstractions;
using AzCfg.Core.Health;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// <see cref="IAccessErrorClassifier"/> over <see cref="AzureErrors"/>: sign-in, 403 and network failures get the fixed
/// <see cref="AzureErrors.Describe"/> texts; anything else is <see cref="EnvironmentAccess.Unreachable"/> with the exception
/// type name only, so no exception message (which may carry values) is ever reported.
/// </summary>
public sealed class AzureAccessErrorClassifier : IAccessErrorClassifier
{
    public AccessFailure Classify(Exception ex) => AzureErrors.Classify(ex) switch
    {
        AzureErrorKind.Auth => new(EnvironmentAccess.AuthRequired, AzureErrors.Describe(ex)),
        AzureErrorKind.Forbidden => new(EnvironmentAccess.Forbidden, AzureErrors.Describe(ex)),
        AzureErrorKind.Network => new(EnvironmentAccess.Unreachable, AzureErrors.Describe(ex)),
        _ => new(EnvironmentAccess.Unreachable, ex.GetType().Name),
    };
}
