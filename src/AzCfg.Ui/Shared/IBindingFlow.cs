namespace AzCfg.Ui.Shared;

/// <summary>Bind / re-bind wizard (FR-W5). Implemented by U1; the F1 default shows "not available yet".</summary>
public interface IBindingFlow
{
    Task<bool> BindAsync(string projectId);
}
