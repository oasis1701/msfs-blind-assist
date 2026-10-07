using System.Linq;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FenixFlowParityTests
{
    private static FlowStep<FenixStateEvaluator> Step(string flow, string id)
        => FenixFlowDefinitions.Build().Single(f => f.Id == flow).Steps.Single(s => s.Id == id);

    private static ChecklistItem<FenixActionExecutor, FenixStateEvaluator> Item(string id)
        => FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    [Theory]
    [InlineData("PREFLIGHT", "PF_ECAMDOOR", "S_ECAM_DOOR", "ECAM page: door")]
    [InlineData("BEFORE_START", "BS_ECAMAPU", "S_ECAM_APU", "ECAM page: APU")]
    [InlineData("ENGINE_START", "ES_ECAMENG", "S_ECAM_ENGINE", "ECAM page: engine")]
    [InlineData("AFTER_START", "AS_ECAMSTS", "S_ECAM_STATUS", "ECAM page: status")]
    [InlineData("SHUTDOWN", "SD_ECAMDOOR", "S_ECAM_DOOR", "ECAM page: door")]
    public void Ecam_page_steps_pulse_the_page_button(string flow, string id, string key, string label)
    {
        var s = Step(flow, id);
        Assert.Equal(key, s.EventName);
        Assert.Equal(label, s.Label);
        Assert.Equal(id, s.CompletesChecklistItemId);
        Assert.NotNull(Item(id).CheckAction);
        Assert.True(FenixActionExecutor.IsMomentaryKey(key), $"{key} must be in the pulse table");
    }

    [Fact]
    public void Takeoff_flaps_have_a_captain_fallback_when_no_simbrief_plan()
    {
        var fallback = Step("AFTER_START", "AS_FLAPS_CAPT");
        Assert.Equal("Flaps: set for takeoff", fallback.Label);
        var e = new FenixStateEvaluator();
        Assert.False(fallback.SkipCondition!(e));
        e.SetTakeoffFlaps(1);
        Assert.True(fallback.SkipCondition!(e));
    }

    [Fact]
    public void After_takeoff_gear_check_no_longer_links_a_removed_line()
        => Assert.Null(Step("AFTER_TAKEOFF", "AT_GEAR_UP_CHECK").CompletesChecklistItemId);

    [Fact]
    public void Shutdown_tcas_and_secure_external_power_have_action_lines()
    {
        Assert.Equal("SD_TCAS_STBY", Step("SHUTDOWN", "SD_TCAS_STBY").CompletesChecklistItemId);
        Assert.Equal("TCAS: STANDBY", Item("SD_TCAS_STBY").Label);
        Assert.Equal("SC_EXTPWR_OFF", Step("SECURE", "SC_EXTPWR_OFF").CompletesChecklistItemId);
        Assert.Equal("External power: OFF", Item("SC_EXTPWR_OFF").Label);
        Assert.Equal("I_OH_ELEC_EXT_PWR_L", Item("SC_EXTPWR_OFF").StateFieldName);
    }

    [Fact]
    public void Shared_wording_matches_the_A32NX()
    {
        Assert.Equal("Weather radar: SYSTEM 1", Item("BT_WXR").Label);
        Assert.Equal("Cockpit door: LOCKED", Item("BS_COCKPITDOOR").Label);
        Assert.Equal("Cockpit door: UNLOCKED", Item("SD_COCKPITDOOR").Label);
        Assert.Equal("APU: ON and available", Item("BS_APU").Label);
        Assert.Equal("Engine 1 starting — waiting for the engine to stabilize", Step("ENGINE_START", "ES_ENG1_N2").Label);
    }
}
