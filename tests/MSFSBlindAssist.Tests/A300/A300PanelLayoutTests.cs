using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The shipped map and its placement. Fleet-wide rules (no two rows of a panel share a name, labels
/// fit, no batch name collisions) are pinned by the fleet tests the A300 is listed in.
/// </summary>
public class A300PanelLayoutTests
{
    private static readonly A300ControlMap Map = A300ControlMap.Load();
    private static readonly A300Placement Placement = A300PanelLayout.Place(Map);

    private static IEnumerable<A300PlacedRow> AllRows => Placement.RowsByPanel.Values.SelectMany(r => r);

    private static A300PlacedRow Row(string key) => AllRows.Single(r => r.Key == key);

    [Fact]
    public void The_embedded_map_loads_with_every_control()
    {
        Assert.Equal(614, Map.Controls.Count);
        Assert.False(string.IsNullOrEmpty(Map.PackageVersion));
    }

    [Fact]
    public void Every_drivable_control_names_its_set_event_and_a_unique_key()
    {
        foreach (var c in Map.Controls.Where(A300PanelLayout.IsPlaced))
            Assert.EndsWith("_Set", c.Event);
        Assert.Equal(Map.Controls.Count, Map.Controls.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void The_sections_are_the_fleet_airbuses_sections()
    {
        // Design decision 2026-10-09: "Main Panel" is "Instrument", as on the FBW A320, A330 and A380.
        Assert.Equal(new[] { "Overhead", "Glareshield", "Instrument", "Pedestal", "Cockpit", "Cargo" },
            Placement.Structure.Keys);
    }

    [Fact]
    public void Each_section_lists_its_panels_by_system_in_the_fleet_airbuses_order()
    {
        // Design decisions 2026-10-09 (docs/a300.md, "Panels"): sorted by system like the FBW A320 where the A300
        // has the same panel; A300-only panels keep the manual's name; window and probe heat and the landing
        // elevation knob stay where the A300 has them.
        Assert.Equal(new[]
        {
            "Electrical", "IRS", "APU", "Oxygen", "Fire", "Hydraulics", "Fuel", "Air Conditioning", "Bleed",
            "Pressurization", "Ventilation", "Anti-Ice", "Window and Probe Heat", "Wipers", "Signs", "Interior Lighting",
            "Exterior Lighting", "Flight Controls", "SAS Control", "Cockpit Door", "Cargo Smoke", "Recorder",
            "Engine Start",
        }, Placement.Structure["Overhead"]);
        Assert.Equal(new[] { "FCU", "EFIS Captain", "EFIS First Officer" }, Placement.Structure["Glareshield"]);
        Assert.Equal(new[]
        {
            "Warnings", "Gear", "Autobrake", "Thrust Rating Panel", "Landing Elevation", "Standby Instruments",
            "Source Switching", "Clock", "GPWS", "Captain Side", "First Officer Side",
        }, Placement.Structure["Instrument"]);
        Assert.Equal(new[]
        {
            "Engines", "Thrust Levers", "Flaps and Speed Brake", "Parking Brake", "Trim", "ECAM Control Panel",
            "Weather Radar", "Transponder", "VHF Radios", "Navigation Radios", "ADF Radios",
            "Audio Control Panel Captain", "Audio Control Panel First Officer", "MCDU Brightness", "IDC",
            "Pedestal Lighting",
        }, Placement.Structure["Pedestal"]);
        Assert.Equal(new[] { "Yokes", "RAT", "Circuit Breakers", "Cockpit" }, Placement.Structure["Cockpit"]);
        Assert.Equal(new[] { "Cargo Door" }, Placement.Structure["Cargo"]);
    }

    [Theory]
    [InlineData("A300_SEATBELT", "Signs")]
    [InlineData("A300_EMERGEXIT_SWITCH", "Signs")]
    [InlineData("A300_NOSELIGHTSWITCH", "Exterior Lighting")]
    [InlineData("A300_NAVLIGHT_SWITCH", "Exterior Lighting")]
    [InlineData("A300_STORMLIGHT", "Interior Lighting")]
    [InlineData("A300_ANN_LIGHTSWITCH", "Interior Lighting")]
    [InlineData("A300_COMPASS_COVER", "Interior Lighting")]
    [InlineData("A300_APU_BLEEDSWITCH", "Bleed")]
    [InlineData("A300_AIR_XFEED", "Bleed")]
    [InlineData("A300_ISO_VALVE_LEFT", "Bleed")]
    [InlineData("A300_PACK1_MODE", "Air Conditioning")]
    [InlineData("A300_TEMP_SELECT", "Air Conditioning")]
    [InlineData("A300_SMOKE_TEST", "Cargo Smoke")]
    [InlineData("A300_CARGO_AFT_SMOKE_TOGGLE", "Cargo Smoke")]
    [InlineData("A300_FIRE_HANDLE_ENG1", "Fire")]
    [InlineData("A300_AUTO_PRESS_1", "Pressurization")]
    [InlineData("A300_FLTRCDR_GNDCTL", "Recorder")]
    [InlineData("A300_CVR_TEST", "Recorder")]
    [InlineData("A300_ATC_CPT", "EFIS Captain")]
    [InlineData("A300_YAW_DAMPER_1", "SAS Control")]
    [InlineData("A300_ATS_2", "SAS Control")]
    [InlineData("A300_MASTER_WARNING_CPT", "Warnings")]
    [InlineData("A300_MASTER_CAUTION_FO", "Warnings")]
    [InlineData("A300_GEAR_LEVER", "Gear")]
    [InlineData("A300_MAN_GEAR_HANDLE_EXT", "Gear")]
    [InlineData("A300_LDG_TEST", "Gear")]
    [InlineData("A300_AUTO_BRK_LO", "Autobrake")]
    [InlineData("A300_ANTI_SKID", "Autobrake")]
    [InlineData("A300_BRAKE_FAN", "Autobrake")]
    [InlineData("A300_TRP_CL", "Thrust Rating Panel")]
    [InlineData("A300_FLEX_TEMP#INC", "Thrust Rating Panel")]
    [InlineData("A300_LANDING_ELEV_SET#INC", "Landing Elevation")]
    [InlineData("A300_ADI_CAGE", "Standby Instruments")]
    [InlineData("A300_FO_SW_FD", "Source Switching")]
    [InlineData("A300_CPT_PFD_XFR", "Source Switching")]
    [InlineData("A300_CPT_CLOCK_START", "Clock")]
    [InlineData("A300_GPWS_FO", "GPWS")]
    [InlineData("A300_GPWS_FLAPS_CONFIG", "GPWS")]
    [InlineData("A300_CPT_ALTIMETER_KNOB#INC", "Captain Side")]
    [InlineData("A300_FO_OXY_PUSH", "First Officer Side")]
    [InlineData("A300_ENG1_CUTOFF", "Engines")]
    [InlineData("A300_TOGA_SEL", "Thrust Levers")]
    [InlineData("A300_AT_DISCO1", "Thrust Levers")]
    [InlineData(A300Levers.FlapsKey, "Flaps and Speed Brake")]
    [InlineData(A300Levers.SpeedBrakeKey, "Flaps and Speed Brake")]
    [InlineData("A300_PARKINGBRAKE", "Parking Brake")]
    [InlineData("A300_PARKBRAKE_PRESS_PUSH", "Parking Brake")]
    [InlineData("A300_TO_CONFIG_TEST", "ECAM Control Panel")]
    [InlineData("A300_EMER_CANCEL_BUTTON", "ECAM Control Panel")]
    [InlineData("A300_ECAM_CLR", "ECAM Control Panel")]
    [InlineData("A300_SGU_1", "ECAM Control Panel")]
    [InlineData("A300_TCAS_MODE", "Transponder")]
    [InlineData("A300_CPT_VHF1_VOL_BUTTON", "Audio Control Panel Captain")]
    [InlineData("A300_FO_INT_VOL", "Audio Control Panel First Officer")]
    public void Each_control_sits_on_its_systems_panel(string key, string panel) =>
        Assert.Contains(Row(key), Placement.RowsByPanel[panel]);

    [Theory]
    [InlineData("INI_PACK1_FAULT", "Air Conditioning")]
    [InlineData("INI_ISOLATION_VALVE_LEFT_FAULT", "Bleed")]
    [InlineData("INI_cabin_sys1_regulator_fault", "Pressurization")]
    [InlineData("INI_GPWS_LIGHT", "GPWS")]
    [InlineData("INI_GLIDESLOPE_LIGHT", "GPWS")]
    [InlineData("INI_TERR_MODE_FAULT", "GPWS")]
    [InlineData("INI_FMS1_message_light", "Captain Side")]
    [InlineData("INI_engine1_oil_low_press_light", "Engines")]
    [InlineData("INI_ENG2_MASTER_SWITCH_LIGHT", "Engines")]
    [InlineData("INI_AUTOLAND_LIGHT", "EFIS Captain")]
    [InlineData("INI_ECAM_CLR_LIGHT", "ECAM Control Panel")]
    [InlineData("INI_ENG1_LOOP_A_LIGHT", "Fire")]
    public void Each_light_sits_on_its_systems_panel(string var, string panel) =>
        Assert.Equal(panel, A300FaultLights.All.Single(l => l.Var == var).Panel);

    [Fact]
    public void The_master_lights_sit_on_warnings() =>
        Assert.All(A300Announcements.Lamps.Where(l => !l.SpeaksOff), l => Assert.Equal("Warnings", l.Panel));

    [Theory]
    [InlineData(A300Readouts.FlexTemperatureKey, "Thrust Rating Panel")]
    [InlineData("A300_RO_LANDING_ELEV", "Landing Elevation")]
    [InlineData(A300Readouts.BaroStandbyKey, "Standby Instruments")]
    [InlineData(A300Readouts.BaroCaptainKey, "Captain Side")]
    [InlineData(A300Readouts.BaroFirstOfficerKey, "First Officer Side")]
    public void Each_readout_sits_on_its_systems_panel(string key, string panel) =>
        Assert.Equal(panel, A300Readouts.All.Single(r => r.Key == key).Panel);

    [Fact]
    public void Every_light_readout_and_typed_box_names_a_panel_that_exists()
    {
        var panels = Placement.Structure.Values.SelectMany(p => p).ToHashSet();
        Assert.All(A300FaultLights.All, l => Assert.Contains(l.Panel, panels));
        Assert.All(A300Announcements.Lamps, l => Assert.Contains(l.Panel, panels));
        Assert.All(A300Readouts.All.Where(r => !A300DisplayPanels.IsDisplayPanel(r.Panel)), r => Assert.Contains(r.Panel, panels));
        Assert.All(A300TypedValues.All, t => Assert.Contains(t.Panel, panels));
        Assert.Contains(A300Trp.Panel, panels);
    }

    [Theory]
    [InlineData(A300TypedValues.BaroCaptainKey, "Captain Side")]
    [InlineData(A300TypedValues.BaroFirstOfficerKey, "First Officer Side")]
    [InlineData(A300TypedValues.BaroStandbyKey, "Standby Instruments")]
    [InlineData(A300TypedValues.MinimumsKey, "EFIS Captain")]
    [InlineData(A300TypedValues.SquawkKey, "Transponder")]
    public void Each_typed_box_sits_on_its_systems_panel(string key, string panel) =>
        Assert.Contains(Row(key), Placement.RowsByPanel[panel]);

    [Theory]
    [InlineData("A300_MASTER_WARNING_CPT", "Captain master warning")]
    [InlineData("A300_MASTER_CAUTION_FO", "First officer master caution")]
    [InlineData("A300_CPT_CLOCK_START", "Captain chrono")]
    [InlineData("A300_FO_CLOCK_RUN", "First officer elapsed time")]
    [InlineData("A300_FO_SW_ATT", "First officer attitude and heading to IRS 3")]
    [InlineData("A300_CPT_PFD_XFR", "Captain PFD and ND transfer")]
    [InlineData("A300_GPWS_FO", "First officer GPWS test")]
    [InlineData("A300_TERR_CPT", "Captain terrain on ND")]
    [InlineData("A300_FO_TERR_MODE", "First officer terrain mode")]
    public void A_row_in_a_panel_both_sides_share_names_its_side_first(string key, string name) =>
        Assert.Equal(name, Row(key).Name);

    [Fact]
    public void Every_section_lists_exactly_the_panels_it_has()
    {
        foreach (var (section, panels) in Placement.Structure)
            Assert.Equal(A300PanelLayout.PanelOrder[section], panels);
    }

    [Fact]
    public void A_panel_the_order_does_not_name_comes_after_the_listed_ones()
    {
        Assert.Equal(new[] { "A", "B", "X" }, A300PanelLayout.Ordered(new[] { "B", "X", "A" }, new[] { "A", "B" }));
        Assert.Equal(new[] { "A" }, A300PanelLayout.Ordered(new[] { "A" }, new[] { "Z", "A" }));
    }

    [Fact]
    public void Every_panel_in_the_structure_has_rows()
    {
        foreach (var panel in Placement.Structure.Values.SelectMany(p => p))
            Assert.NotEmpty(Placement.RowsByPanel[panel]);
    }

    [Theory]
    [InlineData("A300_BATT_1", "Battery 1")]
    [InlineData("A300_GEN_2", "Engine 2 generator")]
    [InlineData("A300_NAVATTLEFT", "IRS 1 mode")]
    [InlineData("A300_FO_ALTIMETER_KNOB#INC", "Altimeter knob increase")]
    [InlineData("A300_VS_KNOB#DEC", "Vertical speed knob decrease")]
    [InlineData("A300_CPT_VHF1_VOL_BUTTON", "VHF 1 receiver")]
    [InlineData("A300_FO_INT_VOL", "Interphone volume")]
    [InlineData("A300_CPT_HF2_PUSH", "HF 2 transmit")]
    [InlineData("A300_STORMLIGHT", "Storm light")]
    [InlineData("A300_EXT_PWR", "External power")]
    public void Rows_carry_the_names_a_pilot_says(string key, string name) => Assert.Equal(name, Row(key).Name);

    [Fact]
    public void The_third_oxygen_button_is_named_as_the_aircraft_titles_it()
    {
        // Its node is PAX_OXY_SUPPLY, but iniBuilds' tooltip titles it "CREW OXYGEN SUPPLY" (the manual's
        // "Crew Oxygen" in Securing Aircraft); it toggles the courier supply's own variable.
        Assert.Equal("Crew oxygen supply", Row("A300_PAX_OXY_SUPPLY").Name);
        Assert.Contains(Row("A300_PAX_OXY_SUPPLY"), Placement.RowsByPanel["Oxygen"]);
    }

    [Fact]
    public void The_first_officers_altimeter_is_not_named_after_the_captains()
    {
        // iniBuilds titles both altimeter knobs "CPT BAROMETER".
        Assert.Contains(Row("A300_FO_ALTIMETER_KNOB#INC"), Placement.RowsByPanel["First Officer Side"]);
        Assert.DoesNotContain(AllRows, r => r.Name.Contains("CPT", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cargo_smoke_guard_comes_just_before_its_switch_and_reads_closed_or_open()
    {
        var fire = Placement.RowsByPanel["Cargo Smoke"].Select(r => r.Key).ToList();
        Assert.Equal(fire.IndexOf("A300_CARGO_FWD_SMOKE_TOGGLE_GUARD") + 1, fire.IndexOf("A300_CARGO_FWD_SMOKE_TOGGLE"));
        Assert.Equal(fire.IndexOf("A300_CARGO_AFT_SMOKE_TOGGLE_GUARD") + 1, fire.IndexOf("A300_CARGO_AFT_SMOKE_TOGGLE"));
        Assert.Equal(new[] { "Closed", "Open" }, Row("A300_CARGO_FWD_SMOKE_TOGGLE_GUARD").Positions.Values);
    }

    [Theory]
    [InlineData("A300_FIRE_1_AG1")]
    [InlineData("A300_FIRE_1_AG2")]
    [InlineData("A300_FIRE_2_AG1")]
    [InlineData("A300_FIRE_2_AG2")]
    [InlineData("A300_FIRE_APU_AG")]
    public void An_agent_button_reads_discharged_as_its_disch_legend_lights(string key) =>
        // Its state is the agent's discharge flag, which lights the button's DISCH legend (Fenix: "Agent 1 Discharge").
        Assert.Equal(new[] { "Off", "Discharged" }, Row(key).Positions.Values);

    [Theory]
    [InlineData("A300_PRESS_SYS_1", "Pressurization system 1")]
    [InlineData("A300_PRESS_SYS_2", "Pressurization system 2")]
    public void A_pressurization_system_is_a_button(string key, string name)
    {
        Assert.Equal((name, A300RowAction.Press), (Row(key).Name, Row(key).Action));
        Assert.Equal(new[] { $"1 (>B:AIRLINER_{key.Substring(5)}_Set)" },
            A300WritePlan.ForPress(Row(key).Control!).Steps.Cast<A300CalcStep>().Select(s => s.Rpn));
    }

    [Theory]
    [InlineData("A300_STOP_CAPT", "EFIS Captain")]
    [InlineData("A300_STOP_FO", "EFIS First Officer")]
    public void A_stop_rudder_input_test_is_a_button_labelled_by_its_light(string key, string panel)
    {
        // Its switch resets itself about 6 s after a press (measured 2026-10-10): a push button in the cockpit.
        Assert.Equal(("Stop rudder input", A300RowAction.Press), (Row(key).Name, Row(key).Action));
        Assert.Contains(Row(key), Placement.RowsByPanel[panel]);
        Assert.Equal(new[] { $"1 (>B:{Row(key).Control!.Event})" },
            A300WritePlan.ForPress(Row(key).Control!).Steps.Cast<A300CalcStep>().Select(s => s.Rpn));
        Assert.True(A300PanelLamps.ByButton.ContainsKey(key));
    }

    [Fact]
    public void The_gravity_extension_handle_is_stowed_by_its_stow_clickspot()
    {
        // Its own event only pulls it (INI_GRAVITY_HANDLE_CMD): picking Stowed "stayed Extended" (2026-10-10).
        // iniBuilds stows it with a separate clickspot, which clears the handle (INI_GRAVITY_HANDLE_ANIM 0).
        var c = Row("A300_MAN_GEAR_HANDLE_EXT").Control!;
        Assert.Equal(new[] { "1 (>B:AIRLINER_Man_Gear_Handle_Hide_Set)" },
            A300WritePlan.ForSet(c, 0, 1).Steps.Cast<A300CalcStep>().Select(s => s.Rpn));
        Assert.Equal(new[] { "1 (>B:AIRLINER_Man_Gear_Handle_Ext_Set)" },
            A300WritePlan.ForSet(c, 1, 0).Steps.Cast<A300CalcStep>().Select(s => s.Rpn));
    }

    [Fact]
    public void Position_words_are_spoken_forms()
    {
        Assert.Equal(new[] { "Off", "Navigate", "Attitude" }, Row("A300_NAVATTLEFT").Positions.Values);
        Assert.Equal(new[] { "System 2", "System 1", "Off" }, Row("A300_NAVLIGHT_SWITCH").Positions.Values);
        Assert.Equal(new[] { "Off", "On" }, Row("A300_BATT_1").Positions.Values);
        Assert.Equal(new[] { "Released", "Set" }, Row("A300_PARKINGBRAKE").Positions.Values);
    }

    [Theory]
    [InlineData("A300_PACK1_MODE")]
    [InlineData("A300_PACK2_MODE")]
    public void Pack_mode_reads_auto_or_manual(string key)
    {
        // iniBuilds' tooltip says OFF and ON, but measured on the ground (2026-10-04) 0 is automatic (the
        // pack ignores its manual temperature switch) and 1 manual (the pack follows it).
        Assert.Equal(new[] { "Auto", "Manual" }, Row(key).Positions.Values);
    }

    [Theory]
    [InlineData("A300_CPT_YOKE_TRIM")]
    [InlineData("A300_FO_YOKE_TRIM")]
    public void A_yoke_pitch_trim_switch_names_the_way_the_aircraft_trims(string key)
    {
        // Switch 0 trims nose UP: measured 2026-10-06 (Set 0 took ELEVATOR TRIM POSITION from 0 to
        // +0.55), and AP::Update calls A300_elev_trim_up_handler for 0 and _down_handler for 2 on
        // both yokes. iniBuilds' tooltip words have the two the other way round.
        var words = Row(key).Positions;
        Assert.Equal("Up", words[0]);
        Assert.Equal("Neutral", words[1]);
        Assert.Equal("Down", words[2]);
    }

    [Fact]
    public void Each_lead_list_opens_its_panel()
    {
        foreach (var (panel, keys) in A300PanelLayout.LeadRows)
            Assert.Equal(keys, Placement.RowsByPanel[panel].Take(keys.Length).Select(r => r.Key));
    }

    [Fact]
    public void After_its_lead_rows_each_panel_follows_the_aircrafts_checklist()
    {
        // Design decision, 2026-10-10: a panel's rows in the order the A300's own checklist sets them (each control's
        // first checklist step, from the generated map), then the rows it never names, in their own order.
        Assert.Contains(Map.Controls, c => c.Checklist != null);
        foreach (var (panel, rows) in Placement.RowsByPanel)
        {
            int lead = A300PanelLayout.LeadRows.TryGetValue(panel, out var keys) ? keys.Length : 0;
            var steps = rows.Skip(lead).Select(r => A300PanelLayout.ChecklistStep(rows, r)).ToList();
            int firstUnnamed = steps.FindIndex(s => s == null);
            if (firstUnnamed >= 0)
                Assert.True(steps.Skip(firstUnnamed).All(s => s == null), $"{panel}: a checklist row after an unnamed one");
            var named = steps.TakeWhile(s => s != null).Select(s => s!.Value).ToList();
            Assert.True(named.SequenceEqual(named.OrderBy(s => s)), $"{panel}: rows out of checklist order");
        }
    }

    [Fact]
    public void A_row_inside_a_group_one_checklist_step_names_stays_in_the_group()
    {
        // The checklist sets both ADFs in one step but never names their transfer buttons: each stays with its radio.
        var adf = Placement.RowsByPanel["ADF Radios"].Select(r => r.Name).ToList();
        Assert.Equal(adf.IndexOf("ADF 1 standby units decrease") + 1, adf.IndexOf("ADF 1 transfer"));
        Assert.Equal(adf.IndexOf("ADF 2 standby units decrease") + 1, adf.IndexOf("ADF 2 transfer"));
    }

    [Fact]
    public void The_signs_follow_the_checklist()
    {
        Assert.Equal(new[] { "No smoking signs", "Seat belt signs", "Emergency exit lights" },
            Placement.RowsByPanel["Signs"].Take(3).Select(r => r.Name));
    }

    [Fact]
    public void Electrical_opens_on_the_batteries_and_the_levers_open_their_panels()
    {
        Assert.Equal(new[] { "A300_BATT_1", "A300_BATT_2", "A300_BATT_3", "A300_EXT_PWR" },
            Placement.RowsByPanel["Electrical"].Take(4).Select(r => r.Key));
        Assert.Equal(new[] { "A300_ENG1_CUTOFF", "A300_ENG2_CUTOFF" }, Placement.RowsByPanel["Engines"].Select(r => r.Key));
        Assert.Equal(new[] { A300Levers.FlapsKey, A300Levers.SpeedBrakeKey, A300Levers.SpoilersArmKey },
            Placement.RowsByPanel[A300Levers.Panel].Select(r => r.Key));
        Assert.Equal("Flaps and Speed Brake", A300Levers.Panel);
        Assert.Equal(new[] { "A300_GEAR_LEVER" }, Placement.RowsByPanel["Gear"].Take(1).Select(r => r.Key));
        Assert.Equal(new[] { "A300_MASTER_WARNING_CPT", "A300_MASTER_CAUTION_CPT", "A300_MASTER_WARNING_FO", "A300_MASTER_CAUTION_FO" },
            Placement.RowsByPanel["Warnings"].Select(r => r.Key));
    }

    [Fact]
    public void No_row_is_lost_or_repeated()
    {
        var keys = AllRows.Select(r => r.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var c in Map.Controls.Where(A300PanelLayout.IsPlaced))
            Assert.Contains(keys, k => k == c.Key || k == c.Key + "#INC");
    }

    [Theory]
    [InlineData("AIRLINER_CPT_VHF1_IDC")]
    [InlineData("AIRLINER_TABLET_BRT_UP")]
    [InlineData("AIRLINER_SEAT_CPT_FWD")]
    [InlineData("AIRLINER_AT_DISCO2")]
    public void Copies_and_cosmetic_parts_are_not_placed(string id)
    {
        if (Map.Find(id) is { } control)
            Assert.False(A300PanelLayout.IsPlaced(control));
        Assert.DoesNotContain(AllRows, r => r.Control?.Id == id);
    }

    [Fact]
    public void Guard_covers_are_never_rows()
    {
        Assert.DoesNotContain(AllRows, r => r.Control?.Kind == A300Kinds.Cover);
    }

    [Theory]
    [InlineData("STORM LIGHT", "Storm light")]
    [InlineData("TA/RA", "TA/RA")]
    [InlineData("ENG1 LOOP A", "ENG1 loop A")]
    [InlineData("APU MASTER", "APU master")]
    [InlineData("WINDOW HEATER WSLO&LAT L", "Window heater wslo&lat L")]
    [InlineData("", "")]
    public void Spoken_words_keep_letters_codes_and_numbers(string word, string spoken) =>
        Assert.Equal(spoken, A300PanelLayout.SpokenWord(word));

    [Theory]
    [InlineData("AIRLINER_CPT_VOR2_VOL_BUTTON", "VOR 2 receiver")]
    [InlineData("AIRLINER_FO_MKR_VOL", "Marker volume")]
    [InlineData("AIRLINER_CPT_PA_PUSH", "PA transmit")]
    [InlineData("AIRLINER_CPT_VHF1_MHZ", null)]
    public void The_audio_panels_follow_one_rule(string id, string? name) =>
        Assert.Equal(name, A300PanelLayout.AudioName(id));
}
