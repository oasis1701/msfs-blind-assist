using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The one-capture-at-a-time gate shared by every aircraft's display read and by the scene
/// description. Tests take their own instance; production shares <c>DisplayReadGate.Shared</c>.
/// </summary>
public class DisplayReadGateTests
{
    [Fact]
    public void AFreeGate_IsTakenByTheFirstCaller()
    {
        var gate = new DisplayReadGate();
        Assert.True(gate.TryEnter());
        Assert.True(gate.IsHeld);
    }

    // The defect this gate closes: a display read and a scene description running together each
    // move or read the simulator camera, so the second capture lands on the first one's view.
    [Fact]
    public void AHeldGate_RefusesTheSecondCaller()
    {
        var gate = new DisplayReadGate();
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public void AReleasedGate_IsTakenAgain()
    {
        var gate = new DisplayReadGate();
        Assert.True(gate.TryEnter());
        gate.Exit();
        Assert.False(gate.IsHeld);
        Assert.True(gate.TryEnter());
    }

    // ReadDisplay releases in a finally and then shows any error dialog, so a release can run for
    // a gate a failure path already released. It must not throw, and must not leave it held.
    [Fact]
    public void ReleasingAGateThatIsNotHeld_IsHarmless()
    {
        var gate = new DisplayReadGate();
        gate.Exit();
        gate.Exit();
        Assert.False(gate.IsHeld);
        Assert.True(gate.TryEnter());
    }

    // Both paths speak the same sentence, so a pilot hears one wording whichever they pressed.
    [Fact]
    public void TheBusyMessage_IsOneSharedWording()
    {
        Assert.Equal("A display read is already in progress.", DisplayReadGate.BusyMessage);
    }
}
