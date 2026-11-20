using AzCfg.Ui.Application.State;

namespace AzCfg.Ui.Tests.State;

public sealed class ProtectedUnlockStateTests
{
    [Fact]
    public void IsUnlocked_Default_False() => Assert.False(new ProtectedUnlockState().IsUnlocked("prod"));

    [Fact]
    public void Unlock_IgnoresCase()
    {
        var state = new ProtectedUnlockState();
        state.Unlock("prod");
        Assert.True(state.IsUnlocked("PROD"));
    }

    [Fact]
    public void Lock_IgnoresCaseAndRaisesChanged()
    {
        var state = new ProtectedUnlockState();
        state.Unlock("prod");
        var raised = 0;
        state.Changed += () => raised++;
        state.Lock("Prod");
        Assert.False(state.IsUnlocked("prod"));
        Assert.Equal(1, raised);
    }
}
