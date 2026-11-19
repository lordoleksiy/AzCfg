namespace AzCfg.Core.Sync;

/// <summary>A key present in some environments (<see cref="PresentIn"/>) and absent from others (<see cref="MissingIn"/>).</summary>
public sealed record MissingKey(string Key, IReadOnlyList<string> PresentIn, IReadOnlyList<string> MissingIn);
