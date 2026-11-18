namespace AzCfg.Ui.Application.State;

/// <summary>What a tab shows; also the dedupe key of <see cref="TabsState.Open"/>.</summary>
public sealed record TabDescriptor(TabKind Kind, string? ProjectId = null);
