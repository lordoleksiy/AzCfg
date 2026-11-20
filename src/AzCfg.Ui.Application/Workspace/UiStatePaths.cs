namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// Root of the UI's own files. <c>AzCfgUiApp</c> registers <c>new UiStatePaths(UiHostOptions.StateRoot)</c>; <c>--demo</c>
/// re-registers it with the demo folder (last registration wins).
/// </summary>
public sealed record UiStatePaths(string Root);
