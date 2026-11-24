namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// A bound project's binding differs from its shared <c>azcfg.json</c> (e.g. a teammate changed it). Applying it is an explicit
/// Write; when <see cref="ServiceChanged"/> it clears the active environment and drops the snapshots (Core rule).
/// </summary>
public sealed record ManifestChange(string CurrentService, string ManifestService)
{
    public bool ServiceChanged => !string.Equals(CurrentService, ManifestService, StringComparison.Ordinal);
}
