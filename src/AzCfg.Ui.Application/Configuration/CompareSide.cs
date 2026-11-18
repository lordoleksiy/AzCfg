using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>One side of a comparison: the local files or the remote configuration of an environment.</summary>
public abstract record CompareSide
{
    public sealed record Local : CompareSide;

    public sealed record Remote(ConfigEnvironment Environment) : CompareSide;
}
