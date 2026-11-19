namespace AzCfg.Cli.Commands;

/// <summary>
/// Why <c>azcfg use</c> did not pull (<c>--json</c> output, key names only): <see cref="Reason"/> is <c>blocked</c> (<see cref="Keys"/> =
/// local changes that would be overwritten), <c>unmanaged</c> (<see cref="Keys"/> = local keys no pull has managed yet; pass <c>--yes</c>)
/// <c>protected</c> (the environment is protected; pass <c>--confirm</c> with its name) or <c>unverified</c> (the active environment could not be read to check for local changes; pass <c>--force</c>).
/// </summary>
public sealed record UseRefusal(string Environment, string Reason, IReadOnlyList<string> Keys, string Hint);
