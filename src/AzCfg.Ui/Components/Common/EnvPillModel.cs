using AzCfg.Core.Environments;

namespace AzCfg.Ui.Components.Common;

/// <summary>What an <see cref="EnvPill"/> shows; features build it (the kind comes from the environment rules of Application).</summary>
public sealed record EnvPillModel(string Name, EnvironmentKind Kind, bool IsProtected, bool IsActive);
