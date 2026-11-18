using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>A local value together with the target it was read from.</summary>
internal sealed record SourcedLocalValue(LocalValue Value, LocalTargetBinding Source);
