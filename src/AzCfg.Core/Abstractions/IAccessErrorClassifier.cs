using AzCfg.Core.Health;

namespace AzCfg.Core.Abstractions;

/// <summary>Classifies an exception thrown by a store read (the exception types belong to the infrastructure, not to Core).</summary>
public interface IAccessErrorClassifier
{
    /// <summary>Never returns <see cref="EnvironmentAccess.Ok"/>; the reason never carries the exception message.</summary>
    AccessFailure Classify(Exception ex);
}
