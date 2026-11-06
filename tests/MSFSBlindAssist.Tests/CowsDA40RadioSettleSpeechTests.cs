using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// DA40A-9: a knob value at rest interrupts, because the pilot is waiting for it. A value a mode or
/// the CDI moved by itself waits its turn: switching the CDI to LOC1 with the PFD softkey, "Course
/// 206" cut off the window's own "Navigation source LOC1" (MSFS 2024, owner, 2026-10-27).
/// </summary>
public class CowsDA40RadioSettleSpeechTests
{
    [Fact]
    public void AKnobAtRestInterrupts()
        => Assert.True(CowsDA40Definition.SettledValueInterrupts(modeSync: false));

    [Fact]
    public void AValueTheCdiOrAModeMovedWaitsItsTurn()
        => Assert.False(CowsDA40Definition.SettledValueInterrupts(modeSync: true));
}
