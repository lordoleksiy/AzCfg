using AzCfg.Ui.Application.State;

namespace AzCfg.Ui.Shared;

/// <summary>Component rendering tabs of <see cref="Kind"/>; it takes <c>[Parameter, EditorRequired] WorkbenchTab Tab</c>.</summary>
public sealed record TabRegistration(TabKind Kind, Type Component);
