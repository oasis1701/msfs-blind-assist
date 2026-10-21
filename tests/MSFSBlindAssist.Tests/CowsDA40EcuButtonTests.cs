using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The NG's ECU test button has two functions by the engine master (DA40E-7): on, the ECU test;
/// off, held 10 s, reset latched failures and charge the batteries. The reset control already
/// refused with the master on. ⚠️ The ECU Test control did not refuse the other way: pressed with
/// the engine master off at VCBI (2026-10-19) it said "ECU test held. Not met: gearbox 0
/// degrees, needs 38" - the unpowered ECU's 0 read as a temperature - while the aeroplane ran the
/// reset and charged the battery (25.4 V after).
/// </summary>
public class CowsDA40EcuButtonTests
{
    [Fact]
    public void TheTestControlRefusesWithTheEngineMasterOff()
        => Assert.Equal("Engine master is off. This button resets latched failures and charges the batteries instead.",
            CowsDA40Definition.EcuButtonRefusal(testControl: true, engineMaster: 0));

    [Fact]
    public void TheResetControlRefusesWithTheEngineMasterOn()
        => Assert.Equal("Engine master is on. This button runs the ECU test instead.",
            CowsDA40Definition.EcuButtonRefusal(testControl: false, engineMaster: 1));

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void EachRunsItsOwnFunctionOnItsOwnSide(bool testControl, double master)
        => Assert.Null(CowsDA40Definition.EcuButtonRefusal(testControl, master));

    [Fact]
    public void AnUndeliveredMasterRefusesNothing()
    {
        // Not known is not a reason to refuse: the press goes through, as it did before.
        Assert.Null(CowsDA40Definition.EcuButtonRefusal(testControl: true, engineMaster: null));
        Assert.Null(CowsDA40Definition.EcuButtonRefusal(testControl: false, engineMaster: null));
    }

    // ⚠️ THE 45 S CEILING WAS MEASURED ON A TEST THAT STARTED AT ONCE. At VCBI (2026-10-19) the
    // spin-up stage alone took 36 s, the test reached stage 5, and the hold let go at 45 s: the
    // model reset the test and nothing was said. A stage change now extends the hold; the hard
    // ceiling stays for a test that never finishes, and a release short of Complete says so.
    [Fact]
    public void AStageChangeExtendsTheHoldUpToTheCeiling()
    {
        const long start = 100_000;
        long until = start + CowsDA40Definition.EcuTestHoldMs;
        long now = start + 36_000;   // stage 1 after the long spin-up
        until = CowsDA40Definition.EcuTestHoldUntil(start, now, until);
        Assert.Equal(now + CowsDA40Definition.EcuTestStageGraceMs, until);

        // Never past the ceiling, however late a stage moves.
        long late = start + CowsDA40Definition.EcuTestMaxHoldMs - 1_000;
        Assert.Equal(start + CowsDA40Definition.EcuTestMaxHoldMs, CowsDA40Definition.EcuTestHoldUntil(start, late, until));

        // Never shortened: an early stage keeps the initial hold.
        Assert.Equal(start + CowsDA40Definition.EcuTestHoldMs,
            CowsDA40Definition.EcuTestHoldUntil(start, start + 1_000, start + CowsDA40Definition.EcuTestHoldMs));
    }

    [Fact]
    public void AHoldEndingShortOfCompleteSaysWhereItStopped()
        => Assert.Equal("ECU test released after 45 seconds, not complete: Spinning up, second ECU",
            CowsDA40Definition.EcuTestStoppedText(45.2, "Spinning up, second ECU"));
}
