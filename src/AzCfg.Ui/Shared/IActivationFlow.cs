using AzCfg.Core.Model;

namespace AzCfg.Ui.Shared;

/// <summary>Make <c>environment</c> active for the project (FR-S6). Implemented by U4; the F1 default shows "not available yet".</summary>
public interface IActivationFlow
{
    Task<bool> MakeActiveAsync(string projectId, ConfigEnvironment environment);
}
