using Spectre.Console;
using Spectre.Console.Rendering;

namespace AzCfg.Cli.Tests.Commands;

/// <summary>An <see cref="IAnsiConsole"/> that renders through <paramref name="inner"/> and reads keys from <paramref name="input"/>.</summary>
internal sealed class ScriptedConsole(IAnsiConsole inner, IAnsiConsoleInput input) : IAnsiConsole
{
    public Profile Profile => inner.Profile;
    public IAnsiConsoleCursor Cursor => inner.Cursor;
    public IAnsiConsoleInput Input => input;
    public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;
    public RenderPipeline Pipeline => inner.Pipeline;
    public void Clear(bool home) => inner.Clear(home);
    public void Write(IRenderable renderable) => inner.Write(renderable);
    public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
}
