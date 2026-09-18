using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The calc-path bridge probe's verdict is per CONNECTION, not per aircraft — and an aircraft
/// switch never disconnects. These pin the re-arm primitive `MainForm.ArmBridgeProbe` calls, which
/// is what stops one profile's verdict deciding the next one's writes.
///
/// The regression this closes: a session begun on any profile that registers no MSFSBA_BRIDGE_PROBE
/// target (PMDG 737/777, HS787, iFly, Fenix) concludes UNVERIFIED and silently. Before the re-arm,
/// picking the MD-11 from the Aircraft menu afterwards left that verdict standing, so
/// `CalcWriteCanLand` was false and EVERY MD-11 write refused "unavailable" for the rest of the
/// connection — with the module installed, the calc path healthy, and no warning ever spoken,
/// because the conclusion had been reached on the previous profile.
///
/// SimConnectManager is constructed here with a null window handle: the constructor only stores it
/// and creates timers, and nothing below touches the socket.
/// </summary>
public class CalcPathProbeRearmTests
{
    private static SimConnectManager NewManager() => new(IntPtr.Zero);

    /// <summary>A fresh manager has reached no verdict, so nothing is refused on its evidence.</summary>
    [Fact]
    public void AFreshManager_HasNoVerdict()
    {
        var sim = NewManager();

        Assert.False(sim.CalcPathVerified);
        Assert.False(sim.CalcPathProbeConcluded);
    }

    /// <summary>
    /// The case that caused the regression: a profile with no probe target concludes UNVERIFIED,
    /// and the re-arm must clear it so the next aircraft is judged on its own evidence.
    /// </summary>
    [Fact]
    public void AConcludedUnverifiedVerdict_IsClearedByTheReArm()
    {
        var sim = NewManager();
        sim.MarkCalcPathProbeConcluded();

        Assert.True(sim.CalcPathProbeConcluded);
        Assert.False(sim.CalcPathVerified);

        sim.ResetCalcPathProbe();

        Assert.False(sim.CalcPathProbeConcluded);
        Assert.False(sim.CalcPathVerified);
    }

    /// <summary>
    /// The PRIMITIVE clears whatever is standing, including a positive verdict — that is what a
    /// reconnect needs, and <c>Disconnect()</c> clears the verdict the same way. Whether an
    /// aircraft SWITCH should call it is a separate decision, pinned below.
    /// </summary>
    [Fact]
    public void TheReArmPrimitive_ClearsAVerifiedVerdictToo()
    {
        var sim = NewManager();
        sim.MarkCalcPathVerified();

        Assert.True(sim.CalcPathVerified);
        Assert.True(sim.CalcPathProbeConcluded);   // verifying concludes the probe too

        sim.ResetCalcPathProbe();

        Assert.False(sim.CalcPathVerified);
        Assert.False(sim.CalcPathProbeConcluded);
    }

    /// <summary>
    /// An aircraft switch re-arms a NEGATIVE verdict and KEEPS a positive one.
    ///
    /// The probe establishes that the MobiFlight WASM executes an RPN write and it lands — a
    /// property of the module and the connection, which a switch does not touch. So only the
    /// negative verdict can be wrong for the next aircraft (a profile registering no probe target
    /// concludes UNVERIFIED, silently, and that standing refused every write on the aircraft
    /// picked afterwards). Clearing a positive one as well re-opened a degraded window on EVERY
    /// Aircraft-menu switch: at least two probe ticks in which FBW SetLVar falls back to the
    /// data-def write that reverts silently, and up to ~60 s of queued dotted events replayed in a
    /// burst on a machine with no WASM module.
    /// </summary>
    [Theory]
    [InlineData(false, true)]   // no verdict yet, or concluded UNVERIFIED → judge the new aircraft afresh
    [InlineData(true, false)]   // already proven on this connection → leave it proven
    public void AnAircraftSwitch_ReArmsOnlyWhatIsNotAlreadyProven(bool verified, bool expectedRearm)
    {
        Assert.Equal(expectedRearm, CalcPathVerdict.ShouldRearmOnAircraftSwitch(verified));
    }

    /// <summary>
    /// Re-arming twice, or on a manager that never concluded, is harmless — the switch path calls
    /// it unconditionally on every aircraft change.
    /// </summary>
    [Fact]
    public void TheReArm_IsIdempotentAndSafeBeforeAnyVerdict()
    {
        var sim = NewManager();

        sim.ResetCalcPathProbe();
        sim.ResetCalcPathProbe();

        Assert.False(sim.CalcPathVerified);
        Assert.False(sim.CalcPathProbeConcluded);
    }
}
