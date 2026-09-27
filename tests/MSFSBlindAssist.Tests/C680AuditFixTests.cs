using MSFSBlindAssist.Aircraft.Citation680;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Panel rows the 2026-09-27 audit found reading the wrong variable, or reading a variable
/// that is not a control at all, pinned against the definition.
/// </summary>
public class C680AuditFixTests
{
    private static readonly Dictionary<string, SimVarDefinition> Vars = new SkywardC680Definition().GetVariables();

    [Fact]
    public void TailFloodIsTheVariableThePluginCallsTailFlood()
        => Assert.Equal("SW_SOV_LIGHTS_LOGO", Vars["C680_TAIL_FLOOD"].Name);

    [Theory]
    [InlineData("SW_SOV_LIGHTS_FLOOD_ON")]       // write does not stick; nothing reads it
    [InlineData("SW_SOV_GARMIN_AMBIENT_LIGHT")]  // a computed ambient level, not a switch
    public void DeadOrComputedVariablesAreNotControls(string name)
        => Assert.DoesNotContain(Vars.Values, d => d.Name == name && !d.RenderAsReadOnlyStatus);

    [Fact]
    public void NoSecondRowForTheTailFloodSwitch()
        => Assert.False(Vars.ContainsKey("C680_LOGO"));

    [Theory]
    [InlineData("C680_NAV_LT", "LIGHT NAV")]
    [InlineData("C680_BEACON", "LIGHT BEACON")]
    public void NavAndBeaconAreReadOnlyTheyAreSetOnTheTouchscreen(string key, string simvar)
    {
        Assert.Equal(simvar, Vars[key].Name);
        Assert.True(Vars[key].RenderAsReadOnlyStatus);
    }

    [Fact]
    public void MfdTouchscreenBacklightDrivesBothMfdTouchscreens()
        => Assert.Equal("40 (>L:WTG3000_Gtc_Backlight:2) 40 (>L:WTG3000_Gtc_Backlight:3)",
                        C680Commands.For("C680_KNOB_GTC_MFD", 40).Single().Code);

    [Theory]
    [InlineData("C680_AI_BOOST_L")]
    [InlineData("C680_AI_BOOST_R")]
    public void N1BoostIsAnOnOffFlag(string key)
    {
        Assert.NotNull(Vars[key].ValueDescriptions);
        Assert.Equal("On", Vars[key].ValueDescriptions![1]);
    }

    // ---- Right tilt (Task 5)

    [Fact]
    public void PressureSourceUsesTheModelsEncoding()
        => Assert.Equal(new[] { "EMER", "L", "NORM", "R", "OFF" }, Vars["C680_PRESS_SRC"].ValueDescriptions!.OrderBy(p => p.Key).Select(p => p.Value));

    [Theory]
    [InlineData("C680_BLEED_L")]
    [InlineData("C680_BLEED_R")]
    public void BleedKnobsUseThePluginsWords(string key)
        => Assert.Equal(new[] { "OFF", "LP", "NORM", "HP" }, Vars[key].ValueDescriptions!.OrderBy(p => p.Key).Select(p => p.Value));

    [Fact]
    public void PassengerOxygenUsesTheModelsEncoding()
        => Assert.Equal(new[] { "OFF", "NORM", "ON" }, Vars["C680_PASS_OXY"].ValueDescriptions!.OrderBy(p => p.Key).Select(p => p.Value));

    [Theory]
    [InlineData(0, "0 (>L:Mask_Selector_Position) 0 (>L:Oxy_Flow) 0 (>L:Oxy_Flow_Force)")]
    [InlineData(1, "1 (>L:Mask_Selector_Position) 1 (>L:Oxy_Flow) 0 (>L:Oxy_Flow_Force)")]
    [InlineData(2, "2 (>L:Mask_Selector_Position) 0 (>L:Oxy_Flow) 1 (>L:Oxy_Flow_Force)")]
    public void PassengerOxygenSetsTheFlowAsTheKnobDoes(double position, string code)
        => Assert.Equal(code, C680Commands.For("C680_PASS_OXY", position).Single().Code);

    [Fact]
    public void DumpNeedsItsCoverOpenAsInTheCockpit()
    {
        Assert.Equal("(B:PRESSURIZATION_Dump_Cover) if{ 1 (>B:PRESSURIZATION_Dump_Set) }", C680Commands.For("C680_PRESS_DUMP", 1).Single().Code);
        Assert.Equal("1 (>B:PRESSURIZATION_Dump_Cover_Set)", C680Commands.For("C680_PRESS_DUMP_COVER", 1).Single().Code);
        Assert.Equal("PRESSURIZATION DUMP SWITCH", Vars["C680_PRESS_DUMP"].Name);
        Assert.Equal(C680SwitchMirror.DumpCover, Vars["C680_PRESS_DUMP_COVER"].Name);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void HydraulicSwitchesNeedTheirCoverOpen(int n)
    {
        Assert.Equal($"(L:HYDRAULICS_Switch_{n}_Cover) 1 == if{{ 1 (>L:SW_SOV_HYDRAULICS_Switch_{n}) }}",
                     C680Commands.For($"C680_HYD_SW_{n}", 1).Single().Code);
        Assert.Equal($"HYDRAULICS_Switch_{n}_Cover", Vars[$"C680_HYD_SW_{n}_COVER"].Name);
    }

    [Theory]
    [InlineData("C680_CKPT_TMP_UP")]
    [InlineData("C680_CKPT_TMP_DN")]
    [InlineData("C680_CABIN_TMP_UP")]
    [InlineData("C680_CABIN_TMP_DN")]
    [InlineData("C680_CABIN_CONTROL")]
    public void DeadTemperatureControlsAreGoneTheTouchscreenOwnsThem(string key)
        => Assert.False(Vars.ContainsKey(key));

    // ---- Glareshield (Task 6)

    [Fact]
    public void XfrIsTheModelsLatch()
        => Assert.Equal("XMLVAR_PushXFR", Vars["C680_XFR"].Name);

    [Theory]
    [InlineData("C680_CRS1_INC", "(>H:AS3000_PFD_1_CRS_INC)")]
    [InlineData("C680_CRS1_DEC", "(>H:AS3000_PFD_1_CRS_DEC)")]
    [InlineData("C680_CRS1_SYNC", "(>H:AS3000_PFD_1_CRS_PUSH)")]
    [InlineData("C680_CRS2_INC", "(>H:AS3000_PFD_2_CRS_INC)")]
    [InlineData("C680_CRS2_SYNC", "(>H:AS3000_PFD_2_CRS_PUSH)")]
    public void CourseKnobsAreTheG3000CourseEvents(string key, string code)
    {
        var c = C680Commands.For(key, 1).Single();
        Assert.Equal(code, c.Code);
        Assert.True(c.Unique);
    }

    [Theory]
    [InlineData("C680_FIRE_L", "(L:SAFETY_Push_Extinguisher_1_Cover) 1 == if{ 1 (>L:SAFETY_Push_Extinguisher_1) }")]
    [InlineData("C680_FIRE_R", "(L:SAFETY_Push_Extinguisher_2_Cover) 1 == if{ 1 (>L:SAFETY_Push_Extinguisher_2) }")]
    [InlineData("C680_FIRE_APU", "(L:SAFETY_Push_Extinguisher_APU_Cover) 1 == if{ 1 (>L:SAFETY_Push_Extinguisher_APU) }")]
    [InlineData("C680_BAG_FIRE", "(L:SAFETY_Push_Baggage_Fire_Cover) 1 == if{ 1 (>L:SAFETY_Push_Baggage_Fire) }")]
    [InlineData("C680_BAG_BOTTLE", "(L:SAFETY_Push_Sec_Bag_Bottle_Cover) 1 == if{ 1 (>L:SAFETY_Push_Sec_Bag_Bottle) }")]
    [InlineData("C680_BOTTLE_L", "1 (>L:SAFETY_Push_Extinguisher_Arm_1)")]
    public void FireButtonsActOnlyAsTheModelLetsThem(string key, string code)
        => Assert.Equal(code, C680Commands.For(key, 1).Single().Code);

    [Theory]
    [InlineData("C680_FIRE_APU", "0 (>L:SAFETY_Push_Extinguisher_APU)")]
    [InlineData("C680_BAG_FIRE", "0 (>L:SAFETY_Push_Baggage_Fire)")]
    [InlineData("C680_BOTTLE_R", "0 (>L:SAFETY_Push_Extinguisher_Arm_2)")]
    [InlineData("C680_FIRE_L", null)]   // the engine fire buttons latch
    public void HeldFirePushesAreReleased(string key, string? release)
        => Assert.Equal(release, C680Commands.ReleaseOf(key));

    [Theory]
    [InlineData("C680_MASTER_WARN", "MSFSBA_C680_MASTER_WARN")]
    [InlineData("C680_MASTER_CAUT", "MSFSBA_C680_MASTER_CAUT")]
    public void MasterLampsAreActiveAndNotAcknowledged(string key, string lvar)
    {
        Assert.Equal(lvar, Vars[key].Name);
        Assert.Contains($"(A:MASTER {(key.EndsWith("WARN") ? "WARNING" : "CAUTION")} ACTIVE, Bool) (A:MASTER {(key.EndsWith("WARN") ? "WARNING" : "CAUTION")} ACKNOWLEDGED, Bool) ! and (>L:{lvar})", C680SwitchMirror.Code);
    }

    [Fact]
    public void StandbyBaroSetIsTheIndexedKohlsmanSet()
        => Assert.Equal("3 16208 (>K:2:KOHLSMAN_SET)", C680Commands.For("C680_SAI_BARO_SET", 1013).Single().Code);

    [Fact]
    public void StandbyKnobIsTheModelsKnob()
    {
        Assert.Equal("(L:LW_SAI_MOD_MENU_OPEN, Bool) 0 == if{ 3 (>K:KOHLSMAN_INC) } els{ 1 (>L:LW_SAI_MOD_SELECTION) }",
                     C680Commands.For("C680_SAI_KNOB_INC", 1).Single().Code);
        Assert.Equal("(L:LW_SAI_MOD_MENU_OPEN, Bool) 0 == if{ (A:KOHLSMAN SETTING STD:3, Bool) ! (>A:KOHLSMAN SETTING STD:3, Bool) } els{ (L:LW_SAI_MOD_MENU_INDEX, number) 0 == if{ 1 (>L:LW_SAI_MOD_SELECTION_CONFIRM, Bool) } }",
                     C680Commands.For("C680_SAI_KNOB_PUSH", 1).Single().Code);
    }

    [Theory]
    [InlineData("C680_SAI_BL_MODE")]
    [InlineData("C680_SAI_QNH_UNIT")]
    [InlineData("C680_SAI_METER")]
    [InlineData("C680_SAI_TURN")]
    [InlineData("C680_SAI_GS")]
    [InlineData("C680_SAI_LIMITS")]
    public void StandbyEfbSettingsAreNotCockpitControls(string key)
        => Assert.False(Vars.ContainsKey(key));
}
