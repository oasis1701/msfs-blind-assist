using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using S = MSFSBlindAssist.Aircraft.BBS757.Bbs757SpringControls;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's spring-loaded cockpit switches, held from the panels for one second (plan 12, task 3): the
/// rudder trim, the aileron trim, the alternate stab trim and the manual cabin altitude switch. The values
/// are measured (2026-10-28, [B757-15]) and read from BlueBird's code: nothing in the aircraft
/// returns a trim switch written 0 or 2 to rest (a lost release ran the rudder trim to its stop twice), so
/// the release always runs and is read back, and re-sent while the switch is off rest. The first half is
/// the pure part (the codes, the phrases, the decisions), CI runs under en-US, so the de-DE and sv-SE cases
/// are the ones that catch a culture leak. The second half is the definition's press, over the shared fake sim.
/// </summary>
public class Bbs757SpringControlsTests
{
    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static Bbs757SpringControl Control(string key) => S.All.Single(c => c.Key == key);

    // ---- The table (measured 2026-10-28) -----------------------------------------------------------------

    [Theory]
    // Plan 12 task 7: the rudder and aileron trim switches sit on their own trim panel at the aft end of the aisle stand
    // (docs/b757.md, "How it writes", Spring switches), so their buttons are on the app's own Rudder and aileron trim panel, listed
    // right after Stabilizer trim; the alternate stab trim switch is on the control stand, with the cutouts.
    [InlineData("BBS757_RUDDER_TRIM", "B757_PEDESTAL_RUDDER_TRIM_KNOB", 1, 0, 2, "Pedestal", "Rudder and aileron trim", "Rudder trim", "Stabilizer trim")]
    [InlineData("BBS757_AILERON_TRIM", "BBS_AILERON_TRIM_SWITCH", 1, 0, 2, "Pedestal", "Rudder and aileron trim", "Aileron trim", "Stabilizer trim")]
    [InlineData("BBS757_ALTN_STAB_TRIM", "BBS_ELEV_TRIM_ALTERNATE", 1, 0, 2, "Pedestal", "Stabilizer trim", "Stab trim", null)]
    [InlineData("BBS757_CABIN_ALT_MANUAL", "B757_OVHD_CABINALT_SWITCH", 4, 0, 7, "Overhead", "Pressurization", "Cabin altitude", null)]
    public void The_four_switches_are_the_measured_ones(string key, string lvar, double rest, double negative, double positive,
        string section, string panel, string name, string? panelAfter)
    {
        var c = Control(key);

        Assert.Equal((lvar, rest, negative, positive, section, panel, name, panelAfter),
            (c.LVar, c.Rest, c.Negative, c.Positive, c.Section, c.Panel, c.Name, c.PanelAfter));
        Assert.Equal(key == "BBS757_CABIN_ALT_MANUAL", c.NeedsManualMode);
    }

    [Fact]
    public void Each_switch_has_its_readout()
    {
        Assert.Equal("B757_PED_RUDDER_TRIM_INDICATOR", S.RudderTrim.ReadoutKey);
        Assert.Equal("B757_FC_YOKE", S.AileronTrim.ReadoutKey);
        Assert.Equal("MON_ElevatorTrim", S.AltnStabTrim.ReadoutKey);   // the trim call-out's own row (stock ELEVATOR TRIM POSITION)
        Assert.Equal("BBS_CABIN_VALVE", S.CabinAltManual.ReadoutKey);
        Assert.Equal(4, S.All.Count);
    }

    [Fact]
    public void The_timings_are_the_planned_ones()
    {
        Assert.Equal(1000, S.HoldMs);
        Assert.Equal(500, S.HoldRewriteMs);
        Assert.Equal(500, S.ReadoutDelayMs);
        Assert.Equal(3, S.MaxReleaseWrites);
        Assert.Equal(250, S.ReleaseReadBackMs);
        Assert.Equal(2000, S.ReleaseRetryIntervalMs);   // after a lost release: every 2 s ...
        Assert.Equal(60000, S.ReleaseRetryForMs);       // ... for up to 60 s
    }

    [Fact]
    public void The_buttons_are_named_and_ordered_as_the_panels_list_them()
    {
        // The side first ([VAR-1]: where rows share a phrase, the discriminator comes first).
        Assert.Equal(new[]
        {
            ("BBS757_RUDDER_TRIM_LEFT", "Left Rudder Trim 1 s", "BBS757_RUDDER_TRIM", false),
            ("BBS757_RUDDER_TRIM_RIGHT", "Right Rudder Trim 1 s", "BBS757_RUDDER_TRIM", true),
            ("BBS757_AILERON_TRIM_LEFT", "Left Aileron Trim 1 s", "BBS757_AILERON_TRIM", false),
            ("BBS757_AILERON_TRIM_RIGHT", "Right Aileron Trim 1 s", "BBS757_AILERON_TRIM", true),
            ("BBS757_ALTN_STAB_TRIM_NOSE_DOWN", "Nose Down Stab Trim 1 s", "BBS757_ALTN_STAB_TRIM", false),
            ("BBS757_ALTN_STAB_TRIM_NOSE_UP", "Nose Up Stab Trim 1 s", "BBS757_ALTN_STAB_TRIM", true),
            ("BBS757_CABIN_ALT_MANUAL_CLIMB", "Climb Cabin 1 s", "BBS757_CABIN_ALT_MANUAL", true),
            ("BBS757_CABIN_ALT_MANUAL_DESCEND", "Descend Cabin 1 s", "BBS757_CABIN_ALT_MANUAL", false),
        }, S.Buttons.Select(b => (b.Key, b.Label, b.Control.Key, b.Positive)));

        foreach (var b in S.Buttons) Assert.Same(b, S.ForButtonKey(b.Key));
        Assert.Null(S.ForButtonKey("B757_OVHD_MODE_SELECT_SWITCH"));
        Assert.Null(S.ForButtonKey(S.RudderTrim.Key));   // a control's key is no button
    }

    [Fact]
    public void A_button_key_is_never_a_typed_field()
    {
        // MainForm renders a key containing "_SET" as a text box.
        Assert.All(S.Buttons, b => Assert.DoesNotContain("_SET", b.Key, StringComparison.Ordinal));
    }

    // ---- Codes ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM", false, "0 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)", "1 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)")]
    [InlineData("BBS757_RUDDER_TRIM", true, "2 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)", "1 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)")]
    [InlineData("BBS757_AILERON_TRIM", false, "0 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)")]
    [InlineData("BBS757_AILERON_TRIM", true, "2 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)")]
    [InlineData("BBS757_ALTN_STAB_TRIM", false, "0 (>L:BBS_ELEV_TRIM_ALTERNATE)", "1 (>L:BBS_ELEV_TRIM_ALTERNATE)")]
    [InlineData("BBS757_ALTN_STAB_TRIM", true, "2 (>L:BBS_ELEV_TRIM_ALTERNATE)", "1 (>L:BBS_ELEV_TRIM_ALTERNATE)")]
    [InlineData("BBS757_CABIN_ALT_MANUAL", false, "0 (>L:B757_OVHD_CABINALT_SWITCH)", "4 (>L:B757_OVHD_CABINALT_SWITCH)")]
    [InlineData("BBS757_CABIN_ALT_MANUAL", true, "7 (>L:B757_OVHD_CABINALT_SWITCH)", "4 (>L:B757_OVHD_CABINALT_SWITCH)")]
    public void Codes_hold_one_side_and_release_to_rest(string key, bool positive, string hold, string release)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal((hold, release), S.Codes(Control(key), positive)));
    }

    // ---- Decisions --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM", 1.0, true)]
    [InlineData("BBS757_RUDDER_TRIM", 1.0004, true)]   // the read-back slack
    [InlineData("BBS757_RUDDER_TRIM", 2.0, false)]
    [InlineData("BBS757_RUDDER_TRIM", 0.0, false)]
    [InlineData("BBS757_RUDDER_TRIM", null, false)]    // unread: not known to rest
    [InlineData("BBS757_RUDDER_TRIM", double.NaN, false)]
    [InlineData("BBS757_CABIN_ALT_MANUAL", 4.0, true)]
    [InlineData("BBS757_CABIN_ALT_MANUAL", 7.0, false)]
    [InlineData("BBS757_CABIN_ALT_MANUAL", 1.0, false)]
    public void At_rest_only_on_the_rest_value(string key, double? reading, bool atRest)
    {
        Assert.Equal(atRest, S.AtRest(Control(key), reading));
    }

    [Theory]
    [InlineData(2.0, null)]
    [InlineData(0.0, "Set the pressurization mode to MAN first")]
    [InlineData(1.0, "Set the pressurization mode to MAN first")]
    [InlineData(null, "Climb Cabin 1 s unavailable")]
    [InlineData(double.NaN, "Climb Cabin 1 s unavailable")]
    public void The_manual_cabin_altitude_works_only_in_man(double? modeSelect, string? refusal)
    {
        Assert.Equal(refusal, S.ManualModeRefusal(modeSelect, "Climb Cabin 1 s"));
        Assert.Equal("B757_OVHD_MODE_SELECT_SWITCH", S.ModeSelectNode);
    }

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM", "Rudder trim switch did not return; the trim may keep running", "Rudder trim switch returned")]
    [InlineData("BBS757_AILERON_TRIM", "Aileron trim switch did not return; the trim may keep running", "Aileron trim switch returned")]
    [InlineData("BBS757_ALTN_STAB_TRIM", "Stab trim switch did not return; the trim may keep running", "Stab trim switch returned")]
    [InlineData("BBS757_CABIN_ALT_MANUAL", "Cabin altitude switch did not return; check the outflow valve", "Cabin altitude switch returned")]
    public void A_switch_that_stays_off_rest_is_said(string key, string phrase, string returned)
    {
        Assert.Equal(phrase, S.DidNotReturnPhrase(Control(key)));
        Assert.Equal(returned, S.ReturnedPhrase(Control(key)));
    }

    // ---- Readout phrases --------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.85, "Rudder trim 0.9 units right")]
    [InlineData(-0.85, "Rudder trim 0.9 units left")]
    [InlineData(12.34, "Rudder trim 12.3 units right")]
    [InlineData(-17.0, "Rudder trim 17.0 units left")]
    [InlineData(0.04, "Rudder trim centered")]       // under 0.05 units
    [InlineData(-0.049, "Rudder trim centered")]
    [InlineData(0.0, "Rudder trim centered")]
    [InlineData(0.05, "Rudder trim 0.1 units right")]
    [InlineData(null, "Rudder trim unavailable")]
    [InlineData(double.NaN, "Rudder trim unavailable")]
    public void Rudder_trim_phrase(double? units, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(phrase, S.RudderTrimPhrase(units)));
    }

    [Theory]
    [InlineData(1.5, "Aileron trim centered")]
    [InlineData(1.505, "Aileron trim centered")]    // within 0.01 of the yoke's neutral 1.5
    [InlineData(1.495, "Aileron trim centered")]
    [InlineData(1.52, "Aileron trim right")]
    [InlineData(2.4, "Aileron trim right")]
    [InlineData(1.489, "Aileron trim left")]        // the measured residual after the put-back
    [InlineData(0.865, "Aileron trim left")]
    [InlineData(null, "Aileron trim unavailable")]
    [InlineData(double.PositiveInfinity, "Aileron trim unavailable")]
    public void Aileron_trim_phrase(double? yoke, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(phrase, S.AileronTrimPhrase(yoke)));
    }

    [Theory]
    [InlineData(2.3, "Trim 2.9")]     // 2.95 units at load: the strip's own formula
    [InlineData(5.5, "Trim 5.5")]
    [InlineData(-2.0, "Trim 0.4")]    // the strip's floor
    [InlineData(null, "Stab trim unavailable")]
    [InlineData(double.NaN, "Stab trim unavailable")]
    public void Stab_trim_phrase_is_the_trim_call_outs(double? degrees, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () =>
            {
                Assert.Equal(phrase, S.StabTrimPhrase(degrees));
                if (degrees is double d && double.IsFinite(d))
                    Assert.Equal(StabTrimUnitsCallout.Phrase(Math.Round(Bbs757Readouts.StabTrimUnits(d), 1)), S.StabTrimPhrase(d));
            });
    }

    [Theory]
    [InlineData(0.869, "Outflow valve 87 percent open")]
    [InlineData(1.0, "Outflow valve 100 percent open")]
    [InlineData(0.0, "Outflow valve 0 percent open")]
    [InlineData(0.004, "Outflow valve 0 percent open")]
    [InlineData(0.996, "Outflow valve 100 percent open")]
    [InlineData(1.2, "Outflow valve 100 percent open")]   // never past fully open
    [InlineData(null, "Outflow valve unavailable")]
    [InlineData(double.NaN, "Outflow valve unavailable")]
    public void Outflow_valve_phrase(double? open, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(phrase, S.OutflowValvePhrase(open)));
    }

    [Theory]
    [InlineData(0.85)]
    [InlineData(-12.34)]
    [InlineData(0.049)]
    [InlineData(null)]
    [InlineData(double.NaN)]
    public void The_rudder_trim_row_and_its_phrase_share_their_words(double? units)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal($"Rudder trim {S.RudderTrimText(units)}", S.RudderTrimPhrase(units)));
        Assert.Equal("0.9 units right", S.RudderTrimText(0.85));
        Assert.Equal("centered", S.RudderTrimText(0.049));
        Assert.Equal("unavailable", S.RudderTrimText(null));
    }

    [Theory]
    [InlineData(1.52)]
    [InlineData(1.489)]
    [InlineData(1.5)]
    [InlineData(null)]
    [InlineData(double.PositiveInfinity)]
    public void The_aileron_trim_row_and_its_phrase_share_their_words(double? yoke)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal($"Aileron trim {S.AileronTrimText(yoke)}", S.AileronTrimPhrase(yoke)));
        Assert.Equal("right", S.AileronTrimText(1.52));
        Assert.Equal("left", S.AileronTrimText(1.489));
        Assert.Equal("centered", S.AileronTrimText(1.5));
        Assert.Equal("unavailable", S.AileronTrimText(null));
    }

    [Theory]
    [InlineData(0.869)]
    [InlineData(1.2)]
    [InlineData(null)]
    [InlineData(double.NaN)]
    public void The_outflow_valve_row_and_its_phrase_share_their_words(double? open)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal($"Outflow valve {S.OutflowValveText(open)}", S.OutflowValvePhrase(open)));
        Assert.Equal("87 percent open", S.OutflowValveText(0.869));
        Assert.Equal("unavailable", S.OutflowValveText(null));
    }

    [Theory]
    [InlineData(2.3, 2.3, false, "Stab trim did not move")]
    [InlineData(2.3, 2.33, false, "Stab trim did not move")]   // 0.024 units: under 0.05
    [InlineData(2.3, 2.33, true, "Stab trim did not move")]
    [InlineData(2.3, 2.6, false, null)]                        // moved: the trim call-out speaks it
    [InlineData(2.6, 2.3, false, null)]
    [InlineData(2.3, 2.6, true, "Trim 3.2")]                   // moved, the call-out silent: the read-back
    [InlineData(null, 2.6, false, "Trim 3.2")]                 // whether it moved cannot be told: the read-back
    [InlineData(2.3, null, false, "Stab trim unavailable")]
    [InlineData(2.3, double.NaN, true, "Stab trim unavailable")]
    public void Stab_trim_speech_after_a_press(double? before, double? after, bool calloutSilent, string? said)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(said, S.StabTrimSpeech(before, after, calloutSilent)));
    }

    [Fact]
    public void Each_control_speaks_its_own_readout()
    {
        Assert.Equal("Rudder trim 0.9 units right", S.ReadoutPhrase(S.RudderTrim, 0.85));
        Assert.Equal("Aileron trim left", S.ReadoutPhrase(S.AileronTrim, 1.4));
        Assert.Equal("Trim 2.9", S.ReadoutPhrase(S.AltnStabTrim, 2.3));
        Assert.Equal("Outflow valve 87 percent open", S.ReadoutPhrase(S.CabinAltManual, 0.869));
    }

    // ---- The definition: variables and panels -----------------------------------------------------------

    private const string ModeSelectNode = "B757_OVHD_MODE_SELECT_SWITCH";
    private const string StabTrimPanel = "Stabilizer trim";
    private const string RudderAileronPanel = "Rudder and aileron trim";

    private static Bbs757Control ModeSelect() => new()
    {
        Node = ModeSelectNode, Section = "Overhead", Panel = "Pressurization", Label = "Mode Select",
        Kind = Bbs757Kinds.Switch, StateVar = ModeSelectNode,
        Positions = { new(0, "AUTO 1"), new(1, "AUTO 2"), new(2, "MAN") },
    };

    private static Bbs757Control StabCutout() => new()
    {
        Node = "B757_Stab_trim_C_toggle_switch_CLICK", Section = "Pedestal", Panel = StabTrimPanel, Label = "Stab Trim C Cutout",
        Kind = Bbs757Kinds.Toggle, StateVar = "B757_Stab_trim_C_toggle_switch_CLICK",
        Positions = { new(0, "NORM"), new(1, "CUTOUT") },
    };

    private static Bbs757ControlMap MapOf(params Bbs757Control[] controls)
    {
        var map = new Bbs757ControlMap();
        foreach (var control in controls) map.Controls.Add(control);
        return map;
    }

    private static readonly string[] RudderAileronButtons =
    {
        "BBS757_RUDDER_TRIM_LEFT", "BBS757_RUDDER_TRIM_RIGHT", "BBS757_AILERON_TRIM_LEFT", "BBS757_AILERON_TRIM_RIGHT",
    };

    private static readonly string[] StabTrimButtons = { "BBS757_ALTN_STAB_TRIM_NOSE_DOWN", "BBS757_ALTN_STAB_TRIM_NOSE_UP" };

    private static readonly string[] CabinButtons = { "BBS757_CABIN_ALT_MANUAL_CLIMB", "BBS757_CABIN_ALT_MANUAL_DESCEND" };

    private static readonly string[] ReadKeys =
    {
        "B757_PEDESTAL_RUDDER_TRIM_KNOB", "BBS_AILERON_TRIM_SWITCH", "BBS_ELEV_TRIM_ALTERNATE", "B757_OVHD_CABINALT_SWITCH",
        "B757_PED_RUDDER_TRIM_INDICATOR", "B757_FC_YOKE", "BBS_CABIN_VALVE",
    };

    [Fact]
    public void The_shipped_stabilizer_trim_panel_starts_with_the_two_stab_trim_buttons()
    {
        var def = new BBS757Definition();
        var panel = def.GetPanelControls()[StabTrimPanel];
        var vars = def.GetVariables();

        Assert.Equal(StabTrimButtons, panel.Take(2));
        Assert.Equal(new[] { "Nose Down Stab Trim 1 s", "Nose Up Stab Trim 1 s" }, panel.Take(2).Select(k => vars[k].DisplayName));
        // the map's controls follow: the two cutouts, each with its guard just before it
        Assert.Equal(new[]
        {
            "B757_AISLE_STAND_STAB_1_GAURD_SWITCH", "B757_Stab_trim_C_toggle_switch_CLICK",
            "B757_AISLE_STAND_STAB_2_GAURD_SWITCH", "B757_Stab_trim_R_toggle_switch_CLICK",
        }, panel.Skip(2));
    }

    [Fact]
    public void The_shipped_rudder_and_aileron_trim_panel_holds_the_four_trim_buttons_right_after_stabilizer_trim()
    {
        // Plan 12 task 7: the app's own panel (no map control is on it), where the cockpit's aileron / rudder trim panel is:
        // at the aft end of the aisle stand, not on the control stand with the stab trim.
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var pedestal = def.GetPanelStructure()["Pedestal"];

        Assert.Equal(RudderAileronButtons, def.GetPanelControls()[RudderAileronPanel]);
        Assert.Equal(new[] { "Left Rudder Trim 1 s", "Right Rudder Trim 1 s", "Left Aileron Trim 1 s", "Right Aileron Trim 1 s" },
            def.GetPanelControls()[RudderAileronPanel].Select(k => vars[k].DisplayName));
        Assert.Equal(pedestal.IndexOf(StabTrimPanel) + 1, pedestal.IndexOf(RudderAileronPanel));
        Assert.Single(def.GetPanelStructure().SelectMany(p => p.Value), RudderAileronPanel);
        Assert.Equal(RudderAileronPanel, S.RudderAileronTrimPanel);
    }

    [Fact]
    public void The_shipped_pressurization_panel_ends_with_the_cabin_buttons_and_keeps_the_field_first()
    {
        var def = new BBS757Definition();
        var panel = def.GetPanelControls()["Pressurization"];

        Assert.Equal(Bbs757LandingAltitude.SetKey, panel[0]);
        Assert.Equal(CabinButtons, panel.TakeLast(2));
        Assert.Contains(ModeSelectNode, panel);
        Assert.Equal(new[] { "Climb Cabin 1 s", "Descend Cabin 1 s" }, panel.TakeLast(2).Select(k => def.GetVariables()[k].DisplayName));
    }

    [Fact]
    public void An_in_memory_map_places_them_the_same_way()
    {
        var def = new BBS757Definition(MapOf(ModeSelect(), StabCutout())) { NestRunning = () => true };

        Assert.Equal(StabTrimButtons.Append("B757_Stab_trim_C_toggle_switch_CLICK"), def.GetPanelControls()[StabTrimPanel]);
        Assert.Equal(RudderAileronButtons, def.GetPanelControls()[RudderAileronPanel]);
        Assert.Equal(new[] { Bbs757LandingAltitude.SetKey, ModeSelectNode }.Concat(CabinButtons), def.GetPanelControls()["Pressurization"]);
        // Listed once each, Stabilizer trim first under its section and Rudder and aileron trim right after it (plan 04's
        // COM radios panel follows them).
        Assert.Equal(new[] { StabTrimPanel, RudderAileronPanel }, def.GetPanelStructure()["Pedestal"].Take(2));
        Assert.Single(def.GetPanelStructure().SelectMany(p => p.Value), StabTrimPanel);
        Assert.Single(def.GetPanelStructure().SelectMany(p => p.Value), RudderAileronPanel);
        Assert.Equal(new[] { "Pressurization" }, def.GetPanelStructure()["Overhead"]);
    }

    [Fact]
    public void A_map_without_the_panels_still_gets_the_buttons()
    {
        var def = new BBS757Definition(new Bbs757ControlMap()) { NestRunning = () => true };

        Assert.Equal(StabTrimButtons, def.GetPanelControls()[StabTrimPanel]);
        Assert.Equal(RudderAileronButtons, def.GetPanelControls()[RudderAileronPanel]);
        Assert.Equal(new[] { Bbs757LandingAltitude.SetKey }.Concat(CabinButtons), def.GetPanelControls()["Pressurization"]);
        // Stabilizer trim is made first, so the panel that follows it still does
        var pedestal = def.GetPanelStructure()["Pedestal"];
        Assert.Equal(pedestal.IndexOf(StabTrimPanel) + 1, pedestal.IndexOf(RudderAileronPanel));
        Assert.Equal(new[] { "Pressurization" }, def.GetPanelStructure()["Overhead"]);
    }

    [Fact]
    public void A_map_with_other_pedestal_panels_lists_the_rudder_and_aileron_trim_right_after_stabilizer_trim()
    {
        var radio = new Bbs757Control
        {
            Node = "B757_PED_NAV_1_OFF_BUTTON", Section = "Pedestal", Panel = "Radio tuning panel 1", Label = "Nav 1 Off",
            Kind = Bbs757Kinds.Button, Press = "1 (>L:B757_PED_NAV_1_OFF_BUTTON)",
        };
        var def = new BBS757Definition(MapOf(radio, StabCutout())) { NestRunning = () => true };

        Assert.Equal(new[] { "Radio tuning panel 1", StabTrimPanel, RudderAileronPanel }, def.GetPanelStructure()["Pedestal"].Take(3));
    }

    [Fact]
    public void Registers_eight_buttons_that_send_nothing_generic()
    {
        var vars = new BBS757Definition().GetVariables();

        foreach (var b in S.Buttons)
        {
            var def = vars[b.Key];
            Assert.Equal(b.Label, def.DisplayName);
            Assert.True(def.RenderAsButton);
            Assert.True(def.SuppressRestingButtonState);
            // An event registration takes no data definition, and a dotted private name changes nothing if ever sent.
            Assert.Equal(SimVarType.Event, def.Type);
            Assert.StartsWith("BBS757.", def.Name, StringComparison.Ordinal);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
            Assert.False(def.IsAnnounced);
        }
        Assert.Equal(8, S.Buttons.Select(b => vars[b.Key].Name).Distinct().Count());
    }

    private static readonly string[] SwitchReadKeys =
        { "B757_PEDESTAL_RUDDER_TRIM_KNOB", "BBS_AILERON_TRIM_SWITCH", "BBS_ELEV_TRIM_ALTERNATE", "B757_OVHD_CABINALT_SWITCH" };

    private const string RudderTrimRow = "B757_PED_RUDDER_TRIM_INDICATOR";
    private const string AileronTrimRow = "B757_FC_YOKE";
    private const string OutflowValveRow = "BBS_CABIN_VALVE";
    private const string CabinAltitudeRow = "B757_TEST_CABIN_ALT";

    [Fact]
    public void Registers_the_seven_reads_quiet_and_off_ctrl_m()
    {
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var controls = def.GetPanelControls().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);
        var displays = def.GetPanelDisplayVariables().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);
        var monitorRows = def.MonitorRows().Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        foreach (string key in ReadKeys)
        {
            var read = vars[key];
            Assert.Equal(key, read.Name);
            Assert.Equal(SimVarType.LVar, read.Type);
            Assert.Equal(UpdateFrequency.OnRequest, read.UpdateFrequency);
            Assert.False(read.IsAnnounced);
            Assert.True(read.ExcludeFromMonitorManager);   // no Ctrl+M row that mutes nothing ([VAR-9])
            Assert.DoesNotContain(key, monitorRows);
            Assert.False(read.RenderAsButton);
            Assert.DoesNotContain(key, controls);
        }
        // The four switches are read only by a press: on no panel's status display either.
        foreach (string key in SwitchReadKeys)
        {
            Assert.False(vars[key].RenderAsReadOnlyStatus);
            Assert.DoesNotContain(key, displays);
        }
        // The stab trim and Mode Select readouts are rows the profile already registers.
        Assert.Equal("ELEVATOR TRIM POSITION", vars[S.StabTrimReadoutKey].Name);
        Assert.Equal(UpdateFrequency.OnRequest, vars[ModeSelectNode].UpdateFrequency);
    }

    // ---- The definition: the readout rows (sighted parity: read without pressing a trim) --------------------

    [Theory]
    [InlineData(RudderTrimRow, "Rudder Trim")]
    [InlineData(AileronTrimRow, "Aileron Trim")]
    [InlineData(OutflowValveRow, "Outflow Valve")]
    public void The_three_readouts_are_read_only_status_rows(string key, string label)
    {
        var read = new BBS757Definition().GetVariables()[key];

        Assert.Equal(label, read.DisplayName);
        Assert.True(read.RenderAsReadOnlyStatus);
        Assert.Equal(UpdateFrequency.OnRequest, read.UpdateFrequency);   // read when the panel's status display is shown
        Assert.True(read.ExcludeFromMonitorManager);
        Assert.False(read.IsAnnounced);
    }

    [Fact]
    public void The_shipped_panels_show_the_readout_rows()
    {
        var displays = new BBS757Definition().GetPanelDisplayVariables();

        // plan 12 task 7: the rudder and aileron trim rows are on their buttons' panel; Stabilizer trim shows none of them
        Assert.Equal(new[] { RudderTrimRow, AileronTrimRow }, displays[RudderAileronPanel]);
        Assert.DoesNotContain(RudderTrimRow, displays.GetValueOrDefault(StabTrimPanel) ?? new List<string>());
        Assert.DoesNotContain(AileronTrimRow, displays.GetValueOrDefault(StabTrimPanel) ?? new List<string>());
        Assert.Equal(new[] { Bbs757LandingAltitude.DisplayKey, OutflowValveRow }, displays["Pressurization"]);   // after the landing altitude's row
    }

    [Fact]
    public void The_readout_rows_follow_the_panels_existing_rows()
    {
        var map = MapOf(ModeSelect(), StabCutout());
        map.Status.Add(new Bbs757StatusRow { Key = "B757_STAB_SOMETHING", Panel = StabTrimPanel, Label = "Something" });
        map.Status.Add(new Bbs757StatusRow { Key = "B757_PRESS_CABIN_ALT", Panel = "Pressurization", Label = "Cabin Altitude" });
        var displays = new BBS757Definition(map) { NestRunning = () => true }.GetPanelDisplayVariables();

        Assert.Equal(new[] { "B757_STAB_SOMETHING" }, displays[StabTrimPanel]);   // the trim rows are on Rudder and aileron trim
        Assert.Equal(new[] { RudderTrimRow, AileronTrimRow }, displays[RudderAileronPanel]);
        Assert.Equal(new[] { "B757_PRESS_CABIN_ALT", Bbs757LandingAltitude.DisplayKey, OutflowValveRow }, displays["Pressurization"]);
    }

    [Fact]
    public void A_map_without_the_panels_still_gets_the_readout_rows()
    {
        var def = new BBS757Definition(new Bbs757ControlMap()) { NestRunning = () => true };

        Assert.Equal(new[] { RudderTrimRow, AileronTrimRow }, def.GetPanelDisplayVariables()[RudderAileronPanel]);
        Assert.Equal(new[] { Bbs757LandingAltitude.DisplayKey, OutflowValveRow }, def.GetPanelDisplayVariables()["Pressurization"]);
        Assert.Contains(RudderAileronPanel, def.GetPanelStructure()["Pedestal"]);
    }

    [Theory]
    [InlineData(RudderTrimRow, 0.85, "0.9 units right")]
    [InlineData(RudderTrimRow, -3.44, "3.4 units left")]
    [InlineData(RudderTrimRow, 0.04, "centered")]        // under 0.05 units
    [InlineData(RudderTrimRow, double.NaN, "unavailable")]
    [InlineData(AileronTrimRow, 1.56, "right")]
    [InlineData(AileronTrimRow, 1.489, "left")]
    [InlineData(AileronTrimRow, 1.505, "centered")]      // within 0.01 of the yoke's 1.5 neutral
    [InlineData(AileronTrimRow, double.NaN, "unavailable")]
    [InlineData(OutflowValveRow, 0.869, "87 percent open")]
    [InlineData(OutflowValveRow, 1.0, "100 percent open")]
    [InlineData(OutflowValveRow, double.NaN, "unavailable")]
    [InlineData(CabinAltitudeRow, 1161.4, "1161 feet")]   // the map's Status row, BlueBird's cabin altitude in feet
    [InlineData(CabinAltitudeRow, 14100.2, "14100 feet")]
    [InlineData(CabinAltitudeRow, -60, "-60 feet")]
    [InlineData(CabinAltitudeRow, double.NaN, "unavailable")]
    public void Each_readout_row_says_its_value(string key, double value, string text)
    {
        var def = new BBS757Definition();
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () =>
            {
                Assert.True(def.TryGetDisplayOverride(key, value, out string shown));
                Assert.Equal(text, shown);
            });
    }

    [Fact]
    public void The_cabin_altitude_row_is_the_maps_status_row()
    {
        var def = new BBS757Definition();

        Assert.Equal("Cabin Altitude", def.GetVariables()[CabinAltitudeRow].DisplayName);
        Assert.Contains(CabinAltitudeRow, def.GetPanelDisplayVariables()["Status"]);
    }

    [Fact]
    public void A_key_already_present_is_never_shadowed()
    {
        // A map control on one of the read variables keeps its own definition.
        var yoke = new Bbs757Control
        {
            Node = "B757_FC_YOKE", Section = "Pedestal", Panel = "Control stand", Label = "Yoke",
            Kind = Bbs757Kinds.Switch, StateVar = "B757_FC_YOKE", Positions = { new(0, "A"), new(1, "B") },
        };
        var vars = new BBS757Definition(MapOf(yoke)).GetVariables();

        Assert.Equal("Yoke", vars["B757_FC_YOKE"].DisplayName);
        Assert.Equal(new Dictionary<double, string> { [0] = "A", [1] = "B" }, vars["B757_FC_YOKE"].ValueDescriptions);
    }

    [Fact]
    public void The_profile_gains_fifteen_variables_and_stays_under_the_ceiling()
    {
        var vars = new BBS757Definition().GetVariables();
        var mine = S.Buttons.Select(b => b.Key).Concat(ReadKeys).ToList();

        Assert.Equal(15, mine.Distinct(StringComparer.Ordinal).Count());
        Assert.All(mine, k => Assert.True(vars.ContainsKey(k), k));
        Assert.True(vars.Count < 900, $"{vars.Count} variables");
    }

    // ---- The definition: the press ----------------------------------------------------------------------

    /// <summary>A 757 definition over in-memory controls, the shared fake sim behind its writes and a speech capture.</summary>
    private sealed class Rig
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly SimConnectManager Manager = new(IntPtr.Zero);
        public readonly BBS757Definition Def;

        /// <summary>The Ctrl+M rows the pilot muted.</summary>
        public readonly HashSet<string> Muted = new(StringComparer.Ordinal);

        /// <summary>The definition's clock (its <c>NowMs</c>), in milliseconds: still unless <see cref="DelayTakes"/> moves it, so no test depends on real time.</summary>
        public long Now;

        /// <summary>How far one wait asked for this many milliseconds moves <see cref="Now"/> (a real wait runs long); null: not at all.</summary>
        public Func<int, int>? DelayTakes;

        /// <param name="more">Map controls besides Mode Select and the stab trim cutout (the lamp test's Lamp Test).</param>
        public Rig(params Bbs757Control[] more)
        {
            Def = new BBS757Definition(MapOf(new[] { ModeSelect(), StabCutout() }.Concat(more).ToArray()))
            {
                NestRunning = () => true, WriteIo = _ => Io(), IsMonitorMuted = key => Muted.Contains(key), NowMs = () => Now,
            };
        }

        /// <summary>The fake sim's io, its waits moving the clock by <see cref="DelayTakes"/> first.</summary>
        private Bbs757SimIo Io()
        {
            var io = Sim.Io;
            return new Bbs757SimIo
            {
                CanWrite = io.CanWrite, ReadFresh = io.ReadFresh, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead,
                Delay = (ms, ct) =>
                {
                    Now += DelayTakes?.Invoke(ms) ?? 0;
                    return io.Delay(ms, ct);
                },
            };
        }

        /// <summary>
        /// Every switch reads what was last written to it, as BlueBird's do; <paramref name="also"/> runs after each
        /// write too (a trim that follows its switch).
        /// </summary>
        public Rig FollowingWrites(Action<string>? also = null)
        {
            Sim.OnWrite = rpn =>
            {
                var m = Regex.Match(rpn, @"^(-?[0-9.]+) \(>L:([A-Za-z0-9_]+)\)$");
                if (m.Success) Sim.Fresh[m.Groups[2].Value] = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                also?.Invoke(rpn);
            };
            return this;
        }

        public Task Press(string key) => Def.PressSpringAsync(key, Manager, Speech);

        /// <summary>The panel button's click: MainForm's call into the definition.</summary>
        public bool Click(string key) => Def.HandleUIVariableSet(key, 1, Def.GetVariables()[key], Manager, Speech);
    }

    private const string RudderKnob = "B757_PEDESTAL_RUDDER_TRIM_KNOB";
    private const string RudderRight = "2 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)";
    private const string RudderLeft = "0 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)";
    private const string RudderRelease = "1 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)";
    private const string CabinClimb = "7 (>L:B757_OVHD_CABINALT_SWITCH)";
    private const string CabinDescend = "0 (>L:B757_OVHD_CABINALT_SWITCH)";
    private const string CabinRelease = "4 (>L:B757_OVHD_CABINALT_SWITCH)";

    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_press_holds_rewrites_releases_then_says_the_readout_once()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);

        Assert.Equal(new[]
        {
            Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500),
            Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500),   // the hold, written again at 500 ms
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250), // released at 1000 ms, read back at rest 250 ms later
            Bbs757FakeIo.DelayEvent(500),                                         // the readout lags the release
        }, rig.Sim.Events);
        // The screen reader said the button; the app says only the read-back (CORE-7).
        Assert.Equal(new[] { "Rudder trim 0.9 units right" }, rig.Speech.All);
        Assert.Empty(rig.Sim.Resyncs);
    }

    [Fact]
    public async Task The_hold_is_written_again_at_500_ms()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;   // the first half of the hold
        rig.Sim.Fresh["B757_FC_YOKE"] = 1.44;

        var press = rig.Press("BBS757_AILERON_TRIM_LEFT");
        Assert.Equal(new[] { "0 (>L:BBS_AILERON_TRIM_SWITCH)" }, rig.Sim.Writes);
        Assert.Equal(new[] { 500 }, rig.Sim.Delays);

        rig.Sim.Release(0);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { "0 (>L:BBS_AILERON_TRIM_SWITCH)", "0 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)" },
            rig.Sim.Writes);
        Assert.Equal(new[] { 500, 500, 250, 500 }, rig.Sim.Delays);   // the hold's halves, the release's read-back, the readout
        Assert.Equal(new[] { "Aileron trim left" }, rig.Speech.All);
    }

    [Fact]
    public async Task Release_always_runs()
    {
        // The pilot switches to another profile during the hold: the lifetime is canceled, the hold is not
        // written again, but the release goes and is read back (nothing else returns the switch to rest),
        // and nothing is said into the other aircraft's session.
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderRight }, rig.Sim.Writes);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(0);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRelease }, rig.Sim.Writes);
        Assert.Equal(0, rig.Sim.CanceledDelays);   // the hold's waits are never canceled
        Assert.Equal(new[] { 500, 500, 250 }, rig.Sim.Delays);   // the release's read-back still waits; no readout wait after the switch
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task Release_always_runs_when_the_switch_comes_in_the_second_half()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 1;

        var press = rig.Press("BBS757_RUDDER_TRIM_LEFT");
        Assert.Equal(new[] { RudderLeft, RudderLeft }, rig.Sim.Writes);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(1);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_switch_stuck_after_a_switch_away_still_gets_every_release_and_says_nothing()
    {
        var rig = new Rig();   // the knob keeps reading its hold value
        rig.Sim.Hold = ordinal => ordinal == 0;
        rig.Sim.Fresh[RudderKnob] = 2;

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        rig.Def.OnSwitchedAway();
        rig.Sim.Release(0);
        await press.WaitAsync(Ceiling);

        // The press's three releases, then the background's: one every 2 s for 60 s (a profile switch does not stop it).
        Assert.Equal(new[] { RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 30)), rig.Sim.Writes);
        Assert.Equal(new[] { 500, 500, 250, 250, 250 }.Concat(Enumerable.Repeat(2000, 30)), rig.Sim.Delays);
        Assert.Equal(0, rig.Sim.CanceledDelays);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_press_after_a_switch_away_sends_and_says_nothing()
    {
        var rig = new Rig().FollowingWrites();
        rig.Def.OnSwitchedAway();

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.True(rig.Click("BBS757_CABIN_ALT_MANUAL_CLIMB"));

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Empty(rig.Speech.All);
    }

    private const string RudderDidNotReturn = "Rudder trim switch did not return; the trim may keep running";
    private const string RudderReturned = "Rudder trim switch returned";

    /// <summary>The 0-based ordinal of a rudder press's first background wait: the hold's two, then the three release read-backs.</summary>
    private const int FirstBackgroundWait = 5;

    [Fact]
    public async Task Release_is_resent_until_the_switch_rests()
    {
        // The knob still reads its hold value after every release: three releases in all, then the pilot is told,
        // and the release goes on in the background (its first wait held here).
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;
        rig.Sim.Hold = ordinal => ordinal == FirstBackgroundWait;

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");

        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease, RudderRelease, RudderRelease }, rig.Sim.Writes);
        // Each release is read back 250 ms after it is written, so a read that has not yet seen it cannot count as lost.
        Assert.Equal(new[]
        {
            Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500), Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500),
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250),
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250),
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250),
            Bbs757FakeIo.DelayEvent(2000),   // the background release's first wait (held)
            Bbs757FakeIo.DelayEvent(500),    // the readout, said meanwhile
        }, rig.Sim.Events);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);
        Assert.Contains(RudderDidNotReturn, rig.Speech.Interrupts);
        Assert.False(press.IsCompleted);   // still releasing

        // The switch is back by the first background check: nothing more is written, and that is said once.
        rig.Sim.Fresh[RudderKnob] = 1;
        rig.Sim.Release(FirstBackgroundWait);
        await press.WaitAsync(Ceiling);

        Assert.Equal(5, rig.Sim.Writes.Count);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right", RudderReturned }, rig.Speech.All);
    }

    [Fact]
    public async Task A_lost_release_is_retried_every_2_s_until_the_switch_returns()
    {
        // The switch comes back on the third background release: 3 + 3 release writes, "returned" said once.
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;
        rig.Sim.Hold = ordinal => ordinal == FirstBackgroundWait;
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == RudderRelease && ++releases == 6) rig.Sim.Fresh[RudderKnob] = 1;
        };

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);

        // While it retries, the switch's gate stays taken: neither of its buttons starts a hold.
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.Equal(5, rig.Sim.Writes.Count);

        rig.Sim.Release(FirstBackgroundWait);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 3)), rig.Sim.Writes);
        // Each background check waits 2 s, reads the switch and, while it is off rest, writes the release again.
        Assert.Equal(new[] { 500, 500, 250, 250, 250, 2000, 500, 2000, 2000, 2000 }, rig.Sim.Delays);
        Assert.Single(rig.Speech.All, RudderReturned);
        Assert.Equal(RudderReturned, rig.Speech.All[^1]);
        Assert.Equal(0, rig.Sim.CanceledDelays);

        // The gate is free again.
        rig.FollowingWrites();
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes.TakeLast(3));
    }

    [Fact]
    public async Task A_switch_that_never_returns_is_released_for_60_s_then_left_quietly()
    {
        var rig = new Rig();   // the knob keeps reading its hold value
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 30)), rig.Sim.Writes);
        var background = rig.Sim.Delays.Where(d => d == 2000).ToList();
        Assert.Equal(30, background.Count);
        Assert.Equal(60000, background.Sum());   // the loop ends at 60 s
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);   // nothing more is said

        // The gate is freed: the next press holds the switch again.
        rig.FollowingWrites();
        int before = rig.Sim.Writes.Count;
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes.Skip(before));
    }

    [Fact]
    public async Task A_switch_away_during_the_retries_keeps_releasing_and_says_nothing()
    {
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;
        rig.Sim.Hold = ordinal => ordinal == FirstBackgroundWait;
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == RudderRelease && ++releases == 6) rig.Sim.Fresh[RudderKnob] = 1;
        };

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(FirstBackgroundWait);
        await press.WaitAsync(Ceiling);

        // The writes continue (the release is the safety); its return is not said into another aircraft's session.
        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 3)), rig.Sim.Writes);
        Assert.Equal(0, rig.Sim.CanceledDelays);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_release_read_back_waits_for_the_write_to_land()
    {
        // The knob reads its old value until the release's wait has passed: one release, no false alarm.
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;
        rig.Sim.Hold = ordinal => ordinal == 2;   // the release's read-back wait
        rig.Sim.OnWrite = rpn => { if (rpn == RudderRelease) rig.Sim.Fresh[RudderKnob] = 2; };   // not landed yet

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease }, rig.Sim.Writes);

        rig.Sim.Fresh[RudderKnob] = 1;   // the write lands within the wait
        rig.Sim.Release(2);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Rudder trim 0.9 units right" }, rig.Speech.All);
    }

    [Fact]
    public async Task Release_stops_once_the_switch_reads_rest()
    {
        // At rest after the second release: exactly two release writes, nothing but the readout said.
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = -0.85;
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == RudderRelease && ++releases == 2) rig.Sim.Fresh[RudderKnob] = 1;
        };

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease, RudderRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { 500, 500, 250, 250, 500 }, rig.Sim.Delays);
        Assert.Equal(new[] { "Rudder trim 0.9 units left" }, rig.Speech.All);
    }

    [Fact]
    public async Task An_unreadable_switch_keeps_being_released_and_is_said()
    {
        var rig = new Rig();   // the knob is never read
        rig.Sim.Fresh["BBS_CABIN_VALVE"] = 0.5;
        rig.Sim.Fresh[ModeSelectNode] = 2;

        await rig.Press("BBS757_CABIN_ALT_MANUAL_DESCEND").WaitAsync(Ceiling);

        // Three releases, then one every 2 s for 60 s: an unread switch is never known to rest.
        Assert.Equal(new[] { CabinDescend, CabinDescend }.Concat(Enumerable.Repeat(CabinRelease, 3 + 30)), rig.Sim.Writes);
        Assert.Equal(new[] { "Cabin altitude switch did not return; check the outflow valve", "Outflow valve 50 percent open" }, rig.Speech.All);
    }

    [Theory]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", 0.0, "Set the pressurization mode to MAN first")]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", 1.0, "Set the pressurization mode to MAN first")]
    [InlineData("BBS757_CABIN_ALT_MANUAL_DESCEND", 0.0, "Set the pressurization mode to MAN first")]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", null, "Climb Cabin 1 s unavailable")]      // Mode Select unread
    [InlineData("BBS757_CABIN_ALT_MANUAL_DESCEND", null, "Descend Cabin 1 s unavailable")]
    public async Task Cabin_buttons_refused_outside_man(string key, double? modeSelect, string said)
    {
        var rig = new Rig().FollowingWrites();
        if (modeSelect is double mode) rig.Sim.Fresh[ModeSelectNode] = mode;

        await rig.Press(key).WaitAsync(Ceiling);

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Equal(new[] { said }, rig.Speech.All);
    }

    [Theory]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", CabinClimb)]
    [InlineData("BBS757_CABIN_ALT_MANUAL_DESCEND", CabinDescend)]
    public async Task Cabin_buttons_hold_in_man(string key, string hold)
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Fresh[ModeSelectNode] = 2;
        rig.Sim.Fresh["BBS_CABIN_VALVE"] = 0.869;

        await rig.Press(key).WaitAsync(Ceiling);

        Assert.Equal(new[] { hold, hold, CabinRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Outflow valve 87 percent open" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_press_that_cannot_land_is_refused_aloud_and_sends_nothing()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.CanWrite = false;
        rig.Sim.Fresh[ModeSelectNode] = 2;

        foreach (var b in S.Buttons) await rig.Press(b.Key).WaitAsync(Ceiling);

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Equal(S.Buttons.Select(b => $"{b.Label} unavailable"), rig.Speech.All);
    }

    [Fact]
    public async Task A_press_while_that_control_holds_is_ignored()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;   // the rudder's first wait
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;
        rig.Sim.Fresh["B757_FC_YOKE"] = 1.5;

        var first = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);    // the same switch: ignored, silently
        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderRight }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);

        // Another switch holds on its own.
        await rig.Press("BBS757_AILERON_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderRight, "2 (>L:BBS_AILERON_TRIM_SWITCH)", "2 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)" },
            rig.Sim.Writes);

        rig.Sim.Release(0);
        await first.WaitAsync(Ceiling);
        Assert.Equal(new[] { "Aileron trim centered", "Rudder trim 0.9 units right" }, rig.Speech.All);

        // Once released, the rudder holds again.
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes.TakeLast(3));
    }

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM_LEFT", "B757_PED_RUDDER_TRIM_INDICATOR", -3.4, "Rudder trim 3.4 units left")]
    [InlineData("BBS757_RUDDER_TRIM_RIGHT", "B757_PED_RUDDER_TRIM_INDICATOR", 0.02, "Rudder trim centered")]
    [InlineData("BBS757_AILERON_TRIM_LEFT", "B757_FC_YOKE", 1.44, "Aileron trim left")]
    [InlineData("BBS757_AILERON_TRIM_RIGHT", "B757_FC_YOKE", 1.56, "Aileron trim right")]
    [InlineData("BBS757_ALTN_STAB_TRIM_NOSE_UP", "NOTHING_READ", 0.0, "Stab trim unavailable")]   // the trim unread
    public async Task Each_button_says_its_readout(string key, string readoutKey, double value, string said)
    {
        foreach (string culture in new[] { "en-US", "de-DE" })
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var rig = new Rig().FollowingWrites();
                rig.Sim.Fresh[readoutKey] = value;

                await rig.Press(key).WaitAsync(Ceiling);

                Assert.Equal(new[] { said }, rig.Speech.All);
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }
    }

    // ---- The stab trim: the trim call-out speaks its move (CORE-7) -----------------------------------------

    private const string StabNoseUp = "2 (>L:BBS_ELEV_TRIM_ALTERNATE)";
    private const string StabRelease = "1 (>L:BBS_ELEV_TRIM_ALTERNATE)";

    /// <summary>A rig whose stabilizer goes from 2.3 to <paramref name="after"/> degrees while the nose-up switch holds.</summary>
    private static Rig StabRig(double after)
    {
        var rig = new Rig();
        rig.Sim.Fresh["MON_ElevatorTrim"] = 2.3;
        rig.FollowingWrites(rpn => { if (rpn == StabNoseUp) rig.Sim.Fresh["MON_ElevatorTrim"] = after; });
        return rig;
    }

    [Fact]
    public async Task A_stab_trim_press_that_did_not_move_it_says_so()
    {
        // No hydraulic pressure, or a cutout in CUTOUT: the switch holds and the trim stays where it was.
        var rig = StabRig(after: 2.3);

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { StabNoseUp, StabNoseUp, StabRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Stab trim did not move" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_that_moved_it_is_left_to_the_trim_call_out()
    {
        // The trim call-out ("Trim 3.2") speaks while the stabilizer moves: the press says nothing more.
        var rig = StabRig(after: 2.6);

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { StabNoseUp, StabNoseUp, StabRelease }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_that_moved_it_says_the_trim_when_its_call_out_is_muted()
    {
        var rig = StabRig(after: 2.6);
        rig.Muted.Add("MON_ElevatorTrim");   // the trim call-out's Ctrl+M row

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { "Trim 3.2" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_that_moved_it_says_the_trim_when_trim_announcements_are_off()
    {
        var rig = StabRig(after: 2.6);
        Assert.True(rig.Def.HandleHotkeyAction(MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleTrimAnnouncements, rig.Manager, rig.Speech, null!, null!));

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { "Trim announcements off", "Trim 3.2" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_reads_the_trim_before_it_holds()
    {
        // The trim before is read fresh, not from a stale cache: a cached value that differs changes nothing.
        var rig = StabRig(after: 2.3);
        rig.Sim.Cache["MON_ElevatorTrim"] = 1.0;

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_DOWN").WaitAsync(Ceiling);

        Assert.Equal(new[] { "0 (>L:BBS_ELEV_TRIM_ALTERNATE)", "0 (>L:BBS_ELEV_TRIM_ALTERNATE)", StabRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Stab trim did not move" }, rig.Speech.All);
    }

    [Fact]
    public void Every_button_click_is_claimed_and_runs_the_hold()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Fresh[ModeSelectNode] = 2;

        foreach (var b in S.Buttons)
        {
            int before = rig.Sim.Writes.Count;
            Assert.True(rig.Click(b.Key));   // the definition claims it: the generic path sends nothing
            var (hold, release) = S.Codes(b.Control, b.Positive);
            Assert.Equal(new[] { hold, hold, release }, rig.Sim.Writes.Skip(before));
        }
    }

    [Fact]
    public void Other_keys_are_not_claimed_by_the_spring_buttons()
    {
        var rig = new Rig();

        Assert.False(rig.Def.HandleUIVariableSet("SOME_OTHER_BUTTON", 1, new SimVarDefinition(), rig.Manager, rig.Speech));
        // The landing altitude field is still claimed and sends nothing.
        Assert.True(rig.Click(Bbs757LandingAltitude.SetKey));
        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_read_that_throws_never_escapes_and_the_release_still_goes()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = io.CanWrite, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
            ReadFresh = (_, _) => throw new InvalidOperationException("no link"),
        };
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => failing, NowMs = () => 0 };
        var speech = new SpeechCapture();

        await def.PressSpringAsync("BBS757_RUDDER_TRIM_RIGHT", new SimConnectManager(IntPtr.Zero), speech).WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 30)), sim.Writes);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim unavailable" }, speech.All);
    }

    [Fact]
    public async Task A_mode_select_read_that_throws_refuses_the_cabin_button()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = io.CanWrite, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
            ReadFresh = (_, _) => throw new InvalidOperationException("no link"),
        };
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => failing };
        var speech = new SpeechCapture();

        await def.PressSpringAsync("BBS757_CABIN_ALT_MANUAL_CLIMB", new SimConnectManager(IntPtr.Zero), speech).WaitAsync(Ceiling);

        Assert.Empty(sim.Writes);
        Assert.Equal(new[] { "Climb Cabin 1 s unavailable" }, speech.All);
    }

    // =====================================================================================================
    // Plan 12 task 5b: the P61 test switches, held for 3 s from a button per test, and the lamp test
    // =====================================================================================================
    //
    // Measured in flight on 2026-10-28 (docs/b757.md, "Held tests and spring switches"): a P61 test switch
    // written once is back on neutral (1) at once (the Asobo 3-state template's STATE0_TIMER 0.01), so a test runs only
    // while its position is written again: every 30 ms it held and the test ran steadily, every ~130 ms it dropped out.

    private static Bbs757SpringControl TestSwitch(string key) => S.TestSwitches.Single(c => c.Key == key);

    private static Bbs757SpringButton TestButton(string key) => S.TestButtons.Single(b => b.Key == key);

    [Theory]
    [InlineData("BBS757_P61_GROUND_PROX_HYD_GEN_TEST", "BBS757_GRNDPROX_HYDGEN_P61_TEST_SW", "Ground proximity and hydraulic generator test")]
    [InlineData("BBS757_P61_LEFT_AIR_DATA_STALL_TEST", "BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW", "Left air data and stall warning test")]
    [InlineData("BBS757_P61_RIGHT_AIR_DATA_STALL_TEST", "BBS757_AIRDATA_STALL_RIGHT_P61_TEST_SW", "Right air data and stall warning test")]
    [InlineData("BBS757_P61_CONFIG_TEST", "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW", "Takeoff and landing config test")]
    [InlineData("BBS757_P61_EQUIP_COOLING_TEST", "BBS757_EQUIP_COOLING_P61_TEST_SW", "Equipment cooling test")]
    [InlineData("BBS757_P61_YAW_DAMPER_TEST", "BBS757_YAW_DAMP_P61_TEST_SW", "Yaw damper test")]
    [InlineData("BBS757_P61_WING_AI_PROBE_HEAT_TEST", "BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW", "Wing anti-ice and probe heat test")]
    public void The_seven_test_switches_are_the_measured_ones(string key, string lvar, string name)
    {
        var c = TestSwitch(key);

        // Rests on neutral (1); 0 and 2 are its two tests.
        Assert.Equal((lvar, 1.0, 0.0, 2.0, "Other", "P61 panel", name), (c.LVar, c.Rest, c.Negative, c.Positive, c.Section, c.Panel, c.Name));
        Assert.Equal((3000, 30), (c.HoldMs, c.RewriteMs));   // held 3 s, written again every 30 ms
        Assert.Null(c.ReadoutKey);                           // a test reads nothing back: its press says "{test} done"
        Assert.False(c.NeedsManualMode);
    }

    [Fact]
    public void The_test_switches_are_listed_in_the_panels_order_and_apart_from_the_trims()
    {
        Assert.Equal(new[]
        {
            "BBS757_P61_GROUND_PROX_HYD_GEN_TEST", "BBS757_P61_LEFT_AIR_DATA_STALL_TEST", "BBS757_P61_RIGHT_AIR_DATA_STALL_TEST",
            "BBS757_P61_CONFIG_TEST", "BBS757_P61_EQUIP_COOLING_TEST", "BBS757_P61_YAW_DAMPER_TEST",
            "BBS757_P61_WING_AI_PROBE_HEAT_TEST",
        }, S.TestSwitches.Select(c => c.Key));
        Assert.Empty(S.TestSwitches.Intersect(S.All));
        Assert.Equal(11, S.All.Concat(S.TestSwitches).Select(c => c.Key).Distinct().Count());
        Assert.Equal(11, S.All.Concat(S.TestSwitches).Select(c => c.LVar).Distinct().Count());
    }

    private const string DuctLeakFuelQtySwitch = "BBS757_DUCT_LEAK_FUEL_QTY_P61_TEST_SW";

    [Fact]
    public void The_duct_leak_fuel_quantity_switch_gets_no_button_because_nothing_reads_it()
    {
        // Plan 12 task 12 (the final review's I1): nothing in BlueBird reads BBS757_DUCT_LEAK_FUEL_QTY_P61_TEST_SW (not the
        // WASM, no XML but its own switch), and holding it on the ground with BBS Nest running changed no variable
        // (2026-10-28), so a button saying "… test done" would promise a test that never runs.
        var def = new BBS757Definition();
        var vars = def.GetVariables();

        Assert.DoesNotContain(S.TestSwitches, c => c.LVar == DuctLeakFuelQtySwitch);
        Assert.DoesNotContain(S.TestButtons, b => b.Key.Contains("DUCT_LEAK", StringComparison.Ordinal));
        Assert.DoesNotContain(S.TestButtons, b => b.Label.Contains("Duct Leak", StringComparison.Ordinal));
        Assert.False(vars.ContainsKey(DuctLeakFuelQtySwitch));
        Assert.Null(S.ForButtonKey("BBS757_P61_UPPER_DUCT_LEAK_FUEL_QTY_TEST"));
        Assert.Null(S.ForButtonKey("BBS757_P61_LOWER_DUCT_LEAK_FUEL_QTY_TEST"));
        Assert.DoesNotContain(def.GetPanelControls()[TestPanel], k => k.Contains("DUCT_LEAK", StringComparison.Ordinal));
    }

    [Fact]
    public void The_trims_and_the_cabin_switch_keep_their_one_second_hold()
    {
        Assert.All(S.All, c => Assert.Equal((1000, 500), (c.HoldMs, c.RewriteMs)));
        Assert.Equal(3000, S.TestHoldMs);
        Assert.Equal(30, S.TestRewriteMs);
    }

    [Fact]
    public void Every_hold_is_written_again_within_its_length()
    {
        foreach (var c in S.All.Concat(S.TestSwitches))
            Assert.True(c.RewriteMs > 0 && c.RewriteMs <= c.HoldMs, $"{c.Key}: {c.RewriteMs} ms within {c.HoldMs} ms");
    }

    private static readonly (string Key, string Label, string LVar, double Value, string Done)[] TestButtonRows =
    {
        ("BBS757_P61_GROUND_PROX_TEST", "Ground Proximity Test 3 s", "BBS757_GRNDPROX_HYDGEN_P61_TEST_SW", 0, "Ground proximity test done"),
        ("BBS757_P61_HYD_GEN_TEST", "Hydraulic Generator Test 3 s", "BBS757_GRNDPROX_HYDGEN_P61_TEST_SW", 2, "Hydraulic generator test done"),
        ("BBS757_P61_LEFT_STALL_TEST", "Left Stall Warning Test 3 s", "BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW", 2, "Left stall warning test done"),
        ("BBS757_P61_LEFT_AIR_DATA_TEST", "Left Air Data Test 3 s", "BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW", 0, "Left air data test done"),
        ("BBS757_P61_RIGHT_STALL_TEST", "Right Stall Warning Test 3 s", "BBS757_AIRDATA_STALL_RIGHT_P61_TEST_SW", 2, "Right stall warning test done"),
        ("BBS757_P61_RIGHT_AIR_DATA_TEST", "Right Air Data Test 3 s", "BBS757_AIRDATA_STALL_RIGHT_P61_TEST_SW", 0, "Right air data test done"),
        ("BBS757_P61_TAKEOFF_CONFIG_TEST", "Takeoff Config Test 3 s", "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW", 0, "Takeoff config test done"),
        ("BBS757_P61_LANDING_CONFIG_TEST", "Landing Config Test 3 s", "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW", 2, "Landing config test done"),
        ("BBS757_P61_UPPER_EQUIP_COOLING_TEST", "Upper Equipment Cooling Test 3 s", "BBS757_EQUIP_COOLING_P61_TEST_SW", 0, "Upper equipment cooling test done"),
        ("BBS757_P61_LOWER_EQUIP_COOLING_TEST", "Lower Equipment Cooling Test 3 s", "BBS757_EQUIP_COOLING_P61_TEST_SW", 2, "Lower equipment cooling test done"),
        ("BBS757_P61_UPPER_YAW_DAMPER_TEST", "Upper Yaw Damper Test 3 s", "BBS757_YAW_DAMP_P61_TEST_SW", 0, "Upper yaw damper test done"),
        ("BBS757_P61_LOWER_YAW_DAMPER_TEST", "Lower Yaw Damper Test 3 s", "BBS757_YAW_DAMP_P61_TEST_SW", 2, "Lower yaw damper test done"),
        ("BBS757_P61_WING_AI_TEST", "Wing Anti-ice Test 3 s", "BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW", 0, "Wing anti-ice test done"),
        ("BBS757_P61_PROBE_HEAT_TEST", "Probe Heat Test 3 s", "BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW", 2, "Probe heat test done"),
    };

    private static readonly string[] TestButtonKeys = TestButtonRows.Select(r => r.Key).ToArray();

    [Fact]
    public void The_fourteen_test_buttons_are_named_and_ordered_as_the_panel_lists_them()
    {
        // One button per test position, the discriminator first ([VAR-1]), in the order the switches stood on the panel.
        Assert.Equal(TestButtonRows.Select(r => (r.Key, r.Label, r.LVar, r.Value)),
            S.TestButtons.Select(b => (b.Key, b.Label, b.Control.LVar, b.Positive ? b.Control.Positive : b.Control.Negative)));

        foreach (var b in S.TestButtons) Assert.Same(b, S.ForButtonKey(b.Key));
        Assert.Equal(14, S.TestButtons.Select(b => b.Label).Distinct().Count());   // no two rows of the panel share a name
        Assert.All(S.TestButtons, b => Assert.DoesNotContain("_SET", b.Key, StringComparison.Ordinal));   // never a typed field
        Assert.Empty(S.TestButtons.Select(b => b.Key).Intersect(S.Buttons.Select(b => b.Key)));
        // The trims' and the cabin's eight stay as they were.
        Assert.Equal(8, S.Buttons.Count);
    }

    [Fact]
    public void Each_test_switch_has_one_button_per_test_position()
    {
        foreach (var c in S.TestSwitches)
        {
            var mine = S.TestButtons.Where(b => ReferenceEquals(b.Control, c)).ToList();
            Assert.Equal(2, mine.Count);
            Assert.Single(mine, b => b.Positive);
        }
        Assert.All(S.TestButtons, b => Assert.Contains(b.Control, S.TestSwitches));
    }

    [Fact]
    public void Each_test_label_says_its_hold()
    {
        Assert.All(S.TestButtons, b => Assert.EndsWith(" 3 s", b.Label, StringComparison.Ordinal));
    }

    [Fact]
    public void Each_test_says_it_is_done()
    {
        // The button's label without its hold, in sentence case: "Takeoff Config Test 3 s" says "Takeoff config test done".
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE", "tr-TR" })
            InCulture(culture, () => Assert.Equal(TestButtonRows.Select(r => r.Done), S.TestButtons.Select(S.DonePhrase)));
    }

    [Theory]
    [InlineData("BBS757_P61_TAKEOFF_CONFIG_TEST", "0 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)", "1 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)")]
    [InlineData("BBS757_P61_LANDING_CONFIG_TEST", "2 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)", "1 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)")]
    [InlineData("BBS757_P61_LEFT_STALL_TEST", "2 (>L:BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW)", "1 (>L:BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW)")]
    [InlineData("BBS757_P61_WING_AI_TEST", "0 (>L:BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW)", "1 (>L:BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW)")]
    public void Test_codes_hold_the_test_position_and_release_to_neutral(string key, string hold, string release)
    {
        var b = TestButton(key);
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal((hold, release), S.Codes(b.Control, b.Positive)));
    }

    [Fact]
    public void A_test_switch_that_stays_off_neutral_is_said()
    {
        var config = TestSwitch("BBS757_P61_CONFIG_TEST");

        Assert.Equal("Takeoff and landing config test switch did not return; the test may keep running", S.DidNotReturnPhrase(config));
        Assert.Equal("Takeoff and landing config test switch returned", S.ReturnedPhrase(config));
        Assert.True(S.AtRest(config, 1));
        Assert.False(S.AtRest(config, 0));
        Assert.False(S.AtRest(config, 2));
        Assert.False(S.AtRest(config, null));
    }

    // ---- The test buttons on the panels -----------------------------------------------------------------

    private const string TestPanel = "P61 panel";
    private const string SquibTestNode = "B757_P6_SQUIBS_BUTTON2";

    private static Bbs757Control P61Control(string node, string label, string kind) => new()
    {
        Node = node, Section = "Other", Panel = TestPanel, Label = label, Kind = kind, StateVar = node,
        Press = $"1 (>L:{node})", Release = kind == Bbs757Kinds.Held ? $"0 (>L:{node})" : null,
        Positions = kind == Bbs757Kinds.Toggle ? new List<Bbs757Position> { new(0, "Off"), new(1, "On") } : new List<Bbs757Position>(),
    };

    private static Bbs757Control ServiceInterphone() => P61Control("B757_VC_P61_SWITCH_1", "Service Interphone", Bbs757Kinds.Toggle);
    private static Bbs757Control SquibTest() => P61Control(SquibTestNode, "Squib Test", Bbs757Kinds.Held);
    private static Bbs757Control GlassGuard() => P61Control("BBS_B757_P61_GLASS_GAURD_1", "P61 Glass Guard 1", Bbs757Kinds.Toggle);

    [Fact]
    public void The_shipped_P61_panel_has_the_fourteen_test_buttons_right_after_the_squib_test()
    {
        var def = new BBS757Definition();
        var panel = def.GetPanelControls()[TestPanel];
        var vars = def.GetVariables();
        int squib = panel.IndexOf(SquibTestNode);

        Assert.True(squib >= 0, "the squib test is on the P61 panel");
        Assert.Equal(TestButtonKeys, panel.Skip(squib + 1).Take(14));
        Assert.Equal(TestButtonRows.Select(r => r.Label), panel.Skip(squib + 1).Take(14).Select(k => vars[k].DisplayName));
        Assert.Equal(14, panel.Count(k => TestButtonKeys.Contains(k)));   // each once
        Assert.Single(def.GetPanelStructure()["Other"], TestPanel);
    }

    [Fact]
    public void An_in_memory_P61_panel_gets_them_after_the_squib_test()
    {
        var def = new BBS757Definition(MapOf(ServiceInterphone(), SquibTest(), GlassGuard())) { NestRunning = () => true };

        Assert.Equal(new[] { "B757_VC_P61_SWITCH_1", SquibTestNode }.Concat(TestButtonKeys).Append("BBS_B757_P61_GLASS_GAURD_1"),
            def.GetPanelControls()[TestPanel]);
        Assert.Equal(new[] { TestPanel }, def.GetPanelStructure()["Other"]);
    }

    [Fact]
    public void Without_the_squib_test_they_go_at_the_panels_end()
    {
        var def = new BBS757Definition(MapOf(ServiceInterphone(), GlassGuard())) { NestRunning = () => true };

        Assert.Equal(new[] { "B757_VC_P61_SWITCH_1", "BBS_B757_P61_GLASS_GAURD_1" }.Concat(TestButtonKeys), def.GetPanelControls()[TestPanel]);
    }

    [Fact]
    public void A_map_without_the_P61_panel_still_gets_the_test_buttons()
    {
        // No Other section at all: it is created with the panel.
        var empty = new BBS757Definition(new Bbs757ControlMap()) { NestRunning = () => true };
        Assert.Equal(TestButtonKeys, empty.GetPanelControls()[TestPanel]);
        Assert.Equal(new[] { TestPanel }, empty.GetPanelStructure()["Other"]);

        // An Other section with other panels: the panel is appended to them.
        var other = new Bbs757Control
        {
            Node = "B757_SOMETHING_ELSE", Section = "Other", Panel = "Something", Label = "Something Else",
            Kind = Bbs757Kinds.Toggle, StateVar = "B757_SOMETHING_ELSE", Positions = { new(0, "Off"), new(1, "On") },
        };
        var withOther = new BBS757Definition(MapOf(other)) { NestRunning = () => true };
        Assert.Equal(new[] { "Something", TestPanel }, withOther.GetPanelStructure()["Other"]);
        Assert.Equal(new[] { "B757_SOMETHING_ELSE" }, withOther.GetPanelControls()["Something"]);   // untouched
        Assert.Equal(TestButtonKeys, withOther.GetPanelControls()[TestPanel]);
    }

    [Fact]
    public void The_test_buttons_add_no_status_row()
    {
        var displays = new BBS757Definition(new Bbs757ControlMap()) { NestRunning = () => true }.GetPanelDisplayVariables();

        Assert.False(displays.ContainsKey(TestPanel));
    }

    // ---- The test buttons' variables ---------------------------------------------------------------------

    private static readonly string[] TestReadKeys =
    {
        "BBS757_GRNDPROX_HYDGEN_P61_TEST_SW", "BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW", "BBS757_AIRDATA_STALL_RIGHT_P61_TEST_SW",
        "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW", "BBS757_EQUIP_COOLING_P61_TEST_SW", "BBS757_YAW_DAMP_P61_TEST_SW",
        "BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW",
    };

    private const string LampTestVar = "OVHD_TEST_LTS";

    [Fact]
    public void Registers_fourteen_test_buttons_that_send_nothing_generic()
    {
        var vars = new BBS757Definition().GetVariables();

        foreach (var b in S.TestButtons)
        {
            var def = vars[b.Key];
            Assert.Equal(b.Label, def.DisplayName);
            Assert.True(def.RenderAsButton);
            Assert.True(def.SuppressRestingButtonState);
            Assert.Equal(SimVarType.Event, def.Type);
            Assert.StartsWith("BBS757.", def.Name, StringComparison.Ordinal);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
            Assert.False(def.IsAnnounced);
        }
        Assert.Equal(22, S.Buttons.Concat(S.TestButtons).Select(b => vars[b.Key].Name).Distinct().Count());
    }

    [Fact]
    public void Registers_the_test_switches_and_the_lamp_test_as_quiet_reads_on_no_panel()
    {
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var controls = def.GetPanelControls().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);
        var displays = def.GetPanelDisplayVariables().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);
        var monitorRows = def.MonitorRows().Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(TestReadKeys, S.TestSwitches.Select(c => c.LVar));
        foreach (string key in TestReadKeys.Append(LampTestVar))
        {
            var read = vars[key];
            Assert.Equal(key, read.Name);
            Assert.Equal(SimVarType.LVar, read.Type);
            Assert.Equal(UpdateFrequency.OnRequest, read.UpdateFrequency);
            Assert.False(read.IsAnnounced);
            Assert.True(read.ExcludeFromMonitorManager);   // no Ctrl+M row that mutes nothing ([VAR-9])
            Assert.DoesNotContain(key, monitorRows);
            Assert.False(read.RenderAsButton);
            Assert.False(read.RenderAsReadOnlyStatus);
            Assert.DoesNotContain(key, controls);
            Assert.DoesNotContain(key, displays);
        }
    }

    [Fact]
    public void The_tests_add_twenty_two_variables_and_the_profile_stays_under_the_ceiling()
    {
        var vars = new BBS757Definition().GetVariables();
        var mine = S.TestButtons.Select(b => b.Key).Concat(TestReadKeys).Append(LampTestVar).ToList();

        Assert.Equal(22, mine.Distinct(StringComparer.Ordinal).Count());
        Assert.All(mine, k => Assert.True(vars.ContainsKey(k), k));
        Assert.True(vars.Count < 900, $"{vars.Count} variables");
    }

    [Fact]
    public void A_test_switch_or_lamp_key_already_present_is_never_shadowed()
    {
        var config = new Bbs757Control
        {
            Node = "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW", Section = "Other", Panel = TestPanel, Label = "Config Switch",
            Kind = Bbs757Kinds.Switch, StateVar = "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW", Positions = { new(0, "T"), new(1, "N"), new(2, "L") },
        };
        var lamps = new Bbs757Control
        {
            Node = LampTestVar, Section = "Overhead", Panel = "Indicator lights and door lock", Label = "Lamps",
            Kind = Bbs757Kinds.Toggle, StateVar = LampTestVar, Positions = { new(0, "Off"), new(1, "On") },
        };
        var vars = new BBS757Definition(MapOf(config, lamps)).GetVariables();

        Assert.Equal("Config Switch", vars["BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW"].DisplayName);
        Assert.Equal("Lamps", vars[LampTestVar].DisplayName);
    }

    // ---- The test press ----------------------------------------------------------------------------------

    private const string ConfigVar = "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW";
    private const string TakeoffConfigHold = "0 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)";
    private const string LandingConfigHold = "2 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)";
    private const string ConfigRelease = "1 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)";
    private const string ConfigDidNotReturn = "Takeoff and landing config test switch did not return; the test may keep running";

    /// <summary>The hold writes of a 3 s test: at 0 ms and every 30 ms after, the last at 2,970 ms.</summary>
    private const int TestHoldWrites = 100;

    /// <summary>A 3 s test's hold as the fake records it: the position at 0 ms, then a 30 ms wait and the position again, up to 3,000 ms.</summary>
    private static List<string> HeldFor3s(string hold)
    {
        var events = new List<string> { Bbs757FakeIo.WriteEvent(hold) };
        for (int i = 1; i < TestHoldWrites; i++)
        {
            events.Add(Bbs757FakeIo.DelayEvent(30));
            events.Add(Bbs757FakeIo.WriteEvent(hold));
        }
        events.Add(Bbs757FakeIo.DelayEvent(30));   // the last gap: the release goes at 3,000 ms
        return events;
    }

    [Fact]
    public async Task A_test_press_holds_its_position_every_30_ms_for_3_s_releases_then_says_done()
    {
        // As measured: the switch reads neutral again whatever was written last (the template's own return).
        var rig = new Rig();
        rig.Sim.Fresh[ConfigVar] = 1;

        await rig.Press("BBS757_P61_TAKEOFF_CONFIG_TEST").WaitAsync(Ceiling);

        Assert.Equal(HeldFor3s(TakeoffConfigHold).Concat(new[]
        {
            Bbs757FakeIo.WriteEvent(ConfigRelease), Bbs757FakeIo.DelayEvent(250),   // released at 3,000 ms, read back at neutral
        }), rig.Sim.Events);
        Assert.Equal(TestHoldWrites, rig.Sim.Writes.Count(w => w == TakeoffConfigHold));
        Assert.Equal(3000, rig.Sim.Delays.Take(TestHoldWrites).Sum());
        // The lights and warnings it raised are the cockpit watcher's to say (plan 13): the press says only that it ended.
        Assert.Equal(new[] { "Takeoff config test done" }, rig.Speech.All);
        Assert.Empty(rig.Sim.Resyncs);
        Assert.Equal(0, rig.Sim.CanceledDelays);
    }

    [Fact]
    public async Task Nothing_is_said_during_the_test()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 50;   // half way through the hold

        var press = rig.Press("BBS757_P61_LANDING_CONFIG_TEST");
        Assert.Equal(Enumerable.Repeat(LandingConfigHold, 51), rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);

        rig.Sim.Release(50);
        await press.WaitAsync(Ceiling);

        Assert.Equal(Enumerable.Repeat(LandingConfigHold, TestHoldWrites).Append(ConfigRelease), rig.Sim.Writes);
        Assert.Equal(new[] { "Landing config test done" }, rig.Speech.All);
    }

    [Fact]
    public async Task Each_test_button_holds_its_own_position_and_says_its_test_done()
    {
        foreach (var row in TestButtonRows)
        {
            var rig = new Rig().FollowingWrites();
            string hold = Bbs757Rpn.SetLVar(row.LVar, row.Value), release = Bbs757Rpn.SetLVar(row.LVar, 1);

            await rig.Press(row.Key).WaitAsync(Ceiling);

            Assert.Equal(Enumerable.Repeat(hold, TestHoldWrites).Append(release), rig.Sim.Writes);
            Assert.Equal(Enumerable.Repeat(30, TestHoldWrites).Append(250), rig.Sim.Delays);
            Assert.Equal(new[] { row.Done }, rig.Speech.All);
        }
    }

    [Fact]
    public async Task A_test_release_that_lands_on_the_second_write_says_done()
    {
        var rig = new Rig();
        rig.Sim.Fresh[ConfigVar] = 0;   // still on the test position after the first release
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == ConfigRelease && ++releases == 2) rig.Sim.Fresh[ConfigVar] = 1;
        };

        await rig.Press("BBS757_P61_TAKEOFF_CONFIG_TEST").WaitAsync(Ceiling);

        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, TestHoldWrites).Concat(new[] { ConfigRelease, ConfigRelease }), rig.Sim.Writes);
        Assert.Equal(Enumerable.Repeat(30, TestHoldWrites).Concat(new[] { 250, 250 }), rig.Sim.Delays);
        Assert.Equal(new[] { "Takeoff config test done" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_test_release_is_verified_and_kept_up_in_the_background_as_the_trims()
    {
        // The switch keeps reading its test position: three releases, the pilot is told (interrupting), the release goes on
        // in the background (its first wait held here), and "done" is never said.
        var rig = new Rig();
        rig.Sim.Fresh[ConfigVar] = 0;
        int firstBackgroundWait = TestHoldWrites + 3;   // the hold's hundred gaps, then the three read-backs
        rig.Sim.Hold = ordinal => ordinal == firstBackgroundWait;

        var press = rig.Press("BBS757_P61_TAKEOFF_CONFIG_TEST");

        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, TestHoldWrites).Concat(Enumerable.Repeat(ConfigRelease, 3)), rig.Sim.Writes);
        Assert.Equal(Enumerable.Repeat(30, TestHoldWrites).Concat(new[] { 250, 250, 250, 2000 }), rig.Sim.Delays);
        Assert.Equal(new[] { ConfigDidNotReturn }, rig.Speech.All);
        Assert.Contains(ConfigDidNotReturn, rig.Speech.Interrupts);
        Assert.False(press.IsCompleted);   // still releasing

        // Meanwhile neither button of the switch starts a hold.
        await rig.Press("BBS757_P61_LANDING_CONFIG_TEST").WaitAsync(Ceiling);
        Assert.Equal(TestHoldWrites + 3, rig.Sim.Writes.Count);

        rig.Sim.Fresh[ConfigVar] = 1;
        rig.Sim.Release(firstBackgroundWait);
        await press.WaitAsync(Ceiling);

        Assert.Equal(TestHoldWrites + 3, rig.Sim.Writes.Count);
        Assert.Equal(new[] { ConfigDidNotReturn, "Takeoff and landing config test switch returned" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_switch_away_mid_test_still_releases_and_says_nothing()
    {
        const string leftStall = "2 (>L:BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW)";
        const string leftRelease = "1 (>L:BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW)";
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 9;   // the tenth gap, after ten writes of the position

        var press = rig.Press("BBS757_P61_LEFT_STALL_TEST");
        Assert.Equal(Enumerable.Repeat(leftStall, 10), rig.Sim.Writes);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(9);
        await press.WaitAsync(Ceiling);

        // The position is not written again into another aircraft's session, but the release goes and is read back.
        Assert.Equal(Enumerable.Repeat(leftStall, 10).Append(leftRelease), rig.Sim.Writes);
        Assert.Equal(Enumerable.Repeat(30, TestHoldWrites).Append(250), rig.Sim.Delays);   // the hold's waits are never canceled
        Assert.Equal(0, rig.Sim.CanceledDelays);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task One_test_holds_per_switch_and_both_its_buttons_share_it()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;   // the takeoff config test's first gap

        var first = rig.Press("BBS757_P61_TAKEOFF_CONFIG_TEST");
        await rig.Press("BBS757_P61_LANDING_CONFIG_TEST").WaitAsync(Ceiling);   // the same switch: ignored, silently
        await rig.Press("BBS757_P61_TAKEOFF_CONFIG_TEST").WaitAsync(Ceiling);
        Assert.Equal(new[] { TakeoffConfigHold }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);

        // Another switch holds on its own.
        await rig.Press("BBS757_P61_GROUND_PROX_TEST").WaitAsync(Ceiling);
        Assert.Equal(new[] { "Ground proximity test done" }, rig.Speech.All);

        rig.Sim.Release(0);
        await first.WaitAsync(Ceiling);
        Assert.Equal(TestHoldWrites, rig.Sim.Writes.Count(w => w == TakeoffConfigHold));
        Assert.Equal(new[] { "Ground proximity test done", "Takeoff config test done" }, rig.Speech.All);

        // Once released, either of its buttons holds again.
        await rig.Press("BBS757_P61_LANDING_CONFIG_TEST").WaitAsync(Ceiling);
        Assert.Equal(Enumerable.Repeat(LandingConfigHold, TestHoldWrites).Append(ConfigRelease), rig.Sim.Writes.TakeLast(TestHoldWrites + 1));
    }

    [Fact]
    public async Task A_test_and_a_trim_hold_at_the_same_time()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;

        var test = rig.Press("BBS757_P61_WING_AI_TEST");
        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.Equal(new[] { "Rudder trim 0.9 units right" }, rig.Speech.All);

        rig.Sim.Release(0);
        await test.WaitAsync(Ceiling);
        Assert.Equal(new[] { "Rudder trim 0.9 units right", "Wing anti-ice test done" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_test_press_that_cannot_land_is_refused_aloud_and_sends_nothing()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.CanWrite = false;

        foreach (var b in S.TestButtons) await rig.Press(b.Key).WaitAsync(Ceiling);

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Equal(TestButtonRows.Select(r => $"{r.Label} unavailable"), rig.Speech.All);
    }

    [Fact]
    public async Task A_test_press_after_a_switch_away_sends_and_says_nothing()
    {
        var rig = new Rig().FollowingWrites();
        rig.Def.OnSwitchedAway();

        await rig.Press("BBS757_P61_PROBE_HEAT_TEST").WaitAsync(Ceiling);
        Assert.True(rig.Click("BBS757_P61_HYD_GEN_TEST"));

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Every_test_button_click_is_claimed_and_runs_the_3_s_hold()
    {
        var rig = new Rig().FollowingWrites();

        foreach (var b in S.TestButtons)
        {
            int before = rig.Sim.Writes.Count;
            Assert.True(rig.Click(b.Key));   // the definition claims it: the generic path sends nothing
            var (hold, release) = S.Codes(b.Control, b.Positive);
            Assert.Equal(Enumerable.Repeat(hold, TestHoldWrites).Append(release), rig.Sim.Writes.Skip(before));
        }
        Assert.Equal(TestButtonRows.Select(r => r.Done), rig.Speech.All);
    }

    [Fact]
    public async Task A_test_read_that_throws_never_escapes_and_the_release_still_goes()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = io.CanWrite, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
            ReadFresh = (_, _) => throw new InvalidOperationException("no link"),
        };
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => failing, NowMs = () => 0 };
        var speech = new SpeechCapture();

        await def.PressSpringAsync("BBS757_P61_TAKEOFF_CONFIG_TEST", new SimConnectManager(IntPtr.Zero), speech).WaitAsync(Ceiling);

        // Never read at neutral: three releases, then one every 2 s for 60 s; never "done".
        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, TestHoldWrites).Concat(Enumerable.Repeat(ConfigRelease, 3 + 30)), sim.Writes);
        Assert.Equal(new[] { ConfigDidNotReturn }, speech.All);
    }

    // ---- The hold ends by real time (fix round 1) ----------------------------------------------------------
    //
    // A real 30 ms wait runs about 40 ms (measured 2026-10-28): counted by its waits alone a "3 s" test held about 4 s. The
    // hold ends by whichever comes first of the waits asked and the definition's clock (NowMs); the fakes' waits take
    // no time, so every sequence above is unchanged.

    [Fact]
    public async Task A_test_whose_waits_run_long_is_released_at_3_s_of_real_time()
    {
        var rig = new Rig().FollowingWrites();
        rig.DelayTakes = ms => ms + 10;   // each 30 ms wait takes 40 ms
        long releasedAt = -1;
        var follow = rig.Sim.OnWrite;
        rig.Sim.OnWrite = rpn =>
        {
            follow?.Invoke(rpn);
            if (rpn == ConfigRelease && releasedAt < 0) releasedAt = rig.Now;
        };

        await rig.Press("BBS757_P61_TAKEOFF_CONFIG_TEST").WaitAsync(Ceiling);

        // Written at 0, 40, ... 2,960 ms of real time: 75 writes; the 75th wait reaches 3,000 ms and the release goes.
        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, 75).Append(ConfigRelease), rig.Sim.Writes);
        Assert.Equal(Enumerable.Repeat(30, 75).Append(250), rig.Sim.Delays);
        Assert.Equal(3000, releasedAt);
        Assert.Equal(new[] { "Takeoff config test done" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_test_hold_never_outlasts_3_s_of_real_time_whatever_the_waits()
    {
        // A wait that runs far long (a busy UI thread): no write once 3 s have passed, and the gap is what is left.
        var rig = new Rig().FollowingWrites();
        rig.DelayTakes = ms => ms == 30 ? 1000 : 0;
        long releasedAt = -1;
        var follow = rig.Sim.OnWrite;
        rig.Sim.OnWrite = rpn =>
        {
            follow?.Invoke(rpn);
            if (rpn == ConfigRelease && releasedAt < 0) releasedAt = rig.Now;
        };

        await rig.Press("BBS757_P61_TAKEOFF_CONFIG_TEST").WaitAsync(Ceiling);

        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, 3).Append(ConfigRelease), rig.Sim.Writes);   // at 0, 1,000 and 2,000 ms
        Assert.Equal(new[] { 30, 30, 30, 250 }, rig.Sim.Delays);
        Assert.Equal(3000, releasedAt);
    }

    [Fact]
    public async Task A_trim_whose_waits_run_long_is_released_at_1_s_of_real_time()
    {
        // The trims follow the same rule: their second wait is what is left of the second, not another 500 ms.
        var rig = new Rig().FollowingWrites();
        rig.DelayTakes = ms => ms == 500 ? 530 : ms;   // the first wait runs 30 ms long
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;
        long releasedAt = -1;
        var follow = rig.Sim.OnWrite;
        rig.Sim.OnWrite = rpn =>
        {
            follow?.Invoke(rpn);
            if (rpn == RudderRelease && releasedAt < 0) releasedAt = rig.Now;
        };

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { 500, 470, 250, 500 }, rig.Sim.Delays);   // 530 ms in, 470 ms are left
        Assert.Equal(1000, releasedAt);
        Assert.Equal(new[] { "Rudder trim 0.9 units right" }, rig.Speech.All);
    }

    // ---- A press that fails part-way (fix round 1) ----------------------------------------------------------

    /// <summary>An announcer whose first <c>Announce</c> throws (a screen reader that failed), recording every phrase it was asked to say.</summary>
    private sealed class FailingFirstSpeech : ScreenReaderAnnouncer
    {
        public FailingFirstSpeech() : base(IntPtr.Zero) { }

        public List<string> Asked { get; } = new();

        public override void Announce(string message)
        {
            Asked.Add(message);
            if (Asked.Count == 1) throw new InvalidOperationException("speech failed");
        }

        public override void AnnounceImmediate(string message) => Asked.Add(message);
    }

    [Fact]
    public async Task A_test_that_ran_says_nothing_more_when_its_press_fails_after_the_writes()
    {
        var sim = new Bbs757FakeIo();
        sim.Fresh[ConfigVar] = 1;
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => sim.Io, NowMs = () => 0 };
        var speech = new FailingFirstSpeech();

        await def.PressSpringAsync("BBS757_P61_TAKEOFF_CONFIG_TEST", new SimConnectManager(IntPtr.Zero), speech).WaitAsync(Ceiling);

        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, TestHoldWrites).Append(ConfigRelease), sim.Writes);   // it ran and was released
        Assert.Equal(new[] { "Takeoff config test done" }, speech.Asked);   // never "… unavailable" after a test that ran
    }

    /// <summary>An announcer whose <c>AnnounceImmediate</c> throws (a screen reader that failed on the interrupting warning), recording every phrase.</summary>
    private sealed class FailingImmediateSpeech : ScreenReaderAnnouncer
    {
        public FailingImmediateSpeech() : base(IntPtr.Zero) { }

        public List<string> Asked { get; } = new();

        public override void Announce(string message) => Asked.Add(message);

        public override void AnnounceImmediate(string message)
        {
            Asked.Add(message);
            throw new InvalidOperationException("speech failed");
        }
    }

    [Fact]
    public async Task A_did_not_return_warning_that_throws_still_leaves_the_background_release_running()
    {
        // Plan 12 task 12 (the final review's recommendation 3): the background release starts before the warning is
        // spoken, so the safety never depends on speech. The knob stays off rest through the press's three releases and
        // comes back on the third background one; the warning's speech throws.
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Hold = ordinal => ordinal == FirstBackgroundWait;
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == RudderRelease && ++releases == 6) rig.Sim.Fresh[RudderKnob] = 1;
        };
        var speech = new FailingImmediateSpeech();

        var press = rig.Def.PressSpringAsync("BBS757_RUDDER_TRIM_RIGHT", rig.Manager, speech);

        Assert.False(press.IsCompleted);   // still releasing in the background
        Assert.Equal(RudderDidNotReturn, speech.Asked[0]);

        rig.Sim.Release(FirstBackgroundWait);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 3)), rig.Sim.Writes);
        Assert.Equal(RudderReturned, speech.Asked[^1]);
        Assert.Equal(0, rig.Sim.CanceledDelays);
    }

    [Fact]
    public async Task A_test_press_that_fails_before_any_write_is_refused_aloud()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = () => throw new InvalidOperationException("no link"),
            ReadFresh = io.ReadFresh, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
        };
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => failing, NowMs = () => 0 };
        var speech = new SpeechCapture();

        await def.PressSpringAsync("BBS757_P61_TAKEOFF_CONFIG_TEST", new SimConnectManager(IntPtr.Zero), speech).WaitAsync(Ceiling);

        Assert.Empty(sim.Writes);
        Assert.Equal(new[] { "Takeoff Config Test 3 s unavailable" }, speech.All);
    }

    // ---- The lamp test (the Lamp Test button, once Warning Test): it latches, so the press reads it back ----

    private const string WarningTestNode = "B757_OVHD_WARNING_TEST_BUTTON";
    private const string LampTestPress =
        "1 (>L:OVHD_TEST_LTS_PRESS, bool) (A:CIRCUIT ON:27, bool) 1 == if{ (L:OVHD_TEST_LTS, bool) ! (>L:OVHD_TEST_LTS, bool) } els{ 0 (>L:OVHD_TEST_LTS, bool) }";
    private const string LampTestRelease = "0 (>L:OVHD_TEST_LTS_PRESS, bool)";

    /// <summary>The map's Lamp Test, as the shipped map has it (a plain button).</summary>
    private static Bbs757Control WarningTest() => new()
    {
        Node = WarningTestNode, Section = "Overhead", Panel = "Indicator lights and door lock", Label = "Lamp Test",
        Kind = Bbs757Kinds.Button, StateVar = "OVHD_TEST_LTS_PRESS", Press = LampTestPress, Release = LampTestRelease,
    };

    [Fact]
    public void The_lamp_test_is_the_maps_lamp_test()
    {
        Assert.Equal(WarningTestNode, Bbs757LampTest.Node);
        Assert.Equal(LampTestVar, Bbs757LampTest.LitVar);
        Assert.Equal(500, Bbs757LampTest.ReadDelayMs);

        var shipped = new BBS757Definition();
        Assert.Equal("Lamp Test", shipped.GetVariables()[WarningTestNode].DisplayName);
        // plan 12 task 7: the lamp test sits with the door lock on the lighting / flight deck door plate
        Assert.Contains(WarningTestNode, shipped.GetPanelControls()["Indicator lights and door lock"]);
    }

    [Theory]
    [InlineData(1.0, "Lamp test on")]
    [InlineData(0.0, "Lamp test off")]
    [InlineData(null, "Lamp Test pressed")]                        // unread: the generic confirmation
    [InlineData(double.NaN, "Lamp Test pressed")]
    [InlineData(double.PositiveInfinity, "Lamp Test pressed")]
    public void Lamp_test_phrase(double? lit, string said)
    {
        Assert.Equal(said, Bbs757LampTest.Phrase(lit, "Lamp Test"));
    }

    [Theory]
    [InlineData(1.0, "Lamp test on")]
    [InlineData(0.0, "Lamp test off")]
    [InlineData(null, "Lamp Test pressed")]
    public void The_lamp_test_press_says_whether_the_lamps_are_on(double? lit, string said)
    {
        var rig = new Rig(WarningTest());
        if (lit is double v) rig.Sim.Fresh[LampTestVar] = v;

        Assert.True(rig.Click(WarningTestNode));   // claimed before the generic map path

        // Pressed as the generic path would (its press, the button's 200 ms hold, its release), read back 500 ms later.
        Assert.Equal(new[]
        {
            Bbs757FakeIo.WriteEvent(LampTestPress), Bbs757FakeIo.DelayEvent(Bbs757Write.ButtonHoldMs),
            Bbs757FakeIo.WriteEvent(LampTestRelease), Bbs757FakeIo.DelayEvent(500),
        }, rig.Sim.Events);
        // Said once: the generic "Lamp Test pressed" is not also said.
        Assert.Equal(new[] { said }, rig.Speech.All);
    }

    [Fact]
    public void A_second_lamp_test_press_turns_it_off()
    {
        // It latches: the press toggles L:OVHD_TEST_LTS (with circuit 27 on), the release leaves it.
        var rig = new Rig(WarningTest());
        rig.Sim.Fresh[LampTestVar] = 0;
        rig.Sim.OnWrite = rpn => { if (rpn == LampTestPress) rig.Sim.Fresh[LampTestVar] = rig.Sim.Fresh[LampTestVar] == 1 ? 0 : 1; };

        Assert.True(rig.Click(WarningTestNode));
        Assert.True(rig.Click(WarningTestNode));

        Assert.Equal(new[] { "Lamp test on", "Lamp test off" }, rig.Speech.All);
        Assert.Equal(new[] { LampTestPress, LampTestRelease, LampTestPress, LampTestRelease }, rig.Sim.Writes);
    }

    [Fact]
    public void A_lamp_test_that_cannot_land_is_refused_aloud_and_sends_nothing()
    {
        var rig = new Rig(WarningTest());
        rig.Sim.CanWrite = false;

        Assert.True(rig.Click(WarningTestNode));

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Equal(new[] { "Lamp Test unavailable" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_switch_away_during_the_lamp_test_press_still_releases_and_says_nothing()
    {
        var rig = new Rig(WarningTest());
        rig.Sim.Fresh[LampTestVar] = 1;
        rig.Sim.Hold = ordinal => ordinal == 0;   // the button's hold

        Assert.True(rig.Click(WarningTestNode));
        Assert.Equal(new[] { LampTestPress }, rig.Sim.Writes);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(0);
        await rig.Sim.WritesReached(2);

        Assert.Equal(new[] { LampTestPress, LampTestRelease }, rig.Sim.Writes);   // a press is always let go
        Assert.Equal(new[] { Bbs757Write.ButtonHoldMs }, rig.Sim.Delays);          // no read-back wait after the switch
        Assert.Equal(0, rig.Sim.CanceledDelays);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_lamp_test_read_that_throws_never_escapes_and_says_pressed()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = io.CanWrite, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
            ReadFresh = (_, _) => throw new InvalidOperationException("no link"),
        };
        var def = new BBS757Definition(MapOf(WarningTest())) { NestRunning = () => true, WriteIo = _ => failing };
        var speech = new SpeechCapture();

        Assert.True(def.HandleUIVariableSet(WarningTestNode, 1, def.GetVariables()[WarningTestNode], new SimConnectManager(IntPtr.Zero), speech));

        Assert.Equal(new[] { LampTestPress, LampTestRelease }, sim.Writes);
        Assert.Equal(new[] { "Lamp Test pressed" }, speech.All);
    }

    [Fact]
    public void Another_button_is_still_pressed_by_the_generic_path()
    {
        var other = new Bbs757Control
        {
            Node = "B757_OVHD_OTHER_TEST_BUTTON", Section = "Overhead", Panel = "Indicator lights and door lock", Label = "Other Test",
            Kind = Bbs757Kinds.Button, StateVar = "B757_OVHD_OTHER_TEST", Press = "1 (>L:B757_OVHD_OTHER_TEST)", Release = "0 (>L:B757_OVHD_OTHER_TEST)",
        };
        var rig = new Rig(WarningTest(), other);

        Assert.True(rig.Click(other.Node));

        Assert.Equal(new[] { other.Press!, other.Release! }, rig.Sim.Writes);
        Assert.Equal(new[] { "Other Test pressed" }, rig.Speech.All);   // read nothing back
    }

    [Fact]
    public void A_warning_test_that_is_not_a_button_is_left_to_the_generic_path()
    {
        // A regenerated map that made it a toggle: picked as a position (settle, fresh read, one press, re-read), never read back as the lamp test.
        var toggle = WarningTest();
        toggle.Kind = Bbs757Kinds.Toggle;
        toggle.Release = null;
        toggle.Positions = new List<Bbs757Position> { new(0, "Off"), new(1, "On") };
        var rig = new Rig(toggle);
        rig.Sim.Fresh[WarningTestNode] = 0;
        rig.Sim.Fresh[LampTestVar] = 1;

        Assert.True(rig.Click(WarningTestNode));

        Assert.Equal(new[] { LampTestPress }, rig.Sim.Writes);
        Assert.Equal(new[] { 300, 1500 }, rig.Sim.Delays);   // the pick's settle and its read-back, not the lamp test's 200 and 500
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_guarded_warning_test_is_left_to_the_generic_path()
    {
        // A guard the map does not list: the generic path refuses aloud and sends nothing; the lamp test would have pressed.
        var guarded = WarningTest();
        guarded.Guard = "B757_OVHD_WARNING_TEST_GUARD";
        var rig = new Rig(guarded);
        rig.Sim.Fresh[LampTestVar] = 1;

        Assert.True(rig.Click(WarningTestNode));

        Assert.Empty(rig.Sim.Writes);
        Assert.Equal(new[] { "Lamp Test unavailable" }, rig.Speech.All);
        Assert.Equal(new[] { WarningTestNode }, rig.Sim.Resyncs);
    }

    [Fact]
    public void A_map_without_the_warning_test_claims_nothing()
    {
        var rig = new Rig();
        rig.Sim.Fresh[LampTestVar] = 1;

        Assert.False(rig.Def.HandleUIVariableSet(WarningTestNode, 1, new SimVarDefinition(), rig.Manager, rig.Speech));

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Empty(rig.Speech.All);
    }
}
