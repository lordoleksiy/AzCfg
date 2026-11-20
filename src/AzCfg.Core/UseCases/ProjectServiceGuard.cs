using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>The rule for <c>--service</c> on a project that is already bound (ADR 0014).</summary>
public static class ProjectServiceGuard
{
    /// <summary>
    /// A <paramref name="requested"/> service equal to the binding's (ignoring case) or none is fine; another service throws
    /// <see cref="InvalidOperationException"/> naming the current service and <c>azcfg config set project.service</c>.
    /// </summary>
    public static void EnsureSameService(ProjectBinding existing, string? requested)
    {
        if (requested is null || requested.Equals(existing.Service, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"This project is already bound to service '{existing.Service}', not '{requested}'. " +
            $"To switch, run 'azcfg config set project.service {requested}'.");
    }
}
