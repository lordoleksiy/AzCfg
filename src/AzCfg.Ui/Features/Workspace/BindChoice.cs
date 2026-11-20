using AzCfg.Core.Model;

namespace AzCfg.Ui.Features.Workspace;

/// <summary>What the bind dialog hands back: the service and the local targets for plain values and secrets (they may be the same).</summary>
public sealed record BindChoice(string Service, LocalTargetBinding Values, LocalTargetBinding Secrets);
