using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>One side of a comparison: the local files or a remote environment.</summary>
public abstract record CompareSide
{
    public sealed record Local : CompareSide;

    public sealed record Environment(ConfigEnvironment Env) : CompareSide;
}
