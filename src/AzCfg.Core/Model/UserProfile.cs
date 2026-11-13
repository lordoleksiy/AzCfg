namespace AzCfg.Core.Model;

public sealed record UserProfile(IReadOnlyDictionary<string, Uri> Environments);
