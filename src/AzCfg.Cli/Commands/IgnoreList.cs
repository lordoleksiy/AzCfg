namespace AzCfg.Cli.Commands;

/// <summary>The <c>--json</c> output of the <c>ignore</c> commands: the project's ignore patterns after the command.</summary>
public sealed record IgnoreList(IReadOnlyList<string> Ignore);
