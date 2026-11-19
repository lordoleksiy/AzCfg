namespace AzCfg.Cli.Commands;

/// <summary>
/// Why <c>azcfg use</c> did not pull (<c>--json</c> output, key names only): <see cref="Reason"/> is <c>blocked</c> (<see cref="Keys"/> =
/// local changes that would be overwritten), <c>unmanaged</c> (<see cref="Keys"/> = local keys no pull has managed yet; pass <c>--yes</c>)
/// or <c>protected</c> (the environment is protected; pass <c>--confirm</c> with its name).
/// </summary>
public sealed record UseRefusal(string Environment, string Reason, IReadOnlyList<string> Keys, string Hint);
