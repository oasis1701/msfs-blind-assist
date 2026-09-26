using System.Linq;
using MSFSBlindAssist.FirstOfficer.PMDG737;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 737 NG3 transponder mode knob CANNOT be driven to STBY (XPDR_ModeSel 0).
/// Live-probed 2026-08-27 against a running NG3: every write transport that reaches the
/// knob steps it 4→3→2→1 and is then inert at 1 — TransmitClientEvent with LEFTSINGLE /
/// WHEEL_DOWN / LEFTDOUBLE / MIDDLESINGLE / LEFTDRAG / DOWN_REPEAT / LEFTRELEASE / an
/// absolute 0 parameter, K:ROTOR_BRAKE action codes 0-9 on index 800, the CDA position
/// write, and the undocumented +20000 alias event (90432). A direct write to the cockpit
/// mirror L:var switch_800_73X reverts within a frame (PMDG-owned read-back), and a write
/// to the stock A:TRANSPONDER STATE:1 reverts too (CTCAS::updateSquawkbox rewrites it
/// every frame from PMDG's own state).
///
/// This is not an MSFSBA transport bug. The aircraft's own cockpit behavior file
/// (73X_Cockpit_Behavior.xml) shows the VC mouse rect for the knob emitting exactly
/// K:ROTOR_BRAKE 80001 for a left-half click — the same code we send — so a human
/// clicking the knob cannot reach STBY either. PMDG's own B738_Checklist.xml never asks
/// for STBY ("Transponder panel — Set", "Transponder mode selector — As needed"), and
/// FSFO V6 fails identically (RotateLeftRightSwitch to 0, gives up after 5 clicks, and
/// verifies switch_800_73X == 0 — a test that can never pass).
///
/// So the FO targets ALT RPTG OFF (1), the lowest REACHABLE position and the real-world
/// step above STBY: the transponder still replies to Mode A interrogations but suppresses
/// Mode C altitude (measured: A:TRANSPONDER STATE:1 reads 3 "On" at position 1 versus 4
/// "Alt" at every higher position).
///
/// The accept predicate deliberately admits 0 AND 1 so the item still passes on an
/// airframe where STBY IS reachable (the alternate digital transponder panel, airframe
/// option "Transponder New Style Installed") without needing a second code path.
/// </summary>
public class Pmdg737TransponderStandbyTests
{
    private const string Label = "Transponder: ALT RPTG OFF";

    // XPDR_ModeSel: 0 STBY, 1 ALT RPTG OFF, 2 XPNDR, 3 TA ONLY, 4 TA/RA.
    private const int AltRptgOff = 1;

    private static MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<
        AircraftActionExecutor, AircraftStateEvaluator> ChecklistItem(string id) =>
        PMDG737ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).Single(i => i.Id == id);

    private static MSFSBlindAssist.FirstOfficer.Models.FlowStep<AircraftStateEvaluator>
        FlowStep(string id) =>
        PMDG737FlowDefinitions.Build()
            .SelectMany(f => f.Steps).Single(s => s.Id == id);

    [Theory]
    [InlineData("PF_XPDR")]
    [InlineData("SD_XPDR")]
    public void ChecklistItemTargetsTheLowestReachablePosition(string itemId)
    {
        var item = ChecklistItem(itemId);
        Assert.Equal(Label, item.Label);
        Assert.Equal("XPDR_ModeSel", item.StateFieldName);
    }

    [Theory]
    [InlineData("PF_XPDR")]
    [InlineData("SD_XPDR")]
    public void ChecklistItemAcceptsStandbyAndAltRptgOffOnly(string itemId)
    {
        var accept = ChecklistItem(itemId).StateCondition;
        Assert.NotNull(accept);

        // STBY stays acceptable — an airframe that CAN reach it must still pass.
        Assert.True(accept!(0), "STBY (0) must satisfy the item");
        Assert.True(accept(AltRptgOff), "ALT RPTG OFF (1) must satisfy the item");

        // Anything that reports altitude must not.
        Assert.False(accept(2), "XPNDR (2) must not satisfy the item");
        Assert.False(accept(3), "TA ONLY (3) must not satisfy the item");
        Assert.False(accept(4), "TA/RA (4) must not satisfy the item");
    }

    [Theory]
    [InlineData("PF_XPDR")]
    [InlineData("SD_XPDR")]
    public void FlowStepCommandsAltRptgOffNotStandby(string stepId)
    {
        var step = FlowStep(stepId);
        Assert.Equal(Label, step.Label);
        Assert.Equal("EVT_TCAS_MODE", step.EventName);
        Assert.Equal(AltRptgOff, step.TargetValue);
    }

    /// <summary>
    /// The Before Start / Before Takeoff items are unaffected — TA/RA (4) is reachable and
    /// stays the target. Pins that this change did not leak across the phase boundary.
    /// </summary>
    [Theory]
    [InlineData("BS_XPDR")]
    [InlineData("BTKO_XPDR")]
    public void TaRaItemsAreUnchanged(string itemId)
    {
        var item = ChecklistItem(itemId);
        Assert.Equal("Transponder: TA/RA", item.Label);
        Assert.True(item.StateCondition!(4), "TA/RA (4) must satisfy the TA/RA item");
        Assert.False(item.StateCondition!(AltRptgOff), "ALT RPTG OFF must not satisfy it");
    }
}
