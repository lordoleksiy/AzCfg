namespace AzCfg.Ui.Features.Environments;

/// <summary>
/// One editable environment of an <see cref="EnvironmentsDraft"/>. <see cref="IsNew"/> rows may be renamed while they are
/// being added; saved environments keep their name (G15: snapshots, active environment and vaults are keyed by it).
/// <see cref="Included"/> is false for a discovered store the user did not tick.
/// </summary>
public sealed class EnvironmentRow
{
    public string Name { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool IsProtected { get; set; }

    public bool Included { get; set; } = true;

    public bool IsNew { get; init; }
}
