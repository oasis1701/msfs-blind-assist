// Which aircraft the MobiFlight calc-path probe runs on — and therefore which pilots hear its
// verdict when the WASM module is missing.
//
// MainForm's probe timer runs the nonce round-trip (write L:MSFSBA_BRIDGE_PROBE through the
// calculator, read it back over a data definition) only for a definition that REGISTERS that
// var; for every other aircraft it concludes at once and silently, because there is nothing to
// verify and nothing to warn about. The registration IS the opt-in.
//
// The MD-11 writes every control through the calculator (Md11EventBus: CEVENT + the direct-write
// family) yet never registered the target, so it sat outside the probe: with no module installed
// every press was dropped into a dead client-data area — MobiFlightWasmModule.IsConnected is true
// with no module, Initialize being purely local setup — and the pilot pressed Battery, heard the
// unchanged state read back as the result, and was told nothing. On a healthy install every
// MD-11 load also logged "calc path NOT available after 0 attempt(s)", a false alarm.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CalcPathProbeOptInTests
{
    public const string ProbeVar = "MSFSBA_BRIDGE_PROBE";

    public static IEnumerable<object[]> AircraftThatWriteThroughTheCalculator()
    {
        yield return new object[] { new FlyByWireA320Definition() };
        yield return new object[] { new FlyByWireA380Definition() };
        yield return new object[] { new HeadwindA330Definition() };   // inherits the A320's set via base.BuildVariables()
        yield return new object[] { new TFDiMD11Definition() };       // every control write is a calculator-path CEVENT
    }

    // An aircraft not on this list does not register the probe var today — opting an aircraft in
    // is a deliberate change, made here for the MD-11. Whether each aircraft's own transport is
    // otherwise healthy is not something this test measures.
    public static IEnumerable<object[]> AircraftWithTheirOwnTransport()
    {
        yield return new object[] { new FenixA320Definition() };
        yield return new object[] { new PMDG737Definition() };
        yield return new object[] { new PMDG777Definition() };
        yield return new object[] { new HorizonSim787Definition() };
        yield return new object[] { new IFly737MAXDefinition() };
    }

    [Theory]
    [MemberData(nameof(AircraftThatWriteThroughTheCalculator))]
    public void Aircraft_that_write_through_the_calculator_register_the_probe_target(IAircraftDefinition aircraft)
    {
        Assert.True(aircraft.GetVariables().ContainsKey(ProbeVar), aircraft.AircraftCode);
    }

    [Theory]
    [MemberData(nameof(AircraftWithTheirOwnTransport))]
    public void Aircraft_with_their_own_transport_do_not_opt_in(IAircraftDefinition aircraft)
    {
        Assert.False(aircraft.GetVariables().ContainsKey(ProbeVar), aircraft.AircraftCode);
    }

    // The target must be the same quiet, OnRequest L:var the FBW defs register: the timer
    // force-reads it over its own data definition after each nonce write (an OnRequest def is
    // what makes RequestVariable(forceUpdate: true) answer at all), and a nonce is nothing to speak.
    [Fact]
    public void The_md11_probe_target_is_a_quiet_on_request_lvar_like_the_fbw_ones()
    {
        var a320 = new FlyByWireA320Definition().GetVariables()[ProbeVar];
        var md11 = new TFDiMD11Definition().GetVariables()[ProbeVar];

        Assert.Equal(a320.Name, md11.Name);
        Assert.Equal(SimVarType.LVar, md11.Type);
        Assert.Equal(UpdateFrequency.OnRequest, md11.UpdateFrequency);
        Assert.False(md11.IsAnnounced);
    }
}
