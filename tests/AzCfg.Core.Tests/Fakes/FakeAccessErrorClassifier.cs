using AzCfg.Core.Abstractions;
using AzCfg.Core.Health;

namespace AzCfg.Core.Tests.Fakes;

/// <summary><see cref="UnauthorizedAccessException"/> → Forbidden; anything else → Unreachable; the reason is the exception type name.</summary>
internal sealed class FakeAccessErrorClassifier : IAccessErrorClassifier
{
    public AccessFailure Classify(Exception ex) =>
        new(ex is UnauthorizedAccessException ? EnvironmentAccess.Forbidden : EnvironmentAccess.Unreachable, ex.GetType().Name);
}
