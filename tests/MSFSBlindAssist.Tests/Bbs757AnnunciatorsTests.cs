using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's announced lights and states (plan 06, <see cref="Bbs757Annunciators"/>): what each row
/// registers, which status display shows it, what a change says, and the EXT PWR button's read-back,
/// which says the light's state instead of "pressed".
/// </summary>
public class Bbs757AnnunciatorsTests
{
    private const string OnKey = Bbs757SharedKeys.ExtPwrOn;
    private const string AvailKey = Bbs757SharedKeys.ExtPwrAvail;

    /// <summary>A definition whose BBS Nest check is settled and whose clock is a field.</summary>
    private sealed class Rig
    {
        public long Now { get; set; } = 100_000;
        public readonly BBS757Definition Def;
        public readonly SpeechCapture Speech = new();

        public Rig(Bbs757ControlMap? map = null)
            => Def = map == null
                ? new BBS757Definition { NestRunning = () => true, NowMs = () => Now, ActiveBatches = () => OneBatch }
                : new BBS757Definition(map) { NestRunning = () => true, NowMs = () => Now, ActiveBatches = () => OneBatch };

        private static readonly int[] OneBatch = { 1 };

        public bool Deliver(string key, double value) => Def.ProcessSimVarUpdate(key, value, Speech);

        /// <summary>One delivery of the only batch, a second later.</summary>
        public void Batch()
        {
            Now += 1000;
            Def.OnContinuousBatchDelivered(1);
        }

        /// <summary>The batch deliveries that end a flight load's quiet once one of BlueBird's own values has changed.</summary>
        public void QuietBatches()
        {
            for (int i = 0; i <= Aircraft.MD11.Md11SeedGate.QuietCycles; i++) Batch();
        }
    }

    /// <summary>A map of one control on <paramref name="panel"/>, so no test depends on the generated map's names.</summary>
    private static Bbs757ControlMap MapWithPanel(string panel) => Bbs757ControlMap.Parse($$"""
        {
          "controls": [
            {
              "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "{{panel}}",
              "label": "Battery", "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON"
            }
          ]
        }
        """);

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

    // ---- Task 1: EXT PWR ----------------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, "EXT PWR on")]
    [InlineData(0.0, "EXT PWR off")]
    [InlineData(0.51, "EXT PWR on")]
    [InlineData(0.5, "EXT PWR off")]
    [InlineData(null, "EXT PWR unavailable")]
    [InlineData(double.NaN, "EXT PWR unavailable")]
    public void Ext_pwr_press_reads_back(double? value, string said)
    {
        // The press reads the ON light back instead of saying "EXT PWR pressed".
        Assert.Equal(OnKey, BBS757Definition.LinkedStatusKey(Bbs757Annunciators.ExtPwrButtonNode));
        Assert.Equal("B757_OVHD_EXT_PWR_BUTTON", Bbs757Annunciators.ExtPwrButtonNode);
        Assert.Equal(said, BBS757Definition.LinkedPhrase(OnKey, value));

        // The read-back rebaselines the row with this phrase, so the row's own words must be the same, or the
        // next delivery would announce the press a second time.
        if (value is double v && double.IsFinite(v))
            Assert.Equal(said, Bbs757Annunciators.ForKey(OnKey)!.Phrase(v, null));
    }

    [Fact]
    public void Ext_pwr_rows_registered_as_announced_status_rows()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var vars = def.GetVariables();

        foreach (var (key, lvar, label) in new[]
        {
            (OnKey, "B757_OVHD_EXT_PWR_BUTTON_ON", "External Power On"),
            (AvailKey, "B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL", "External Power Available"),
        })
        {
            var row = vars[key];
            Assert.Equal(lvar, row.Name);
            Assert.Equal(label, row.DisplayName);
            Assert.Equal(SimVarType.LVar, row.Type);
            Assert.Equal("number", row.Units);
            Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
            Assert.True(row.IsAnnounced);
            Assert.True(row.RenderAsReadOnlyStatus);
            Assert.False(row.ExcludeFromMonitorManager);
            Assert.Contains(def.MonitorRows(), r => r.Key == key && r.Label == label);
            Assert.DoesNotContain(key, def.GetPanelControls().SelectMany(p => p.Value));
        }

        Assert.True(def.TryGetDisplayOverride(OnKey, 1, out string on));
        Assert.Equal("On", on);
        Assert.True(def.TryGetDisplayOverride(OnKey, 0, out string off));
        Assert.Equal("Off", off);
        Assert.True(def.TryGetDisplayOverride(AvailKey, 1, out string avail));
        Assert.Equal("Available", avail);
        Assert.True(def.TryGetDisplayOverride(AvailKey, 0, out string notAvail));
        Assert.Equal("Not available", notAvail);
        Assert.True(def.TryGetDisplayOverride(AvailKey, double.NaN, out string unread));
        Assert.Equal("unavailable", unread);
    }

    [Theory]
    [InlineData("Electrical", true)]
    [InlineData("Pedestal", false)]
    public void Ext_pwr_rows_on_electrical(string panel, bool joins)
    {
        var def = new Rig(MapWithPanel(panel)).Def;
        var displays = def.GetPanelDisplayVariables();

        if (joins)
        {
            var rows = displays[Bbs757Annunciators.ElectricalPanel];
            Assert.Equal(new[] { OnKey, AvailKey }, rows.Where(r => r is OnKey or AvailKey));
            Assert.Single(displays.SelectMany(p => p.Value), OnKey);
            Assert.Single(displays.SelectMany(p => p.Value), AvailKey);
            // Plan 07's ground power row follows: the button's lights, then the cart.
            Assert.True(rows.IndexOf(AvailKey) < rows.IndexOf(Bbs757SharedKeys.GpuConnected));
        }
        else
        {
            Assert.DoesNotContain(OnKey, displays.SelectMany(p => p.Value));
            Assert.DoesNotContain(AvailKey, displays.SelectMany(p => p.Value));
        }
    }

    [Theory]
    [InlineData(OnKey, 0.0, 1.0, "EXT PWR on")]
    [InlineData(OnKey, 1.0, 0.0, "EXT PWR off")]
    [InlineData(AvailKey, 0.0, 1.0, "External power available")]
    [InlineData(AvailKey, 1.0, 0.0, "External power not available")]
    public void Ext_pwr_change_announced_once(string key, double first, double then, string said)
    {
        var rig = new Rig();

        Assert.True(rig.Deliver(key, first));   // the cockpit as found: a silent baseline
        rig.Deliver(key, first);
        rig.Deliver(key, double.NaN);           // unreadable: neither spoken nor a baseline
        Assert.Empty(rig.Speech.All);

        Assert.True(rig.Deliver(key, then));
        rig.Deliver(key, then);
        Assert.Equal(new[] { said }, rig.Speech.All);

        rig.Def.OnSimContextReset();            // a flight load: the next reading is a baseline again
        rig.Deliver(key, first);
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public async Task An_ext_pwr_press_is_heard_once_even_if_avail_changes_with_it()
    {
        // Not measured: BlueBird may put the AVAIL light out when ON lights. The press says "EXT PWR on" itself
        // (CORE-7); an AVAIL change inside its echo must not be said beside it, nor sound like power lost.
        var map = Bbs757ControlMap.Parse("""
            {
              "controls": [
                {
                  "node": "B757_OVHD_EXT_PWR_BUTTON", "section": "Overhead", "panel": "Electrical", "label": "EXT PWR",
                  "kind": "button", "state_var": null,
                  "press": "1 (>L:B757_OVHD_EXT_PWR_BUTTON, bool)", "release": "0 (>L:B757_OVHD_EXT_PWR_BUTTON, bool)"
                }
              ]
            }
            """);
        var io = new Bbs757FakeIo { Hold = _ => true };
        io.Fresh[OnKey] = 1;
        long now = 100_000;
        var def = new BBS757Definition(map) { NestRunning = () => true, NowMs = () => now, WriteIo = _ => io.Io };
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(OnKey, 0, speech);
        def.ProcessSimVarUpdate(AvailKey, 1, speech);

        def.HandleUIVariableSet(Bbs757Annunciators.ExtPwrButtonNode, 1, new SimVarDefinition(), new SimConnectManager(IntPtr.Zero), speech);
        await io.WritesReached(1);
        now += 300;
        def.ProcessSimVarUpdate(OnKey, 1, speech);     // the press's own echo
        def.ProcessSimVarUpdate(AvailKey, 0, speech);  // AVAIL out as ON lights
        for (int i = 0; i < 200 && !speech.All.Contains("EXT PWR on"); i++)
        {
            for (int d = 0; d < io.Delays.Count; d++) io.Release(d);   // each wait as it is asked
            await Task.Delay(10);
        }
        now += 1_000;
        def.ProcessSimVarUpdate(AvailKey, 0, speech);

        Assert.Equal(new[] { "EXT PWR on" }, speech.All);
    }

    [Fact]
    public void A_row_the_flight_load_left_alone_speaks_its_first_change_after_the_load()
    {
        // After a flight load a row the new flight did not change is not delivered again (the batch delivers
        // changes only), so its next delivery is a real change, not a baseline: the parking brake released
        // after loading parked must be heard.
        var rig = new Rig();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 1);
        rig.Deliver(Bbs757Readouts.SpeedbrakeKey, 0);

        rig.Def.OnSimContextReset();
        rig.Deliver(Bbs757Readouts.SpeedbrakeKey, 1);   // BlueBird publishes the new flight
        rig.QuietBatches();                             // and nothing moves for five deliveries
        Assert.False(rig.Def.LoadQuietPending);
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 0);

        Assert.Equal(new[] { Bbs757Readouts.ParkingBrakePhrase(0) }, rig.Speech.All);
    }

    [Fact]
    public void A_change_the_flight_load_brings_is_not_spoken_however_late_it_comes()
    {
        // No wall clock: AircraftLoaded fires during the loading screen, and until BlueBird has published and
        // gone quiet a change is the new flight as found. With nothing of BlueBird's own moving yet, a minute of
        // batches ends nothing before the gate's ceiling.
        var rig = new Rig();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 1);

        rig.Def.OnSimContextReset();
        for (int i = 0; i < 20; i++) rig.Batch();
        Assert.True(rig.Def.LoadQuietPending);
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 0);   // the new flight's brake: no change
        rig.Batch();
        Assert.True(rig.Def.LoadQuietPending);            // it moved: the quiet count starts over

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_flight_load_that_changes_nothing_ends_its_quiet_at_the_ceiling()
    {
        var rig = new Rig();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 1);

        rig.Def.OnSimContextReset();
        rig.Batch();                                                   // the first full cycle
        rig.Now += Aircraft.MD11.Md11SeedGate.CeilingMs;
        rig.Batch();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 0);

        Assert.Equal(new[] { Bbs757Readouts.ParkingBrakePhrase(0) }, rig.Speech.All);
    }

    [Fact]
    public void The_first_change_after_a_reconnect_is_spoken()
    {
        // The reconnect's first batch delivers every row again BEFORE MainForm's Connected branch calls
        // ResetAnnouncementBaselines: a wipe there took the next real change for a baseline.
        var rig = new Rig();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 1);

        rig.Def.OnSimContextReset();                      // the drop
        rig.Def.OnVariableCacheCleared();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 1);   // the reconnect's re-fire: a baseline
        rig.Def.ResetAnnouncementBaselines();             // Connected
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 0);

        Assert.Equal(new[] { Bbs757Readouts.ParkingBrakePhrase(0) }, rig.Speech.All);
    }

    [Fact]
    public void A_dropped_connection_makes_every_next_reading_a_baseline()
    {
        // The reconnect delivers every row again, whatever happened meanwhile: none of it is a change.
        var rig = new Rig();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 1);

        rig.Def.OnSimContextReset();
        rig.Def.OnVariableCacheCleared();
        rig.Deliver(Bbs757Readouts.ParkingBrakeKey, 0);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void An_irs_switch_value_survives_a_flight_load()
    {
        // A switch value is a fact, not a baseline: the batch delivers only changes, so after a flight load
        // the switch at NAV is not delivered again, and the IRS must still say aligned.
        var rig = new Rig();
        rig.Deliver("BBS757_IRS_L_SWITCH_WATCH", 2);
        rig.Deliver("BBS757_IRS_L_ALIGN", 1);
        rig.Def.OnSimContextReset();
        rig.Deliver("BBS757_IRS_L_ALIGN", 1);   // the new flight as found
        rig.Batch();
        rig.Now += Aircraft.MD11.Md11SeedGate.CeilingMs;
        rig.Batch();                            // the load's quiet is over: a change is news again
        rig.Deliver("BBS757_IRS_L_ALIGN", 0);
        Assert.Equal(new[] { "IRS L aligned" }, rig.Speech.All);
    }

    [Fact]
    public void A_ground_power_window_run_is_heard_once_with_both_rows_present()
    {
        // Plan 07's handoff: the GPU tap changes the AVAIL light too (about 0.17 s after it); the window says
        // what came of its taps, so neither the Ground Power row nor the AVAIL row may say it again.
        var rig = new Rig();
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 0);
        rig.Deliver(AvailKey, 0);

        rig.Def.ArmGroundPowerEcho();
        rig.Now += 6_000;
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 1);
        rig.Deliver(AvailKey, 1);
        rig.Def.SettleGroundPowerEcho(1);
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 1);
        rig.Deliver(AvailKey, 1);
        Assert.Empty(rig.Speech.All);

        // A later change from elsewhere (the cart removed by BlueBird) speaks both rows.
        rig.Now += 60_000;
        rig.Deliver(Bbs757SharedKeys.GpuConnected, 0);
        rig.Deliver(AvailKey, 0);
        Assert.Equal(new[] { "Ground power disconnected", "External power not available" }, rig.Speech.All);
    }

    // ---- Task 4: the essential announcements ---------------------------------------------------------------

    /// <summary>Delivers each (key, value) in order and returns what was said.</summary>
    private static List<string> Said(params (string Key, double Value)[] deliveries)
    {
        var rig = new Rig();
        foreach (var (key, value) in deliveries) Assert.True(rig.Deliver(key, value));
        return rig.Speech.All;
    }

    [Fact]
    public void Asa_phrases()
    {
        // The upper ASA: 0 LAND 3, 2 LAND 2, 1 blank (silent); the first reading is a baseline.
        Assert.Equal(new[] { "LAND 3", "LAND 2", "LAND 3" }, Said(
            ("BBS757_ASA_UPPER", 1), ("BBS757_ASA_UPPER", 0), ("BBS757_ASA_UPPER", 2),
            ("BBS757_ASA_UPPER", 1), ("BBS757_ASA_UPPER", 0)));
        Assert.Equal(new[] { "NO LAND 3", "NO AUTOLAND" }, Said(
            ("BBS757_ASA_LOWER", 1), ("BBS757_ASA_LOWER", 0), ("BBS757_ASA_LOWER", 1), ("BBS757_ASA_LOWER", 2)));
        Assert.Empty(Said(("BBS757_ASA_UPPER", 0)));   // LAND 3 as found: no change
    }

    [Theory]
    [InlineData("BBS757_ALT_ALERT", "Altitude alert")]
    [InlineData("BBS757_CONFIG_WARNING", "Configuration warning")]
    public void Alerts_speak_when_they_light(string key, string said)
    {
        Assert.Equal(new[] { said, said }, Said((key, 0), (key, 1), (key, 0), (key, 1)));
        Assert.Empty(Said((key, 1), (key, 0)));   // lit as found, then out: silent
    }

    [Theory]
    [InlineData("L")]
    [InlineData("C")]
    [InlineData("R")]
    public void Irs_aligned_only_in_nav(string irs)
    {
        string light = $"BBS757_IRS_{irs}_ALIGN", sw = $"BBS757_IRS_{irs}_SWITCH_WATCH";

        // OFF to NAV: the ALIGN light comes on, then goes out at NAV: aligned.
        Assert.Equal(new[] { $"IRS {irs} aligned" }, Said((sw, 0), (light, 0), (sw, 2), (light, 1), (light, 0)));

        // Switched off while aligning: the light goes out too, and nothing is said.
        Assert.Empty(Said((sw, 1), (light, 0), (light, 1), (sw, 0), (light, 0)));
        Assert.Empty(Said((sw, 2), (light, 1), (sw, 0), (light, 0)));

        // The switch row itself is never spoken.
        Assert.Empty(Said((sw, 0), (sw, 1), (sw, 2), (sw, 3)));
    }

    [Theory]
    [InlineData("BBS757_DOOR_L1", 100, "Door L1")]
    [InlineData("BBS757_DOOR_R3", 100, "Door R3")]
    [InlineData("BBS757_DOOR_CARGO_FWD", 200, "Forward cargo door")]
    [InlineData("BBS757_DOOR_CARGO_AFT", 200, "Aft cargo door")]
    public void Door_ends_only(string key, double full, string name)
    {
        // Animating through the middle speaks only at the ends.
        Assert.Equal(new[] { $"{name} open", $"{name} closed" }, Said(
            (key, 0), (key, full * 0.3), (key, full * 0.6), (key, full * 0.95), (key, full * 0.5),
            (key, full), (key, full * 0.2), (key, full * 0.02)));
    }

    [Fact]
    public void Start_valve_closed_is_said_at_cutout_and_open_is_quiet()
    {
        // The start switch (GND 1, AUTO 3): the pilot picks GND (heard as the pick), BlueBird releases it to
        // AUTO at cutout (2026-10-27). Only the cutout speaks.
        Assert.Equal(new[] { "Left start valve closed" },
            Said(("BBS757_L_START_VALVE", 3), ("BBS757_L_START_VALVE", 1), ("BBS757_L_START_VALVE", 3)));
        Assert.Empty(Said(("BBS757_R_START_VALVE", 3), ("BBS757_R_START_VALVE", 1)));
        // CONT and FLT are closed too; moving between them says nothing.
        Assert.Empty(Said(("BBS757_R_START_VALVE", 3), ("BBS757_R_START_VALVE", 5), ("BBS757_R_START_VALVE", 6)));
    }

    public static TheoryData<string, string, string, string?> Task4Rows => new()
    {
        { "BBS757_ASA_UPPER", "B757_MAIN_L_AUTOLAND_1_BUTTON_ON", "Autoland Status Upper", "Status" },
        { "BBS757_ASA_LOWER", "B757_MAIN_L_AUTOLAND_2_BUTTON_ON", "Autoland Status Lower", "Status" },
        { "BBS757_ALT_ALERT", "BBS757_CA_ALTIMETER_ALT_ALERT_LIGHT", "Altitude Alert", null },
        { "BBS757_CONFIG_WARNING", "B757_MAIN_CONFIG_1_DECAL", "Configuration Warning", null },
        { "BBS757_IRS_L_ALIGN", "B757_OVHD_IRS_L_ALIGN_DECAL", "IRS L Aligned", null },
        { "BBS757_IRS_C_ALIGN", "B757_OVHD_IRS_C_ALIGN_DECAL", "IRS C Aligned", null },
        { "BBS757_IRS_R_ALIGN", "B757_OVHD_IRS_R_ALIGN_DECAL", "IRS R Aligned", null },
        { "BBS757_L_START_VALVE", "B757_OVHD_L_ENG_START_SWITCH", "Left Start Valve", "Engines" },
        { "BBS757_R_START_VALVE", "B757_OVHD_R_ENG_START_SWITCH", "Right Start Valve", "Engines" },
        { "BBS757_DOOR_L1", "B757_DOORS_PAX_L1", "Door L1", null },
        { "BBS757_DOOR_L2", "B757_DOORS_PAX_L2", "Door L2", null },
        { "BBS757_DOOR_L3", "B757_DOORS_PAX_L3", "Door L3", null },
        { "BBS757_DOOR_R1", "B757_DOORS_PAX_R1", "Door R1", null },
        { "BBS757_DOOR_R2", "B757_DOORS_PAX_R2", "Door R2", null },
        { "BBS757_DOOR_R3", "B757_DOORS_PAX_R3", "Door R3", null },
        { "BBS757_DOOR_CARGO_FWD", "B757_DOORS_CARGO_FWD", "Forward Cargo Door", null },
        { "BBS757_DOOR_CARGO_AFT", "B757_DOORS_CARGO_AFT", "Aft Cargo Door", null },
    };

    [Theory]
    [MemberData(nameof(Task4Rows))]
    public void Rows_are_announced_and_muteable(string key, string lvar, string label, string? panel)
    {
        var def = new Rig(Bbs757ControlMap.Parse("""
            {
              "controls": [
                { "node": "B757_A", "section": "Pedestal", "panel": "Engines", "label": "A", "kind": "toggle", "state_var": "B757_A" },
                { "node": "B757_B", "section": "Overhead", "panel": "Electrical", "label": "B", "kind": "toggle", "state_var": "B757_B" }
              ]
            }
            """)).Def;
        var row = def.GetVariables()[key];

        Assert.Equal(lvar, row.Name);
        Assert.Equal(label, row.DisplayName);
        Assert.Equal(SimVarType.LVar, row.Type);
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
        Assert.True(row.IsAnnounced);
        Assert.False(row.ExcludeFromMonitorManager);
        Assert.Contains(def.MonitorRows(), r => r.Key == key && r.Label == label);
        Assert.False(def.IsMuteWrapExempt(key));   // MainForm's Ctrl+M wrap mutes it ([VAR-8])

        var shownOn = def.GetPanelDisplayVariables().Where(p => p.Value.Contains(key)).Select(p => p.Key).ToList();
        Assert.Equal(panel == null ? Array.Empty<string>() : new[] { panel }, shownOn);
        Assert.Equal(panel != null, row.RenderAsReadOnlyStatus);
    }

    [Theory]
    [InlineData("L")]
    [InlineData("C")]
    [InlineData("R")]
    public void Irs_switch_watches_are_silent_rows(string irs)
    {
        var def = new BBS757Definition { NestRunning = () => true };
        string key = $"BBS757_IRS_{irs}_SWITCH_WATCH";
        var row = def.GetVariables()[key];

        Assert.Equal($"B757_IRS_{irs}_SWITCH", row.Name);
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
        Assert.True(row.ExcludeFromMonitorManager);   // it speaks nothing: no Ctrl+M row ([VAR-9])
        Assert.DoesNotContain(def.MonitorRows(), r => r.Key == key);
        Assert.DoesNotContain(key, def.GetPanelDisplayVariables().SelectMany(p => p.Value));
    }

    [Fact]
    public void Variable_ceiling_holds()
    {
        var vars = new BBS757Definition { NestRunning = () => true }.GetVariables();
        Assert.True(vars.Count < 900, $"{vars.Count} variables");
        // Every row of the plan registered, each under its own key.
        Assert.All(Bbs757Annunciators.Rows, r => Assert.Equal(r.LVar, vars[r.Key].Name));
        Assert.Equal(22, Bbs757Annunciators.Rows.Count);
    }

    // ---- Plan 14: squawk, TCAS mode and altimeter changes -------------------------------------------

    private const string SquawkKey = Bbs757Annunciators.SquawkWatchKey;
    private const string TcasKey = Bbs757Annunciators.TcasModeWatchKey;
    private const string AltimeterKey = Bbs757Annunciators.AltimeterWatchKey;

    private static BBS757Definition WatchDef(Func<long> now, Func<string, bool>? muted = null)
        => new() { NestRunning = () => true, NowMs = now, UseAltimeterTimer = false, IsMonitorMuted = muted ?? (_ => false) };

    [Fact]
    public void Squawk_change_from_elsewhere_is_spoken()
    {
        var rig = new Rig();
        rig.Deliver(SquawkKey, 0x1200);   // the cockpit as found: silent
        rig.Deliver(SquawkKey, 0x4743);

        Assert.Equal(new[] { "Squawk 4743" }, rig.Speech.All);
    }

    [Fact]
    public void Own_squawk_set_not_repeated()
    {
        long now = 100_000;
        var def = WatchDef(() => now);
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(SquawkKey, 0x1200, speech);

        def.ArmOwnSquawkEcho();   // the squawk field's set (OnOwnSquawkSet); its read-back speaks
        now += 1500;
        def.ProcessSimVarUpdate(SquawkKey, 0x4743, speech);
        Assert.Empty(speech.All);

        now += BBS757Definition.OwnWriteEchoMs;   // later, another tool's change is heard again
        def.ProcessSimVarUpdate(SquawkKey, 0x7000, speech);
        Assert.Equal(new[] { "Squawk 7000" }, speech.All);
    }

    [Theory]
    [InlineData(0.0, "TCAS STBY")]
    [InlineData(1.0, "TCAS ALT RPTG OFF")]
    [InlineData(2.0, "TCAS XPNDR")]
    [InlineData(3.0, "TCAS TA ONLY")]
    [InlineData(4.0, "TCAS TA/RA")]
    [InlineData(5.0, null)]
    [InlineData(2.5, null)]
    [InlineData(double.NaN, null)]
    public void Tcas_mode_words(double value, string? said)
    {
        Assert.Equal(said, Bbs757Annunciators.TcasModePhrase(value));
    }

    [Fact]
    public void Tcas_mode_change_from_elsewhere_is_spoken_and_a_pick_is_the_picks()
    {
        var rig = new Rig();
        rig.Deliver(TcasKey, 2);
        rig.Deliver(TcasKey, 4);
        Assert.Equal(new[] { "TCAS TA/RA" }, rig.Speech.All);

        // A pick on the panel arms the row's echo (the screen reader read the combo) and settles on its read-back.
        Assert.Equal(TcasKey, BBS757Definition.LinkedStatusKey(Bbs757Annunciators.TcasModeNode));
        Assert.Equal("TCAS XPNDR", BBS757Definition.LinkedPhrase(TcasKey, 2));
        Assert.Equal("TCAS mode unavailable", BBS757Definition.LinkedPhrase(TcasKey, null));
    }

    [Fact]
    public async Task A_tcas_pick_on_the_panel_is_not_said_again()
    {
        long now = 0;
        var sim = new FakeTcasSim();
        var def = new BBS757Definition { NestRunning = () => true, NowMs = () => now, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(TcasKey, 2, speech);
        sim.OnWrite = () => def.ProcessSimVarUpdate(TcasKey, 4, speech);   // the change arrives during the pick
        sim.Fresh[Bbs757Annunciators.TcasModeNode] = 2;
        sim.Fresh[TcasKey] = 4;

        var knob = Bbs757Annunciators.TcasModeNode;
        def.HandleUIVariableSet(knob, 4, def.GetVariables()[knob], new SimConnectManager(IntPtr.Zero), speech);
        await sim.Done;

        Assert.NotEmpty(sim.Writes);
        Assert.Empty(speech.All);
    }

    /// <summary>A simulated sim for one pick: fresh reads by key, the writes, and a hook on each write.</summary>
    private sealed class FakeTcasSim
    {
        public readonly Dictionary<string, double?> Fresh = new(StringComparer.Ordinal);
        public readonly List<string> Writes = new();
        public Action? OnWrite;
        private readonly TaskCompletionSource _done = new();
        public Task Done => Task.WhenAny(_done.Task, Task.Delay(2000));

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => true,
            ReadFresh = (key, ct) => Task.FromResult(Fresh.TryGetValue(key, out double? v) ? v : null),
            Cached = key => Fresh.TryGetValue(key, out double? v) ? v : null,
            Write = rpn => { Writes.Add(rpn); OnWrite?.Invoke(); },
            RequestRead = _ => _done.TrySetResult(),
            Delay = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
        };
    }

    [Fact]
    public void Altimeter_spoken_once_it_rests_without_another_delivery()
    {
        long now = 100_000;
        var def = WatchDef(() => now);
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(AltimeterKey, 1013.25, speech);   // baseline: silent

        // A baro knob turning: a new value each second, then nothing (the row is delivered only on change).
        foreach (double mb in new[] { 1015.0, 1017.0, 1020.0 })
        {
            def.ProcessSimVarUpdate(AltimeterKey, mb, speech);
            now += 1000;
            def.ReleaseRestedAltimeter(now, speech);
        }
        Assert.Empty(speech.All);   // still turning

        now += Bbs757RestingValue.RestMs;
        def.ReleaseRestedAltimeter(now, speech);
        now += 5000;
        def.ReleaseRestedAltimeter(now, speech);

        Assert.Equal(new[] { "Altimeter: 1020, 30.12" }, speech.All);
    }

    [Fact]
    public void Altimeter_set_to_standard_says_standard()
    {
        long now = 100_000;
        var def = WatchDef(() => now);
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(AltimeterKey, 1020, speech);
        def.ProcessSimVarUpdate(AltimeterKey, 1013.25, speech);
        now += Bbs757RestingValue.RestMs;
        def.ReleaseRestedAltimeter(now, speech);

        Assert.Equal(new[] { "Altimeter standard" }, speech.All);
    }

    [Fact]
    public void Muted_altimeter_row_is_dropped()
    {
        long now = 100_000;
        var def = WatchDef(() => now, key => key == AltimeterKey);
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(AltimeterKey, 1013.25, speech);
        def.ProcessSimVarUpdate(AltimeterKey, 1020, speech);
        now += Bbs757RestingValue.RestMs;
        def.ReleaseRestedAltimeter(now, speech);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void Own_altimeter_set_not_repeated()
    {
        long now = 100_000;
        var def = WatchDef(() => now);
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(AltimeterKey, 1013.25, speech);

        def.ArmOwnAltimeterEcho();   // Ctrl+B's set (OnOwnAltimeterSet); its read-back speaks
        now += 1000;
        def.ProcessSimVarUpdate(AltimeterKey, 1020, speech);
        now += Bbs757RestingValue.RestMs;
        def.ReleaseRestedAltimeter(now, speech);
        Assert.Empty(speech.All);

        now += BBS757Definition.OwnWriteEchoMs;   // later, a knob turned in the cockpit is heard
        def.ProcessSimVarUpdate(AltimeterKey, 1009, speech);
        now += Bbs757RestingValue.RestMs;
        def.ReleaseRestedAltimeter(now, speech);
        Assert.Equal(new[] { "Altimeter: 1009, 29.80" }, speech.All);
    }

    [Fact]
    public void Watches_registered_as_announced_rows_with_ctrl_m_rows()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var vars = def.GetVariables();
        foreach (var (key, name, label) in new[]
                 {
                     (SquawkKey, "TRANSPONDER CODE:1", "Squawk"),
                     (TcasKey, "B757_PED_TCAS_MODE_KNOB", "TCAS Mode"),
                     (AltimeterKey, "KOHLSMAN SETTING MB:1", "Altimeter"),
                 })
        {
            var v = vars[key];
            Assert.Equal(name, v.Name);
            Assert.Equal(label, v.DisplayName);
            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced);
            Assert.False(v.ExcludeFromMonitorManager);
        }

        // The squawk is the transponder panel's read-back row itself (one variable, [VAR-7]); the others are on no display.
        Assert.Equal(Bbs757Squawk.CodeKey, SquawkKey);
        Assert.True(vars[SquawkKey].RenderAsReadOnlyStatus);
        Assert.Contains(def.GetPanelDisplayVariables().Values, rows => rows.Contains(SquawkKey));
        Assert.DoesNotContain(def.GetPanelDisplayVariables().Values, rows => rows.Contains(TcasKey) || rows.Contains(AltimeterKey));
    }

    [Fact]
    public void Watch_phrases_under_a_comma_decimal_culture()
    {
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            InCulture(culture, () =>
            {
                Assert.Equal("Altimeter: 1020, 30.12", Bbs757Annunciators.AltimeterPhrase(1020));
                Assert.Equal("Altimeter: 1009, 29.80", Bbs757Annunciators.AltimeterPhrase(1009));
                Assert.Equal("Squawk 0421", Bbs757Annunciators.SquawkPhrase(0x0421));
                Assert.Equal("TCAS TA/RA", Bbs757Annunciators.TcasModePhrase(4));
            });
        }
        Assert.Null(Bbs757Annunciators.AltimeterPhrase(0));
        Assert.Null(Bbs757Annunciators.SquawkPhrase(double.NaN));
    }

    [Fact]
    public void Every_row_speaks_the_same_under_a_comma_decimal_culture()
    {
        double[] values = { 0, 0.3, 0.5, 0.7, 1, 2, 5, 50, 90, 95, 100, 150, 180, 200, double.NaN };
        double?[] related = { null, 0, 1, 2 };

        List<string?> Phrases()
            => Bbs757Annunciators.Rows.SelectMany(r => values.SelectMany(v => related.Select(c => r.Phrase(v, c)))).ToList();

        List<string?> expected = new();
        InCulture("", () => expected = Phrases());
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(expected, Phrases()));
    }
}
