using AzCfg.Ui.Application.State;

namespace AzCfg.Ui.Shared;

/// <summary>Component rendering a project mode; it takes <c>[Parameter, EditorRequired] ProjectSession Session</c>.</summary>
public sealed record ProjectModeRegistration(ProjectMode Mode, Type Component);
