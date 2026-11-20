namespace AzCfg.Ui.Features.Environments;

/// <summary>A validation problem of an <see cref="EnvironmentsDraft"/>; <see cref="Row"/> is null for a problem of the whole draft.</summary>
public sealed record EnvironmentIssue(EnvironmentRow? Row, string Message);
