using System.Linq;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// MD-11 altitude reporting before taxi (2026-10-20): After Start sets the transponder to XPDR and
/// altitude reporting ON through the SAME knob and switch Before Takeoff turns to TA/RA
/// (AftFootPedestal.xml MD11_PED_XPNDR_MODE_KB 0 Standby / 1 Transponder only / 2 TA only / 3 TA-RA;
/// MD11_PED_XPNDR_ALT_RPTG_KB 1 Enabled), and leaves a mode already past standby alone.
/// </summary>
public class Md11FoTransponderBeforeTaxiTests
{
    private const string Mode = "MD11_PED_XPNDR_MODE_KB";
    private const string AltRptg = "MD11_PED_XPNDR_ALT_RPTG_KB";

    [Fact]
    public void After_start_sets_xpdr_and_altitude_reporting_on_the_before_takeoff_controls()
    {
        var flows = Md11FoFlowDefinitions.Build();
        var asSteps = flows.Single(f => f.Id == "AFTER_START").Steps;
        var mode = asSteps.Single(s => s.Id == "AS_XPDR_MODE");
        var alt = asSteps.Single(s => s.Id == "AS_ALT_RPTG");
        Assert.Equal(Mode, mode.EventName);
        Assert.Equal(1, mode.TargetValue);
        Assert.Equal(AltRptg, alt.EventName);
        Assert.Equal(1, alt.TargetValue);
        Assert.Equal("AS_XPDR_MODE", mode.CompletesChecklistItemId);
        Assert.Equal("AS_ALT_RPTG", alt.CompletesChecklistItemId);

        var bt = flows.Single(f => f.Id == "BEFORE_TAKEOFF").Steps;
        Assert.Equal(mode.EventName, bt.Single(s => s.Id == "BT_XPDR_MODE").EventName);
        Assert.Equal(alt.EventName, bt.Single(s => s.Id == "BT_ALT_RPTG").EventName);
        Assert.Equal(3, bt.Single(s => s.Id == "BT_XPDR_MODE").TargetValue);

        var order = flows.Select(f => f.Id).ToList();
        Assert.True(order.IndexOf("AFTER_START") < order.IndexOf("BEFORE_TAKEOFF"));
    }

    // The After Start line reads XPDR or any TCAS mode, so Before Takeoff's TA/RA never reverts it.
    [Fact]
    public void The_after_start_line_accepts_xpdr_and_every_tcas_mode()
    {
        var item = Md11FoChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == "AS_XPDR_MODE");
        Assert.Equal(Mode, item.StateFieldName);
        Assert.False(item.StateCondition!(0));
        Assert.True(item.StateCondition(1) && item.StateCondition(2) && item.StateCondition(3));
        Assert.NotNull(item.CheckAction);
    }
}
