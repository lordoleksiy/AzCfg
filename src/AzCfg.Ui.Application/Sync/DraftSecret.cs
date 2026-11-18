using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Sync;

/// <summary>A typed secret value of a draft; <see cref="Value"/> prints as <c>***</c>.</summary>
public sealed record DraftSecret(string SecretName, SecretString Value);
