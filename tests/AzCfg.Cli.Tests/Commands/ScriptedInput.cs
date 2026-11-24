using Spectre.Console;

namespace AzCfg.Cli.Tests.Commands;

/// <summary>Console input typed from a script: <c>\n</c> is Enter; a prompt beyond the script throws.</summary>
internal sealed class ScriptedInput(string text) : IAnsiConsoleInput
{
    private readonly Queue<char> _keys = new(text);

    public bool IsKeyAvailable() => _keys.Count > 0;

    public ConsoleKeyInfo? ReadKey(bool intercept) => _keys.TryDequeue(out var c)
        ? c == '\n' ? new('\r', ConsoleKey.Enter, false, false, false) : new(c, default, false, false, false)
        : throw new InvalidOperationException("Unexpected prompt.");

    public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
}
