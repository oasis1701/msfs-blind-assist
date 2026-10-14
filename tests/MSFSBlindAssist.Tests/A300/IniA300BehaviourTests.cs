using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// What the A300 definition sends and says, against a SimConnectManager that never connected, a
/// fake cache, a fake clock and a recording sender.
/// </summary>
public class IniA300BehaviourTests
{
    private long _now = 10_000;
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly List<string> _reReads = new();
    private readonly List<TaskCompletionSource> _waits = new();

    public IniA300BehaviourTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (key, _) => _reReads.Add(key),
            Delay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
        };
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    [Fact]
    public void The_fcu_windows_take_typed_values_as_the_fleet_declares_them()
    {
        // The windows are written directly (A300TypedValues), as the MD-11's and the FBW Airbuses' are.
        Assert.Equal(FCUControlType.SetValue, _def.GetAltitudeControlType());
        Assert.Equal(FCUControlType.SetValue, _def.GetHeadingControlType());
        Assert.Equal(FCUControlType.SetValue, _def.GetSpeedControlType());
        Assert.Equal(FCUControlType.SetValue, _def.GetVerticalSpeedControlType());
    }

    [Fact]
    public void A_base_variable_is_left_to_mainform()
    {
        Assert.False(Set("SIM_ON_GROUND", 1));
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_switch_is_flipped_once_and_nothing_is_said()
    {
        _cache["A300_BATT_1"] = 0;
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_BATT_1_Set)" }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_second_pick_before_the_cache_catches_up_plans_from_the_first()
    {
        _cache["A300_BATT_1"] = 0;
        Assert.True(Set("A300_BATT_1", 1));
        Assert.True(Set("A300_BATT_1", 1));   // the cache still says 0: without the commanded state this flipped it back
        Assert.Single(_sent);
    }

    [Fact]
    public void Two_rows_on_one_variable_share_the_commanded_hold()
    {
        // The courier low pressure supply and crew oxygen supply buttons fire their own events but flip ONE
        // variable (measured live, 2026-10-13: a Set of 1 turned it off while it was on).
        _cache["A300_COURIER_O2_SUPPLY"] = 0;
        _cache["A300_PAX_OXY_SUPPLY"] = 0;
        Assert.True(Set("A300_COURIER_O2_SUPPLY", 1));
        Assert.True(Set("A300_PAX_OXY_SUPPLY", 1));   // its own cache still says 0
        Assert.Single(_sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_second_row_on_the_same_variable_is_planned_from_the_first_rows_pick()
    {
        // The shared hold is the first row's pick, not a block on the second row: asking the other row for the
        // opposite position is a real flip.
        _cache["A300_COURIER_O2_SUPPLY"] = 0;
        _cache["A300_PAX_OXY_SUPPLY"] = 0;
        Assert.True(Set("A300_COURIER_O2_SUPPLY", 1));
        Assert.True(Set("A300_PAX_OXY_SUPPLY", 0));
        Assert.Equal(new[] { "1 (>B:AIRLINER_COURIER_O2_SUPPLY_Set)", "0 (>B:AIRLINER_PAX_OXY_SUPPLY_Set)" }, _sent);
    }

    [Fact]
    public void A_row_sharing_its_variable_with_another_reads_it_live_on_its_own_subscription()
    {
        // The crew oxygen supply button toggles the courier supply's variable (iniBuilds' wiring): read
        // once at panel open, its row went stale after the other row's write, and its next pick was
        // planned from the old position. [VAR-7]: the copy stays off the batch.
        var vars = _def.GetVariables();
        var courier = vars["A300_COURIER_O2_SUPPLY"];
        var crew = vars["A300_PAX_OXY_SUPPLY"];
        Assert.Equal(courier.Name, crew.Name);
        Assert.True(ContinuousBatchLayout.RidesBatch(courier));
        Assert.Equal((UpdateFrequency.Continuous, true, true),
            (crew.UpdateFrequency, crew.IsAnnounced, crew.ExcludeFromBatch));
    }

    [Fact]
    public void Every_position_row_follows_the_cockpit_live()
    {
        // Every combo; a knob's slider is read when its panel opens.
        var rows = _def.GetVariables().Values.Where(v => v.ValueDescriptions?.Count > 0 && !v.RenderAsSlider);
        Assert.NotEmpty(rows);
        Assert.Empty(rows.Where(v => v.UpdateFrequency != UpdateFrequency.Continuous).Select(v => $"{v.DisplayName} ({v.Name})"));
    }

    private void ReadFreshReturns(string key, double value)
    {
        _def.TypedDelay = _ => Task.CompletedTask;
        _def.ReadFresh = (_, k, _) => Task.FromResult<double?>(k == key ? value : null);
    }

    [Fact]
    public void A_switch_the_aircraft_did_not_move_snaps_back_and_says_so()
    {
        // The crossbleed button acts only in manual mode: in auto the aircraft ignores it, and the combo kept
        // the pilot's pick while the valve stayed open (2026-10-10). Worded as the MD-11 and the iFly 737 word it.
        _cache["A300_AIR_XFEED"] = 1;
        ReadFreshReturns("A300_AIR_XFEED", 1);
        Assert.True(Set("A300_AIR_XFEED", 0));
        Assert.Equal(new[] { "0 (>B:AIRLINER_AIR_XFEED_Set)" }, _sent);
        Assert.Equal(new[] { "Crossbleed did not move" }, _speech.All);
        Assert.Equal(new[] { "A300_AIR_XFEED" }, _reReads);
    }

    [Fact]
    public void A_read_back_between_positions_is_not_called_did_not_move()
    {
        // A valve in transit reads between its positions: that is no position, so it is neither "did not move"
        // nor a position to plan the next pick from.
        _cache["A300_AIR_XFEED"] = 1;
        ReadFreshReturns("A300_AIR_XFEED", 0.5);
        Assert.True(Set("A300_AIR_XFEED", 0));
        Assert.Empty(_speech.All);
        Assert.Empty(_reReads);
    }

    [Fact]
    public void A_switch_that_moved_says_nothing()
    {
        _cache["A300_BATT_1"] = 0;
        ReadFreshReturns("A300_BATT_1", 1);
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Empty(_speech.All);
        Assert.Empty(_reReads);
    }

    [Fact]
    public void A_switch_that_did_not_move_is_planned_from_where_it_is()
    {
        _cache["A300_AIR_XFEED"] = 1;
        ReadFreshReturns("A300_AIR_XFEED", 1);
        Set("A300_AIR_XFEED", 0);
        Assert.True(Set("A300_AIR_XFEED", 0));   // without the read-back this planned from the stale pick: nothing sent
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public void Only_the_latest_pick_of_a_switch_is_checked()
    {
        var release = new TaskCompletionSource();
        _def.TypedDelay = _ => release.Task;
        _def.ReadFresh = (_, _, _) => Task.FromResult<double?>(1);
        _cache["A300_AIR_XFEED"] = 1;
        Set("A300_AIR_XFEED", 0);
        Set("A300_AIR_XFEED", 1);   // the pilot changed their mind before the first check
        release.SetResult();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_lever_that_moved_then_dropped_is_not_said_to_have_not_moved()
    {
        // Pitch trim 2 picked on with no hydraulics: it engaged, then dropped before the read-back, which said
        // it had not moved after the drop's own "Pitch trim 2 off" (2026-10-10).
        var release = new TaskCompletionSource();
        _def.TypedDelay = _ => release.Task;
        _def.ReadFresh = (_, _, _) => Task.FromResult<double?>(0);
        _cache["A300_PITCH_TRIM_2"] = 0;
        Deliver("A300_PITCH_TRIM_2", 0);
        Set("A300_PITCH_TRIM_2", 1);
        Deliver("A300_PITCH_TRIM_2", 1);
        _speech.All.Clear();   // the engage: the real announcer drops it inside the echo wrap
        Deliver("A300_PITCH_TRIM_2", 0);
        release.SetResult();
        Assert.Equal(new[] { "Pitch trim 2 off" }, _speech.All);
        Assert.Empty(_reReads);
    }

    [Fact]
    public void A_spring_switch_is_read_again_after_it_returns()
    {
        // Its 1 s hold can fall between two 1 Hz deliveries, which then see no change: the rudder trim combo
        // kept "Left" after the switch had returned (2026-10-10).
        _cache["A300_NOSE_TRIM"] = 1;
        Assert.True(Set("A300_NOSE_TRIM", 2));
        Assert.Empty(_reReads);
        while (_waits.FirstOrDefault(w => !w.Task.IsCompleted) is { } wait)
            wait.SetResult();
        Assert.Equal(new[] { "A300_NOSE_TRIM" }, _reReads);
    }

    [Theory]
    [InlineData("A300_LANDING_ELEV_SET#INC")]
    [InlineData("A300_LANDING_ELEV_SET#DEC")]
    public void A_landing_elevation_knob_step_is_read_back(string row)
    {
        // A numeric confirmation, as the FCU and flex temperature knobs' (the box alone said it, 2026-10-10).
        ReadFreshReturns("A300_RO_LANDING_ELEV", 50);
        Assert.True(Set(row, 1));
        Assert.Equal(new[] { "Landing elevation 50 feet" }, _speech.All);
    }

    [Fact]
    public void A_read_back_whose_value_lands_after_the_definition_went_away_says_nothing()
    {
        // An aircraft switch disposes the definition before the pending reads are failed, and a delivery can land
        // in between: that value belongs to the old aircraft.
        var read = new TaskCompletionSource<double?>();
        _def.TypedDelay = _ => Task.CompletedTask;
        _def.ReadFresh = (_, _, _) => read.Task;
        Assert.True(Set("A300_LANDING_ELEV_SET#INC", 1));
        _def.Dispose();
        read.SetResult(50);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_moved_check_whose_value_lands_after_the_definition_went_away_says_nothing()
    {
        // The same for a switch pick's read-back: a real position that is not the pick would say "did not move"
        // over the next aircraft, and ask for the old aircraft's row again.
        var read = new TaskCompletionSource<double?>();
        _def.TypedDelay = _ => Task.CompletedTask;
        _def.ReadFresh = (_, _, _) => read.Task;
        _cache["A300_AIR_XFEED"] = 1;
        Assert.True(Set("A300_AIR_XFEED", 0));
        _def.Dispose();
        read.SetResult(1);
        Assert.Empty(_speech.All);
        Assert.Empty(_reReads);
    }

    private void ReadFreshReturns(Dictionary<string, double> values)
    {
        _def.TypedDelay = _ => Task.CompletedTask;
        _def.ReadFresh = (_, k, _) => Task.FromResult<double?>(values.TryGetValue(k, out var v) ? v : null);
    }

    [Theory]
    // The transfer switch picks the window in use; it does not swap them (2026-10-10). After a transfer, the
    // knob iniBuilds calls "standby" tunes the frequency in use, and its read-back says so. The ADF knobs are
    // the knobs left (the VHF ones are not rows), so they keep this logic pinned.
    [InlineData("A300_CPT_ADF2_MED#INC", 0, "ADF 1 active 990")]
    [InlineData("A300_CPT_ADF2_MED#DEC", 1, "ADF 1 standby 990")]
    [InlineData("A300_CPT_ADF1_BIG#INC", 1, "ADF 1 active 900")]
    [InlineData("A300_CPT_ADF1_SMALL#DEC", 0, "ADF 1 standby 900")]
    public void An_adf_knob_step_is_read_back_by_its_windows_role(string row, double transfer, string phrase)
    {
        var adf = A300Radios.ByTransfer["A300_ADF1_TFR"];
        ReadFreshReturns(new() { [adf.Window1Key] = 900, [adf.Window2Key] = 990, [adf.TransferKey] = transfer });
        Assert.True(Set(row, 1));
        Assert.Equal(new[] { phrase }, _speech.All);
    }

    [Theory]
    [InlineData("A300_CPT_VHF1_MHZ#INC")]
    [InlineData("A300_CPT_VHF2_KHZ#DEC")]
    [InlineData("A300_FO_VHF1_KHZ#INC")]
    [InlineData("A300_VOR100_CAPT#INC")]
    [InlineData("A300_VOR10_FO#DEC")]
    [InlineData("A300_ILS_100#INC")]
    public void A_vhf_or_nav_frequency_knob_is_no_row_of_the_definition(string row)
    {
        // Left out of the panels (design decision, Robin, 2026-10-14): the definition does not claim it, and nothing is sent.
        Assert.DoesNotContain(row, _def.GetVariables().Keys);
        Assert.False(_def.HandleUIVariableSet(row, 1, new SimVarDefinition(), _sim, _speech));
        Assert.Empty(_sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_transfer_is_read_back_with_the_frequency_now_in_use()
    {
        var adf = A300Radios.ByTransfer["A300_ADF1_TFR"];
        ReadFreshReturns(new() { [adf.Window1Key] = 900, [adf.Window2Key] = 990, [adf.TransferKey] = 0 });
        Assert.True(Set("A300_ADF1_TFR", 1));
        Assert.Equal(new[] { "ADF 1 active 990" }, _speech.All);
    }

    private void ReleaseAllWaits()
    {
        while (_waits.FirstOrDefault(w => !w.Task.IsCompleted) is { } wait)
            wait.SetResult();
    }

    private void DoorReadsOpen70() => ReadFreshReturns(new()
    {
        [A300CargoDoor.StatusKey] = 0.7, [A300CargoDoor.ClosedKey] = 0, [A300CargoDoor.LockedKey] = 0,
        [A300CargoDoor.At70Key] = 1, [A300CargoDoor.At145Key] = 0,
    });

    [Fact]
    public void The_cargo_door_says_where_it_stopped_once_its_switch_is_let_go()
    {
        // The door moves only while its switch is held, about 30 s end to end (2026-10-10): the hold's end is
        // the only moment the pilot learns where it went, which the screen reader cannot say.
        _cache["A300_MAIN_CARGO_DOOR_SWITCH"] = 1;
        DoorReadsOpen70();
        Assert.True(Set("A300_MAIN_CARGO_DOOR_SWITCH", 0));
        Assert.Empty(_speech.All);
        ReleaseAllWaits();
        Assert.Equal(new[] { "Cargo door open 70 degrees" }, _speech.All);
        Assert.Equal(new[] { "A300_MAIN_CARGO_DOOR_SWITCH" }, _reReads);
    }

    [Fact]
    public void A_later_pick_of_a_spring_drops_the_earlier_picks_release()
    {
        // Open, Neutral, then Open again within the 35 s hold: the first hold's release must not stop the second.
        _cache["A300_MAIN_CARGO_DOOR_SWITCH"] = 1;
        DoorReadsOpen70();
        Set("A300_MAIN_CARGO_DOOR_SWITCH", 0);
        var firstHold = _waits[0];
        Set("A300_MAIN_CARGO_DOOR_SWITCH", 1);
        Set("A300_MAIN_CARGO_DOOR_SWITCH", 0);
        _sent.Clear();
        firstHold.SetResult();
        Assert.Empty(_sent);
        ReleaseAllWaits();
        Assert.Equal(new[] { "1 (>B:AIRLINER_MAIN_CARGO_DOOR_SWITCH_Set)" }, _sent);
    }

    [Fact]
    public void A_cargo_door_pick_overtaken_by_neutral_speaks_once_for_the_stop()
    {
        _cache["A300_MAIN_CARGO_DOOR_SWITCH"] = 1;
        DoorReadsOpen70();
        Set("A300_MAIN_CARGO_DOOR_SWITCH", 0);
        Set("A300_MAIN_CARGO_DOOR_SWITCH", 1);   // stopped part way
        ReleaseAllWaits();
        Assert.Equal(new[] { "Cargo door open 70 degrees" }, _speech.All);
    }

    [Fact]
    public void The_crossbleed_reads_open_or_closed_and_its_mode_auto_or_manual()
    {
        var vars = _def.GetVariables();
        Assert.Equal(new[] { "Closed", "Open" }, vars["A300_AIR_XFEED"].ValueDescriptions!.Values);
        Assert.Equal(new[] { "Auto", "Manual" }, vars["A300_AIR_XFEED_AUTO"].ValueDescriptions!.Values);
    }

    [Fact]
    public void A_switch_in_an_unknown_position_is_refused_aloud_and_snapped_back()
    {
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Empty(_sent);
        // The PMDGs' words for a switch whose state is not read yet.
        Assert.Equal(new[] { "Switch not ready, please try again in a moment." }, _speech.All);
        Assert.Equal(new[] { "A300_BATT_1" }, _reReads);
    }

    [Fact]
    public void A_write_that_cannot_land_is_refused_aloud()
    {
        _def.CanLand = _ => false;
        _cache["A300_BATT_1"] = 0;
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Battery 1 unavailable" }, _speech.All);
    }

    [Fact]
    public void A_hold_button_is_released_after_its_hold()
    {
        Assert.True(Set("A300_ECAM_ENG", 1));
        Assert.Equal(new[] { "2 (>B:AIRLINER_ECAM_ENG_Set)" }, _sent);
        _waits.Single().SetResult();
        Assert.Equal(new[] { "2 (>B:AIRLINER_ECAM_ENG_Set)", "0 (>B:AIRLINER_ECAM_ENG_Set)" }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Disposing_mid_hold_releases_the_button_at_once()
    {
        _def.Attach(_sim);
        Assert.True(Set("A300_ECAM_ENG", 1));
        _def.Dispose();
        Assert.Equal(new[] { "2 (>B:AIRLINER_ECAM_ENG_Set)", "0 (>B:AIRLINER_ECAM_ENG_Set)" }, _sent);
        _waits.Single().SetResult();
        Assert.Equal(2, _sent.Count);   // the release is not sent twice
    }

    [Fact]
    public void An_encoder_row_steps_its_knob()
    {
        Assert.True(Set("A300_SPEED_KNOB#INC", 1));
        Assert.True(Set("A300_SPEED_KNOB#DEC", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_SPEED_KNOB_Set)", "-1 (>B:AIRLINER_SPEED_KNOB_Set)" }, _sent);
    }

    [Theory]
    [InlineData(A300Levers.FlapsKey, 2, "8192 (>K:FLAPS_SET)")]
    [InlineData(A300Levers.SpoilersArmKey, 1, "1 (>K:SPOILERS_ARM_SET)")]
    [InlineData(A300Levers.SpeedBrakeKey, 0.5, "8192 (>K:SPOILERS_SET)")]
    public void A_lever_is_written_with_the_stock_event(string key, double value, string rpn)
    {
        Assert.True(Set(key, value));
        Assert.Equal(new[] { rpn }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_lever_write_that_cannot_land_is_refused_aloud()
    {
        _def.CanLand = _ => false;
        Assert.True(Set(A300Levers.FlapsKey, 1));
        Assert.Equal(new[] { "Flaps lever unavailable" }, _speech.All);
        Assert.Equal(new[] { A300Levers.FlapsKey }, _reReads);
    }

    [Fact]
    public void The_master_warning_speaks_as_it_comes_on_and_not_as_it_goes_out()
    {
        Deliver(A300Announcements.MasterWarningKey, 0);   // baseline
        Deliver(A300Announcements.MasterWarningKey, 1);
        Deliver(A300Announcements.MasterWarningKey, 0);   // the pilot pressed it
        Deliver(A300Announcements.MasterWarningKey, 1);
        Assert.Equal(new[] { "Master warning", "Master warning" }, _speech.All);
    }

    [Fact]
    public void A_lever_moved_elsewhere_is_spoken_after_its_baseline()
    {
        Deliver(A300Levers.FlapsKey, 0);
        Deliver(A300Levers.FlapsKey, 2);
        Deliver(A300Announcements.ParkingBrakeKey, 1);
        Deliver(A300Announcements.ParkingBrakeKey, 0);
        Assert.Equal(new[] { "Flaps 15/15", "Parking brake released" }, _speech.All);
    }

    [Theory]
    [InlineData("A300_PITCH_TRIM_1", "Pitch trim 1 off")]
    [InlineData("A300_PITCH_TRIM_2", "Pitch trim 2 off")]
    [InlineData("A300_YAW_DAMPER_1", "Yaw damper 1 off")]
    [InlineData("A300_YAW_DAMPER_2", "Yaw damper 2 off")]
    public void A_sas_lever_that_drops_by_itself_is_spoken(string key, string phrase)
    {
        // A pitch trim lever picked on with no hydraulics engaged, then dropped by itself 2.1 s later (2026-10-10).
        // MainForm's echo wrap silences the engage, the pilot's own pick; the drop is the aircraft's.
        Deliver(key, 0);
        Deliver(key, 1);
        _speech.All.Clear();   // the real announcer drops this inside the echo wrap; SpeechCapture records all
        Deliver(key, 0);
        Assert.Equal(new[] { phrase }, _speech.All);
        Assert.False(_def.GetVariables()[key].ExcludeFromMonitorManager);   // Ctrl+M can mute it
    }

    [Fact]
    public void A_switch_position_is_consumed_silently()
    {
        Assert.True(_def.ProcessSimVarUpdate("A300_BATT_1", 1, _speech));
        Assert.Empty(_speech.All);
        Assert.False(_def.ProcessSimVarUpdate("A300_RO_FCU_SPEED", 250, _speech));   // a readout: MainForm's display path
    }

    [Fact]
    public void A_context_reset_makes_the_next_value_a_silent_baseline()
    {
        Deliver(A300Announcements.GearLeverKey, 1);
        _def.OnSimContextReset();
        Deliver(A300Announcements.GearLeverKey, 0);
        Deliver(A300Announcements.GearLeverKey, 1);
        Assert.Equal(new[] { "Gear lever down" }, _speech.All);
    }

    [Fact]
    public void Only_the_aircrafts_own_lvars_prove_it_has_published()
    {
        Assert.False(_def.IsAircraftOwned("A300_IN_A_EXTERNAL_POWER_ON_1_BOOL"));   // stock: the sim core writes it
        Assert.True(_def.IsAircraftOwned(A300LampBoard.AcPowerKey));
        Assert.False(_def.IsAircraftOwned(A300Levers.FlapsKey));
        Assert.True(_def.IsAircraftOwned(A300Levers.SpoilersArmKey));
    }

    private Md11SeedGate SeedGate() =>
        (Md11SeedGate)typeof(IniA300Definition).GetField("_seedGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_def)!;

    [Theory]
    // After a flight load the sim core writes the stock SimVars before the aircraft's module has published anything,
    // so only a change of the aircraft's own L:var counts as the module being up. Every NoteValue site is here.
    [InlineData("A300_IN_A_EXTERNAL_POWER_ON_1_BOOL", false)]   // the light board: a lamp's stock input
    [InlineData("A300_LT_GEAR_2_UNLK_SEQ2_LIGHT", false)]       // the light board: the nose gear light, stock GEAR POSITION:0
    [InlineData(A300LampBoard.AcPowerKey, true)]                // the light board: an L:var
    [InlineData(A300Levers.FlapsKey, false)]                    // the announced keys: the stock flap handle index
    [InlineData(A300Levers.SpoilersArmKey, true)]               // the announced keys: an L:var that is not a lamp
    [InlineData("A300_PITCH_TRIM_1", true)]                     // the announced keys: a SAS lever, L:INI_pitch_trim1
    [InlineData(A300Announcements.MasterWarningKey, true)]      // the announced keys: a master light
    [InlineData(A300Readouts.AltitudeKey, true)]                // the altitude window
    [InlineData(A300TakeoffCallouts.V1Key, true)]               // the V1 read-back
    public void After_a_context_reset_only_a_change_of_the_aircrafts_own_lvar_counts_as_it_publishing(string key, bool counts)
    {
        var gate = SeedGate();
        _def.OnSimContextReset();
        Assert.True(gate.Armed);
        Deliver(key, 1);
        Assert.Equal(counts, gate.SawChange);
    }

    [Fact]
    public void The_glideslope_call_out_listens_to_the_ils_receiver()
    {
        Assert.Equal("NAV HAS GLIDE SLOPE:3", _def.GetVariables()["MON_GlideSlopeAlive"].Name);
        Assert.Equal("NAV HAS GLIDE SLOPE:1", new FlyByWireA320Definition().GetVariables()["MON_GlideSlopeAlive"].Name);
    }

    /// <summary>Reaches the protected base variables, whose keys the A300 does not own.</summary>
    private sealed class BaseKeysProbe : IniA300Definition
    {
        public HashSet<string> BaseKeys() => GetBaseVariables().Keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>One key from each family of silent subscription: the clocks, the rudder trim display, the VHF and ADF
    /// windows and the ADF signal, the cargo door's flags and angle button, the memo inputs and line, the MCDU lights,
    /// the IRS flags and the ILS inputs. A composed-line input that reads another key's cache (the first memo input, the
    /// captain's MSG light, the aligning flag, the nav selector) is not a subscription, so each of those families is
    /// represented by an input nothing else streams.</summary>
    private static string[] SilentFamilies() => new[]
    {
        A300Clock.Digits[0].Key, A300Trim.RudderDigitKeys[0], A300Radios.All[0].Window1Key,
        A300Radios.BearingLines.Values.First().SignalKey, A300CargoDoor.Flags[0].Key,
        A300CargoDoor.AngleButtons.Values.First().Key, A300EwdMemos.Inputs.Single(i => i.Var == "INI_PARKING_BRAKE_STATUS").Key,
        A300EwdMemos.LineKey, A300McduLights.For(A300McduUnit.Captain).Single(l => l.Label == "FAIL").Key,
        A300IrsAlignment.All[0].AlignedKey, A300IlsDeviation.LocalizerValidKey,
    };

    [Fact]
    public void Every_silent_subscription_is_consumed_without_a_word()
    {
        // The A300's own subscriptions with no Ctrl+M row (Continuous, announced, left out of the monitor manager) are
        // read only for a label, a box line or a refusal: two deliveries, a baseline and then a change, are consumed in
        // ProcessSimVarUpdate and say nothing. The base class's variables speak through MainForm by design, so only the
        // keys the A300 adds are walked.
        using var probe = new BaseKeysProbe();
        var baseKeys = probe.BaseKeys();
        var silent = _def.GetVariables()
            .Where(kv => !baseKeys.Contains(kv.Key)
                         && kv.Value is { UpdateFrequency: UpdateFrequency.Continuous, IsAnnounced: true, ExcludeFromMonitorManager: true })
            .Select(kv => kv.Key)
            .ToList();
        Assert.All(SilentFamilies(), key => Assert.Contains(key, silent));   // the walk covers every family, not an empty filter
        var unconsumed = new List<string>();
        foreach (var key in silent)
        {
            bool baseline = _def.ProcessSimVarUpdate(key, 0, _speech);
            bool change = _def.ProcessSimVarUpdate(key, 1, _speech);
            if (!baseline || !change)
                unconsumed.Add(key);
        }
        Assert.Empty(unconsumed);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_silent_subscription_is_consumed_when_its_delivery_is_the_first_thing_that_reaches_the_definition()
    {
        // A delivery can arrive before anything asked for the variables (several tests deliver first): the silent keys
        // are known by building them, not by whoever happened to ask first.
        var fresh = new IniA300Definition();
        foreach (var key in SilentFamilies())
            Assert.True(fresh.ProcessSimVarUpdate(key, 1, _speech), key);
        Assert.Empty(_speech.All);
    }

    /// <summary>A variable a composed line reads from the cache: its key, its variable and, for a stock one, its unit.</summary>
    private readonly record struct ComposedInput(string Key, string Var, bool IsStock, string Units);

    /// <summary>The inputs of the composed lines, read from the cache only: the E/WD memos' inputs, the IRS alignment
    /// lines' flags, the ILS deviation lines' EFIS nav selector and the MCDU annunciators.</summary>
    private static IEnumerable<ComposedInput> ComposedLineInputs() =>
        A300EwdMemos.Inputs.Select(i => new ComposedInput(i.Key, i.Var, i.IsStock, i.Units))
            .Concat(A300IrsAlignment.All.SelectMany(l => new[]
            {
                new ComposedInput(l.AligningKey, l.AligningVar, false, "number"),
                new ComposedInput(l.AlignedKey, l.AlignedVar, false, "number"),
            }))
            .Append(new ComposedInput(A300IlsDeviation.NavSelectorKey, A300IlsDeviation.NavSelectorVar, false, "number"))
            .Concat(A300McduLights.All.Select(l => new ComposedInput(l.Key, l.Var, false, "number")));

    [Fact]
    public void No_composed_line_input_restreams_a_variable_already_streamed()
    {
        // A memo, an IRS line, the ILS deviation line and an MCDU annunciator read the cache, so an input whose variable
        // another key already streams reads that key's cache instead of a second subscription of its own. The light
        // board's inputs, the panel rows and the keys that carry a Ctrl+M row keep theirs: their own deliveries drive
        // something.
        var vars = _def.GetVariables();
        var registered = ComposedLineInputs().Select(i => i.Key).Where(vars.ContainsKey).ToList();
        Assert.NotEmpty(registered);   // the inputs nothing else streams (the total air temperature, say) keep their own
        string FullName(string key) => ContinuousBatchLayout.FullName(vars[key]);
        var restreamed = registered
            .Select(key => (Key: key, Others: vars.Keys
                .Where(other => other != key && vars[other].UpdateFrequency == UpdateFrequency.Continuous
                                && string.Equals(FullName(other), FullName(key), StringComparison.Ordinal))   // as InputSubscription matches
                .ToList()))
            .Where(x => x.Others.Count > 0)
            .Select(x => $"{x.Key} re-streams {FullName(x.Key)}, which {string.Join(", ", x.Others)} also stream")
            .ToList();
        Assert.True(restreamed.Count == 0, string.Join(Environment.NewLine, restreamed));
    }

    [Fact]
    public void An_input_that_reads_another_keys_cache_is_neither_registered_nor_silent()
    {
        // The key it reads streams the very same variable, once a second and into the same cache, and the input itself is
        // registered nowhere, so it is not in the silent set either (an input that reads another key leaves the set by itself).
        var vars = _def.GetVariables();
        var aliased = ComposedLineInputs().Where(i => _def.StreamingKey(i.Key) != i.Key).ToList();
        Assert.NotEmpty(aliased);   // the memos', the IRS and the nav selector inputs, and the captain's MSG light
        foreach (var input in aliased)
        {
            string streaming = _def.StreamingKey(input.Key);
            Assert.DoesNotContain(input.Key, vars.Keys);
            Assert.DoesNotContain(input.Key, _def.SilentKeys);
            var def = vars[streaming];
            Assert.True(def.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced, $"{input.Key} reads {streaming}");
            Assert.Equal((input.IsStock ? SimVarType.SimVar : SimVarType.LVar, input.Var), (def.Type, def.Name));
            Assert.Equal(input.Units, def.Units, ignoreCase: true);   // a stock variable's unit, and an L:var's "number"
        }
        // What keeps its own subscription: a light board input, a panel row and a key with a Ctrl+M row are never the input
        // that reads another key, and the memo line keeps its key (the first memo input reads it).
        var rows = _def.GetPanelControls().Values.SelectMany(keys => keys).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(aliased, i => A300LampBoard.InputKeys.Contains(i.Key) || rows.Contains(i.Key));
        Assert.Equal(A300EwdMemos.LineKey, _def.StreamingKey(A300EwdMemos.Inputs[0].Key));
        Assert.Contains(A300EwdMemos.LineKey, vars.Keys);
    }

    [Fact]
    public void An_input_reads_another_key_only_when_it_streams_the_same_variable_in_the_same_unit()
    {
        // Name, type and unit all have to match, and the name exactly: a name that differs only in case may be another
        // variable, and a variable streamed in another unit ("Bool", say) is not the value the input's readers expect. A
        // definition that is not streamed (on request, or not announced) fills no cache of its own.
        static SimVarDefinition Stream(string name, SimVarType type = SimVarType.LVar, string units = "number",
            UpdateFrequency frequency = UpdateFrequency.Continuous, bool announced = true) => new()
        {
            Name = name, Type = type, Units = units, UpdateFrequency = frequency, IsAnnounced = announced,
        };
        using var def = new IniA300Definition();
        var vars = new Dictionary<string, SimVarDefinition>
        {
            ["SAME"] = Stream("INI_X"),
            ["OTHER_UNIT"] = Stream("INI_U", units: "Bool"),
            ["OTHER_CASE"] = Stream("INI_c_lower"),
            ["STOCK"] = Stream("EXTERNAL POWER ON:1", SimVarType.SimVar, "Bool"),
            ["STOCK_OTHER_UNIT"] = Stream("SPOILERS HANDLE POSITION", SimVarType.SimVar, "Percent"),
            ["ON_REQUEST"] = Stream("INI_R", frequency: UpdateFrequency.OnRequest),
            ["NOT_ANNOUNCED"] = Stream("INI_N", announced: false),
        };

        Assert.Null(def.InputSubscription(vars, "IN_SAME", "INI_X"));
        Assert.Null(def.InputSubscription(vars, "IN_STOCK", "EXTERNAL POWER ON:1", isStock: true, units: "bool"));   // a unit's case does not matter
        Assert.NotNull(def.InputSubscription(vars, "IN_OTHER_UNIT", "INI_U"));
        Assert.NotNull(def.InputSubscription(vars, "IN_OTHER_CASE", "INI_C_LOWER"));
        Assert.NotNull(def.InputSubscription(vars, "IN_STOCK_OTHER_UNIT", "SPOILERS HANDLE POSITION", isStock: true, units: "Number"));
        Assert.NotNull(def.InputSubscription(vars, "IN_ON_REQUEST", "INI_R"));
        Assert.NotNull(def.InputSubscription(vars, "IN_NOT_ANNOUNCED", "INI_N"));
        Assert.DoesNotContain("IN_SAME", vars.Keys);
        Assert.DoesNotContain("IN_STOCK", vars.Keys);
        Assert.All(new[] { "IN_OTHER_UNIT", "IN_OTHER_CASE", "IN_STOCK_OTHER_UNIT", "IN_ON_REQUEST", "IN_NOT_ANNOUNCED" },
            key => Assert.Contains(key, vars.Keys));
    }

    [Fact]
    public void Every_key_the_composed_lines_ask_the_cache_for_is_registered()
    {
        // A reader of an input that another key streams must go through ReadInput. Written as Cached(sim, irs.AligningKey),
        // it would ask for a key nothing registers or delivers, and read null for good. So record every key the composed
        // lines ask the cache for (a memo sample, each IRS and ILS line, both units' MCDU annunciators) and require each to
        // be registered. Every question is answered with 1, so no reader stops at an unread input, and the AC light power
        // reads on, so the annunciators ask for their lights.
        var asked = new HashSet<string>(StringComparer.Ordinal);
        _def.Cached = (_, key) =>
        {
            asked.Add(key);
            return 1;
        };
        _def.Attach(_sim);
        _def.OnDeferredFlushBatchDelivered(_speech);   // the memo sample; the FMA's sources, composed in the same call, are asked too
        foreach (var irs in A300IrsAlignment.All)
            _def.TryGetDisplayOverride(irs.LineKey, 0, out _);
        _def.TryGetDisplayOverride(A300IlsDeviation.LocalizerKey, 0, out _);
        _def.TryGetDisplayOverride(A300IlsDeviation.GlideslopeKey, 0, out _);
        _def.McduAnnunciators(A300McduUnit.Captain);
        _def.McduAnnunciators(A300McduUnit.FirstOfficer);

        var vars = _def.GetVariables();
        var unregistered = asked.Where(key => !vars.ContainsKey(key)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unregistered.Count == 0, "asked the cache for keys nothing registers: " + string.Join(", ", unregistered));
        // The recording is not empty: every composed-line input was asked for, under the key that streams it.
        Assert.All(ComposedLineInputs(), input => Assert.Contains(_def.StreamingKey(input.Key), asked));
    }

    [Fact]
    public void Every_silent_key_is_still_registered_as_a_silent_subscription()
    {
        // The set holds exactly what OwnSubscription registered without a Ctrl+M row. A later registration over one of those
        // keys (a row that speaks, say) would leave ProcessSimVarUpdate consuming it in silence.
        var vars = _def.GetVariables();
        Assert.NotEmpty(_def.SilentKeys);
        foreach (var key in _def.SilentKeys)
        {
            Assert.True(vars.TryGetValue(key, out var def), $"{key} is in the silent set but not registered");
            Assert.True(def!.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced && def.ExcludeFromBatch
                        && def.ExcludeFromMonitorManager, $"{key} is in the silent set but is not a silent subscription");
        }
    }

    [Fact]
    public void A_state_list_that_names_an_aliased_key_names_the_key_that_streams_it()
    {
        // A label or status line that reads an input another key streams repaints when that key is delivered: the key the
        // sim never delivers under is replaced, once, and a key that was not aliased stays.
        var aliases = new Dictionary<string, string> { ["INPUT_A"] = "STREAMING_A", ["INPUT_B"] = "STREAMING_A" };
        var lists = new[]
        {
            new SimVarDefinition { StateVariables = new[] { "OWN", "INPUT_A", "INPUT_B", "OTHER" } },
            new SimVarDefinition { StateVariables = new[] { "OWN", "OTHER" } },
            new SimVarDefinition(),
        };
        var untouched = lists[1].StateVariables;
        IniA300Definition.RepointStateVariables(lists, aliases);
        Assert.Equal(new[] { "OWN", "STREAMING_A", "OTHER" }, lists[0].StateVariables);
        Assert.Same(untouched, lists[1].StateVariables);
        Assert.Null(lists[2].StateVariables);
    }

    [Fact]
    public void Readers_that_need_only_a_threshold_ignore_small_changes()
    {
        // [SIM-7]. Three variables are only ever compared with a threshold, by readers of the cache (which takes every
        // sample whatever the tolerance): the memo for TAT in icing range (-15 to 5 Celsius), the memo for an IRS in
        // align (its time remaining against zero) and the ADF bearing line (the signal against zero). A move inside half
        // a unit is no change to them, so it does not wake MainForm once a second; a real step still does.
        var vars = _def.GetVariables();
        var thresholdKeys = new[] { A300EwdMemos.Inputs.Single(i => i.Var == "TOTAL AIR TEMPERATURE").Key }
            .Concat(A300EwdMemos.Inputs.Where(i => i.Var.EndsWith("_TIME_REMAIN", StringComparison.Ordinal)).Select(i => i.Key))
            .Concat(A300Radios.BearingLines.Values.Select(b => b.SignalKey))
            .ToList();
        Assert.Equal(6, thresholdKeys.Count);   // TAT, three IRS, two ADF
        foreach (var key in thresholdKeys)
        {
            var def = vars[key];
            Assert.Equal(0.5, def.ChangeTolerance);
            Assert.False(SimConnectManager.IsValueChange(10.0, 10.4, def), key);
            Assert.True(SimConnectManager.IsValueChange(10.0, 10.6, def), key);
        }
        // Widening another variable is a decision about its readers: make it here, deliberately.
        using var probe = new BaseKeysProbe();
        var baseKeys = probe.BaseKeys();
        var widened = vars.Where(kv => kv.Value.ChangeTolerance != null && !baseKeys.Contains(kv.Key)).Select(kv => kv.Key);
        Assert.Equal(thresholdKeys.Order(StringComparer.Ordinal), widened.Order(StringComparer.Ordinal));
        // Md11SeedGate compares at the shared constant, so a key it counts never widens its own.
        var counted = A300LampBoard.InputKeys.Concat(A300Announcements.AnnouncedKeys)
            .Concat(new[] { A300Readouts.AltitudeKey, A300TakeoffCallouts.V1Key, A300TakeoffCallouts.VrKey, A300TakeoffCallouts.V2Key });
        Assert.Empty(thresholdKeys.Intersect(counted));
    }

    [Fact]
    public void An_adf_signal_inside_the_tolerance_is_still_a_signal()
    {
        // The bearing line compares the signal with zero only, from the cache: 0.3 is a signal whatever the tolerance does
        // with a move that small, and a signal lost to exactly 0 reads "no signal".
        var (signalKey, _) = A300Radios.BearingLines["A300_RO_ADF1_BEARING"];
        _def.Attach(_sim);
        Assert.False(SimConnectManager.IsValueChange(0, 0.3, _def.GetVariables()[signalKey]));
        _cache[signalKey] = 0.3;
        Assert.True(_def.TryGetDisplayOverride("A300_RO_ADF1_BEARING", 45, out var received));
        Assert.Equal("45 degrees right", received);
        _cache[signalKey] = 0;
        Assert.True(_def.TryGetDisplayOverride("A300_RO_ADF1_BEARING", 45, out var lost));
        Assert.Equal("no signal", lost);
    }
}
