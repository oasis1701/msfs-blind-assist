using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The E/WD memos, transcribed from the A300's own EWD code (v1.0.11, 2026-10-10): the twenty
/// <c>Message_MemoN::is_active</c> conditions, shown in their fixed order once active for 2.5 s
/// (<c>EWD::UpdateMemo</c>).
/// </summary>
public class A300EwdMemosTests
{
    private static Func<string, double?> Reading(params (string Var, double Value)[] set)
    {
        // Every input known and at rest; the named ones as given.
        var values = A300EwdMemos.Inputs.ToDictionary(i => i.Var, i => i.Var switch
        {
            "INI_LANDING_LIGHT_L_SWITCH" or "INI_LANDING_LIGHT_R_SWITCH" => 2.0,   // retracted
            "INI_tcas_mode_pedestal" => 2.0,                                     // TA/RA
            "TOTAL AIR TEMPERATURE" => 20.0,
            _ => 0.0,
        });
        foreach (var (var, value) in set)
            values[var] = value;
        var byKey = A300EwdMemos.Inputs.ToDictionary(i => i.Key, i => values[i.Var]);
        return key => byKey.TryGetValue(key, out var v) ? v : null;
    }

    private static string[] ActiveWords(Func<string, double?> read) =>
        A300EwdMemos.All.Where(m => m.IsActive(read) == true).Select(m => m.Words).ToArray();

    [Fact]
    public void The_twenty_memos_are_in_the_aircrafts_order_with_its_texts()
    {
        Assert.Equal(new[]
        {
            "CAB PRESS MAN CTL", "TAT IN ICING RANGE", "APU RUNNING", "CONTINUOUS RELIGHT ON", "FUEL X FEED",
            "EXT PWR CONNECTED", "PARKING BRAKE ON", "SEAT BELTS ON", "NO SMOKING ON", "FUEL FEED MAN CTL",
            "SPD BRAKES EXTENDED", "LDG LIGHT EXTENDED", "ECON FLOW SELECTED", "MAX COOL ON", "EMER CANCEL ON",
            "ENG ANTI ICE ON", "WING ANTI ICE ON", "CTR TANK FEEDING", "IRS IN ALIGN", "TCAS STBY",
        }, A300EwdMemos.All.Select(m => m.Text));
    }

    [Fact]
    public void Nothing_is_active_at_rest() => Assert.Empty(ActiveWords(Reading()));

    [Theory]
    [InlineData("INI_CABIN_MAN_PRESS_ARROW", 1, "Cabin pressure manual control")]
    [InlineData("INI_apu_available", 1, "APU running")]
    [InlineData("INI_eng_ignition_switch", 4, "Continuous relight on")]
    [InlineData("INI_xfeed_transfer_on", 1, "Fuel crossfeed")]
    [InlineData("EXTERNAL POWER ON:1", 1, "External power connected")]
    [InlineData("INI_PARKING_BRAKE_STATUS", 1, "Parking brake on")]
    [InlineData("INI_SEATBELTS_SWITCH", 1, "Seat belts on")]
    [InlineData("INI_NO_SMOKING_ON", 1, "No smoking on")]
    [InlineData("INI_FUEL_FEED_MAN_CTL", 1, "Fuel feed manual control")]
    [InlineData("SPOILERS HANDLE POSITION", 3, "Speed brakes extended")]
    [InlineData("INI_LANDING_LIGHT_L_SWITCH", 1, "Landing light extended")]
    [InlineData("INI_LANDING_LIGHT_R_SWITCH", 0, "Landing light extended")]
    [InlineData("INI_econ_flow_selected", 1, "Economy flow selected")]
    [InlineData("INI_max_cool", 1, "Max cool on")]
    [InlineData("INI_EMER_CANCEL", 1, "Emergency cancel on")]
    [InlineData("INI_ENG1_ANTI_ICE", 1, "Engine anti ice on")]
    [InlineData("INI_ENG2_ANTI_ICE", 1, "Engine anti ice on")]
    [InlineData("INI_WING_ANTI_ICE", 1, "Wing anti ice on")]
    [InlineData("INI_CENTER_TANK_IS_FEEDING", 1, "Center tank feeding")]
    [InlineData("INI_tcas_mode_pedestal", 1, "TCAS standby")]
    [InlineData("INI_tcas_mode_pedestal", 0, "TCAS standby")]
    public void Each_memo_shows_on_its_own_condition(string var, double value, string words) =>
        Assert.Equal(new[] { words }, ActiveWords(Reading((var, value))));

    [Theory]
    [InlineData(-15, true)]
    [InlineData(5, true)]
    [InlineData(-15.5, false)]
    [InlineData(5.5, false)]
    public void Tat_in_icing_range_is_minus_15_to_5(double tat, bool shown) =>
        Assert.Equal(shown, ActiveWords(Reading(("TOTAL AIR TEMPERATURE", tat))).Contains("TAT in icing range"));

    [Theory]
    // An IRS aligning with time remaining; an IRS whose time has run out, or not aligning, is not.
    [InlineData("INI_IRS3_IS_ALIGNING", "INI_IRS3_TIME_REMAIN", 1, 120, true)]
    [InlineData("INI_IRS1_IS_ALIGNING", "INI_IRS1_TIME_REMAIN", 1, 0, false)]
    [InlineData("INI_IRS2_IS_ALIGNING", "INI_IRS2_TIME_REMAIN", 0, 300, false)]
    public void Irs_in_align_needs_an_irs_aligning_with_time_left(string aligning, string remain, double a, double t, bool shown) =>
        Assert.Equal(shown, ActiveWords(Reading((aligning, a), (remain, t))).Contains("IRS in align"));

    [Fact]
    public void An_unread_input_makes_its_memo_unknown()
    {
        var read = Reading();
        Func<string, double?> partial = key => key == A300EwdMemos.Inputs.First(i => i.Var == "INI_apu_available").Key ? null : read(key);
        Assert.Null(A300EwdMemos.All.Single(m => m.Text == "APU RUNNING").IsActive(partial));
    }

    [Fact]
    public void A_memo_shows_once_active_for_two_and_a_half_seconds()
    {
        var tracker = new A300MemoTracker();
        var off = Reading();
        var on = Reading(("INI_apu_available", 1));
        tracker.Update(off, 0);   // the baseline: nothing active
        Assert.Empty(tracker.Update(on, 1000).Shown);
        Assert.Empty(tracker.Update(on, 3000).Shown);       // 2 s
        var update = tracker.Update(on, 3600);               // 2.6 s
        Assert.Equal(new[] { "APU running" }, update.Shown.Select(m => m.Words));
        Assert.Equal(new[] { "APU running" }, update.Displayed.Select(m => m.Words));
        Assert.Empty(tracker.Update(on, 4600).Shown);       // once
        Assert.Empty(tracker.Update(off, 5600).Displayed);
    }

    [Fact]
    public void A_memo_that_drops_out_starts_its_wait_again()
    {
        var tracker = new A300MemoTracker();
        var on = Reading(("INI_SEATBELTS_SWITCH", 1));
        tracker.Update(Reading(), 0);
        tracker.Update(on, 1000);
        tracker.Update(Reading(), 2000);
        tracker.Update(on, 3000);
        Assert.Empty(tracker.Update(on, 4000).Shown);   // 1 s since it came back
        Assert.Single(tracker.Update(on, 6000).Shown);
    }

    [Fact]
    public void Memos_active_at_the_first_complete_reading_are_its_baseline()
    {
        // A flight load shows its memos without their being changes: none is spoken.
        var tracker = new A300MemoTracker();
        var loaded = Reading(("INI_PARKING_BRAKE_STATUS", 1), ("EXTERNAL POWER ON:1", 1));
        var first = tracker.Update(loaded, 0);
        Assert.Empty(first.Shown);
        Assert.Equal(new[] { "External power connected", "Parking brake on" }, first.Displayed.Select(m => m.Words));
        Assert.Empty(tracker.Update(loaded, 5000).Shown);
        tracker.Reset();
        Assert.Empty(tracker.Update(loaded, 6000).Shown);
    }

    [Fact]
    public void The_baseline_waits_for_every_input()
    {
        var tracker = new A300MemoTracker();
        Assert.Empty(tracker.Update(_ => null, 0).Displayed);
        var on = Reading(("INI_apu_available", 1));
        Assert.Empty(tracker.Update(on, 1000).Shown);   // the first complete reading: the baseline
        Assert.Empty(tracker.Update(on, 9000).Shown);
    }

    [Theory]
    [InlineData(new string[0], "none")]
    [InlineData(new[] { "APU running" }, "APU running")]
    [InlineData(new[] { "APU running", "Seat belts on" }, "APU running, Seat belts on")]
    public void The_line_lists_the_memos_shown(string[] words, string line) =>
        Assert.Equal(line, A300EwdMemos.Line(words));

}

/// <summary>How the A300 definition carries the memos: their inputs read from the cache, each on its own subscription
/// (consumed silently) or, where another key already streams the variable, from that key; read at the end of each sample
/// (where the FMA is composed) and shown on the ECAM Memos box, never spoken.</summary>
public class IniA300MemoBehaviourTests
{
    private long _now = 10_000;
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly Dictionary<string, double> _cache = new();
    private readonly HashSet<string> _muted = new();

    public IniA300MemoBehaviourTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = key => _muted.Contains(key),
        };
        _def.Attach(new SimConnectManager(IntPtr.Zero));
        foreach (var input in A300EwdMemos.Inputs)
            _cache[_def.StreamingKey(input.Key)] = input.Var switch
            {
                "INI_LANDING_LIGHT_L_SWITCH" or "INI_LANDING_LIGHT_R_SWITCH" or "INI_tcas_mode_pedestal" => 2,
                "TOTAL AIR TEMPERATURE" => 20,
                _ => 0,
            };
    }

    /// <summary>A memo input's value, in the cache under the key that streams its variable.</summary>
    private void Input(string var, double value) =>
        _cache[_def.StreamingKey(A300EwdMemos.Inputs.Single(i => i.Var == var).Key)] = value;

    private string[] Sample(long advanceMs = 1000)
    {
        _now += advanceMs;
        _speech.All.Clear();
        _def.OnDeferredFlushBatchDelivered(_speech);
        return _speech.All.Where(s => s.StartsWith("Memo", StringComparison.Ordinal)).ToArray();
    }

    private string BoxLine() =>
        _def.TryGetDisplayOverride(A300EwdMemos.LineKey, 0, out var text) ? text : "(no line)";

    [Fact]
    public void Every_input_is_read_from_one_registered_key()
    {
        // An input nothing else streams has a subscription of its own, with no Ctrl+M row; one whose variable a panel row, a
        // lamp, the light board's external power input or the memo line already streams has none, and its readers ask that key.
        var vars = _def.GetVariables();
        foreach (var input in A300EwdMemos.Inputs)
        {
            string streaming = _def.StreamingKey(input.Key);
            var def = vars[streaming];
            Assert.Equal(input.Var, def.Name);
            Assert.Equal(input.IsStock ? SimVarType.SimVar : SimVarType.LVar, def.Type);
            Assert.True(def.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced, input.Key);
            if (streaming == input.Key)
                Assert.True(def.ExcludeFromBatch && def.ExcludeFromMonitorManager, input.Key);
            else
                Assert.DoesNotContain(input.Key, vars.Keys);
        }
        // A tolerance belongs to the registered key, so the inputs that carry one keep a subscription of their own.
        Assert.All(A300EwdMemos.Inputs.Where(i => i.ChangeTolerance != null), i => Assert.Equal(i.Key, _def.StreamingKey(i.Key)));
    }

    [Fact]
    public void A_memo_input_with_a_subscription_of_its_own_is_consumed_without_a_word()
    {
        // The keys that stream the other inputs are panel rows, lamps, the light board's input and the memo line; each is
        // consumed by its own branch (a lamp speaks later, at a batch end). Only an input with a subscription of its own is
        // the memos' to consume.
        var own = A300EwdMemos.Inputs.Where(i => _def.StreamingKey(i.Key) == i.Key).ToList();
        Assert.NotEmpty(own);
        foreach (var input in own)
            Assert.True(_def.ProcessSimVarUpdate(input.Key, 1, _speech), input.Key);
        Assert.Empty(_speech.All);
    }

    [Theory]
    // A panel row, a lamp, the light board's external power input and the memo line stream these variables already.
    [InlineData("INI_SEATBELTS_SWITCH", "A300_SEATBELT")]
    [InlineData("INI_ENG1_ANTI_ICE", "A300_ANTI_ICE_E1")]
    [InlineData("INI_tcas_mode_pedestal", "A300_TCAS_MODE")]
    [InlineData("INI_apu_available", "A300_LT_APU_AVAIL_SEQ1_LIGHT")]
    [InlineData("EXTERNAL POWER ON:1", "A300_IN_A_EXTERNAL_POWER_ON_1_BOOL")]
    [InlineData("INI_IRS3_IS_ALIGNING", "A300_LT_IRS_2_009_LIGHT")]
    [InlineData("INI_CABIN_MAN_PRESS_ARROW", A300EwdMemos.LineKey)]
    public void An_input_another_key_streams_has_no_subscription_of_its_own(string var, string streaming)
    {
        var input = A300EwdMemos.Inputs.Single(i => i.Var == var);
        Assert.Equal(streaming, _def.StreamingKey(input.Key));
        Assert.DoesNotContain(input.Key, _def.GetVariables().Keys);
        Assert.Equal(var, _def.GetVariables()[streaming].Name);
    }

    [Fact]
    public void A_memo_follows_the_cache_of_the_key_that_streams_its_input()
    {
        Assert.Empty(Sample());   // the baseline
        _cache["A300_SEATBELT"] = 1;   // the panel row's key: the sim never delivers anything under the memo input's own
        Assert.Empty(Sample());
        Assert.Empty(Sample(3000));
        Assert.Equal("Seat belts on", BoxLine());
    }

    [Fact]
    public void A_threshold_crossed_inside_the_tolerance_still_moves_the_memo()
    {
        // [SIM-7]: a sample that moves by less than the tolerance fires no change, but the cache takes it, and the memo
        // reads the cache at the end of the sample, so the top of the icing range (5 Celsius) is still crossed.
        var tat = A300EwdMemos.Inputs.Single(i => i.Var == "TOTAL AIR TEMPERATURE");
        Assert.False(SimConnectManager.IsValueChange(4.9, 5.2, _def.GetVariables()[tat.Key]));   // no change fires for this move
        Assert.Empty(Sample());
        Input(tat.Var, 4.9);
        Sample();
        Sample(3000);
        Assert.Equal("TAT in icing range", BoxLine());
        Input(tat.Var, 5.2);
        Sample();
        Assert.Equal("none", BoxLine());
    }

    [Fact]
    public void An_irs_time_of_a_fraction_of_a_second_is_still_time_left()
    {
        // The time remaining is compared with zero only (memo 19): 0.3 s is not zero, whatever the tolerance does with it.
        var remain = A300EwdMemos.Inputs.Single(i => i.Var == "INI_IRS1_TIME_REMAIN");
        Assert.False(SimConnectManager.IsValueChange(0, 0.3, _def.GetVariables()[remain.Key]));
        Assert.Empty(Sample());
        Input("INI_IRS1_IS_ALIGNING", 1);
        Input("INI_IRS1_TIME_REMAIN", 0.3);
        Sample();
        Sample(3000);
        Assert.Equal("IRS in align", BoxLine());
    }

    [Fact]
    public void The_memo_line_is_on_the_ecam_memos_box_with_no_ctrl_m_row()
    {
        // Memos are never spoken (design decision, 2026-10-11), so a row would mute nothing ([VAR-9]).
        var line = _def.GetVariables()[A300EwdMemos.LineKey];
        Assert.True(line.ExcludeFromMonitorManager);
        Assert.True(line.RenderAsReadOnlyStatus);
        Assert.Contains(A300EwdMemos.LineKey, _def.GetPanelDisplayVariables()[A300EwdMemos.Panel]);
        Assert.Contains(A300EwdMemos.Panel, _def.GetPanelStructure()[A300DisplayPanels.Section]);
        Assert.True(_def.ProcessSimVarUpdate(A300EwdMemos.LineKey, 1, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_memo_appearing_is_shown_and_never_spoken()
    {
        // Design decision, 2026-10-11: a sign or light gives its switch read-back only, as on the Fenix, the PMDGs, the
        // iFly and the MD-11; "Memo: Seat belts on" a few seconds later repeated the switch.
        Assert.Empty(Sample());   // the baseline
        Assert.Equal("none", BoxLine());
        Input("INI_SEATBELTS_SWITCH", 1);
        Input("INI_apu_available", 1);
        Assert.Empty(Sample());
        Assert.Empty(Sample(2000));
        Assert.Empty(Sample());
        Assert.Empty(Sample(5000));
        Assert.Equal("APU running, Seat belts on", BoxLine());
        Input("INI_SEATBELTS_SWITCH", 0);
        Assert.Empty(Sample());
        Assert.Equal("APU running", BoxLine());
    }

    [Fact]
    public void Memos_at_load_are_the_baseline_of_the_box()
    {
        Input("INI_PARKING_BRAKE_STATUS", 1);
        Assert.Empty(Sample());
        Assert.Equal("Parking brake on", BoxLine());
    }

    [Fact]
    public void A_context_reset_makes_the_next_reading_a_baseline()
    {
        Sample();
        _def.OnSimContextReset();
        Input("INI_apu_available", 1);
        for (int i = 0; i < 8; i++)
            Assert.Empty(Sample());
    }
}
