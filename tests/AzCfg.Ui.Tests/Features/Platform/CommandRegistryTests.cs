using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Tests.Features.Platform;

public sealed class CommandRegistryTests
{
    private sealed class Contributor(params UiCommand[] commands) : ICommandContributor
    {
        public IEnumerable<UiCommand> Commands => commands;
    }

    private static UiCommand Command(string id, Action? run = null, Func<bool>? enabled = null) =>
        new(id, id.ToUpperInvariant(), "Test", null, () =>
        {
            run?.Invoke();
            return Task.CompletedTask;
        }, enabled);

    [Fact]
    public void All_CombinesContributorsAndDynamicCommands()
    {
        var registry = new CommandRegistry([new Contributor(Command("a"), Command("b")), new Contributor(Command("c"))]);
        registry.Register(Command("d"));
        Assert.Equal(["a", "b", "c", "d"], registry.All.Select(c => c.Id));
    }

    [Fact]
    public async Task ExecuteAsync_EnabledCommand_RunsAndReturnsTrue()
    {
        var ran = 0;
        var registry = new CommandRegistry([new Contributor(Command("a", () => ran++))]);
        Assert.True(await registry.ExecuteAsync("a"));
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledCommand_DoesNotRun()
    {
        var ran = 0;
        var registry = new CommandRegistry([new Contributor(Command("a", () => ran++, () => false))]);
        Assert.False(await registry.ExecuteAsync("a"));
        Assert.Equal(0, ran);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownId_ReturnsFalse() =>
        Assert.False(await new CommandRegistry([]).ExecuteAsync("nope"));

    [Fact]
    public async Task Register_Disposed_RemovesTheCommandAndRaisesChanged()
    {
        var registry = new CommandRegistry([]);
        var raised = 0;
        registry.Changed += () => raised++;
        var registration = registry.Register(Command("dyn"));
        Assert.True(await registry.ExecuteAsync("dyn"));
        registration.Dispose();
        Assert.False(await registry.ExecuteAsync("dyn"));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void All_DynamicCommandWithSameId_ReplacesContributed()
    {
        var registry = new CommandRegistry([new Contributor(Command("a"))]);
        var replacement = Command("a", enabled: () => false);
        registry.Register(replacement);
        Assert.Same(replacement, Assert.Single(registry.All));
    }
}
