using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300 definition's outside-change call-outs for the VHF frequencies and the squawk ([A300-29]): registered on
/// their own subscriptions with a Ctrl+M row each, consumed in silence, and spoken at a batch end only when MSFSBA did
/// not make the change, against a SimConnectManager that never connected, a fake cache and a fake clock.
/// </summary>
public class IniA300RadioWatchTests
{
    private static readonly A300Radio Vhf1 = A300Radios.Vhf[0];
    private static readonly A300Radio Vhf2 = A300Radios.Vhf[1];

    private const double W1 = 122800;   // in use while the transfer switch reads 0
    private const double W2 = 124805;

    private static double Code(string digits) => Convert.ToInt32(digits, 16);   // BCO16: each nibble a digit

    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly HashSet<string> _muted = new();
    private readonly List<string> _sent = new();
    private long _now = 10_000;

    public IniA300RadioWatchTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            TypedDelay = _ => Task.CompletedTask,
            IsMuted = key => _muted.Contains(key),
        };
        _def.Attach(_sim);
    }

    /// <summary>A delivery as the sim makes it: the cache holds the value, then MainForm hands it to the definition.</summary>
    private void Reading(string key, double value)
    {
        _cache[key] = value;
        _def.ProcessSimVarUpdate(key, value, _speech);
    }

    private void VhfReads(A300Radio radio, double window1, double window2, double transfer)
    {
        Reading(radio.Window1Key, window1);
        Reading(radio.Window2Key, window2);
        Reading(radio.TransferKey, transfer);
    }

    /// <summary>A second on, and the end of the batch the watches run on.</summary>
    private void BatchEnd(long advance = 1000)
    {
        _now += advance;
        _def.OnContinuousBatchDelivered(IniA300Definition.RadioWatchBatch);
    }

    /// <summary>A reading delivered at the given time on the fake clock.</summary>
    private void DeliverAt(long at, string key, double value)
    {
        _now = at;
        Reading(key, value);
    }

    /// <summary>The end of the batch the watches run on, at the given time on the fake clock.</summary>
    private void FlushAt(long at)
    {
        _now = at;
        _def.OnContinuousBatchDelivered(IniA300Definition.RadioWatchBatch);
    }

    /// <summary>Both VHF radios (122.800 in use, 124.805 standby) and the squawk (1200) read, then one batch end: the baselines.</summary>
    private void Baseline()
    {
        VhfReads(Vhf1, W1, W2, 0);
        VhfReads(Vhf2, W1, W2, 0);
        Reading(A300RadioWatches.SquawkKey, Code("1200"));
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    private void ReadFreshReturns(Dictionary<string, double> values)
    {
        _def.ReadFresh = (_, key, _) => Task.FromResult<double?>(values.TryGetValue(key, out var v) ? v : null);
    }

    private Md11SeedGate SeedGate() =>
        (Md11SeedGate)typeof(IniA300Definition).GetField("_seedGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_def)!;

    // ------------------------------------------------------------------------------------------------------------
    // Registration
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Each_vhf_radios_row_rides_its_transfer_switch_on_its_own_subscription()
    {
        var vars = _def.GetVariables();
        foreach (var radio in A300Radios.Vhf)
        {
            var def = vars[radio.TransferKey];
            Assert.Equal((radio.TransferVar, $"{radio.Name} frequencies", UpdateFrequency.Continuous, true, true, false),
                (def.Name, def.DisplayName, def.UpdateFrequency, def.IsAnnounced, def.ExcludeFromBatch, def.ExcludeFromMonitorManager));
            Assert.False(ContinuousBatchLayout.RidesBatch(def));   // [A300-9]: never a batch-covered variable
        }
    }

    [Fact]
    public void The_squawk_row_is_a_new_subscription_to_the_transponder_code_in_bco16()
    {
        var def = _def.GetVariables()[A300RadioWatches.SquawkKey];
        Assert.Equal(("TRANSPONDER CODE:1", SimVarType.SimVar, "BCO16", "Squawk"), (def.Name, def.Type, def.Units, def.DisplayName));
        Assert.Equal((UpdateFrequency.Continuous, true, true, false),
            (def.UpdateFrequency, def.IsAnnounced, def.ExcludeFromBatch, def.ExcludeFromMonitorManager));
        Assert.False(ContinuousBatchLayout.RidesBatch(def));
    }

    [Fact]
    public void No_watched_value_has_a_change_tolerance_every_step_matters()
    {
        // [SIM-7]: the default 0.001 passes a one-kilohertz step and a one-digit step of the code.
        var vars = _def.GetVariables();
        foreach (var key in A300RadioWatches.InputKeys)
            Assert.Null(vars[key].ChangeTolerance);
    }

    [Fact]
    public void The_rows_carriers_are_not_silent_keys_the_other_inputs_are()
    {
        var silent = _def.SilentKeys;
        foreach (var key in A300RadioWatches.RowKeys)
            Assert.DoesNotContain(key, silent);
        foreach (var radio in A300Radios.Vhf)
        {
            Assert.Contains(radio.Window1Key, silent);
            Assert.Contains(radio.Window2Key, silent);
        }
        // The ADF radios are not watched: all three of each keep their silent subscription.
        foreach (var radio in A300Radios.All.Where(r => !A300Radios.Vhf.Contains(r)))
        {
            Assert.All(new[] { radio.Window1Key, radio.Window2Key, radio.TransferKey }, key => Assert.Contains(key, silent));
            Assert.True(_def.GetVariables()[radio.TransferKey].ExcludeFromMonitorManager);
        }
    }

    [Fact]
    public void Ctrl_m_lists_the_three_rows_by_name()
    {
        var rows = MonitorRowBuilder.Build(_def.GetVariables()).ToDictionary(r => r.Key, r => r.Label);
        Assert.Equal("VHF 1 frequencies", rows[Vhf1.TransferKey]);
        Assert.Equal("VHF 2 frequencies", rows[Vhf2.TransferKey]);
        Assert.Equal("Squawk", rows[A300RadioWatches.SquawkKey]);
        Assert.Equal(rows.Count, rows.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());   // a flat list: no two alike
        // The windows carry nothing of their own: a checkbox there would mute nothing.
        Assert.DoesNotContain(rows.Keys, key => key == Vhf1.Window1Key || key == Vhf1.Window2Key || key == Vhf2.Window1Key);
    }

    [Fact]
    public void No_watched_variable_shares_its_name_with_a_batch_covered_one()
    {
        // [VAR-7]: the batch sorts by name, so a second batched copy of one of these would shift every slot after it.
        var vars = _def.GetVariables();
        var batched = vars.Values.Where(ContinuousBatchLayout.RidesBatch).Select(ContinuousBatchLayout.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in A300RadioWatches.InputKeys)
            Assert.DoesNotContain(ContinuousBatchLayout.FullName(vars[key]), batched);
    }

    [Fact]
    public void Every_input_delivery_is_consumed_in_silence()
    {
        // The carriers are not in the silent set, so a branch of their own consumes them; nothing reaches MainForm's
        // generic announce, whatever the value.
        foreach (var key in A300RadioWatches.InputKeys)
        {
            Assert.True(_def.ProcessSimVarUpdate(key, 0, _speech), key);
            Assert.True(_def.ProcessSimVarUpdate(key, 1, _speech), key);
            Assert.True(_def.ProcessSimVarUpdate(key, 21619, _speech), key);
        }
        Assert.Empty(_speech.All);
    }

    // ------------------------------------------------------------------------------------------------------------
    // An outside VHF change
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_outside_standby_change_speaks_once()
    {
        Baseline();
        Reading(Vhf1.Window2Key, 121500);           // an ATC program sets the next frequency into the standby window
        BatchEnd();
        Assert.Empty(_speech.All);                  // the pair is new
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);
        BatchEnd();
        BatchEnd();
        Assert.Single(_speech.All);
    }

    [Fact]
    public void An_outside_active_change_speaks_the_window_in_use()
    {
        Baseline();
        Reading(Vhf1.Window1Key, 124850);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 active 124.850" }, _speech.All);
    }

    [Fact]
    public void An_outside_swap_is_one_sentence()
    {
        // The stock swap EXCHANGES the two windows' contents and leaves the transfer switch alone (measured 2026-10-14).
        Baseline();
        Reading(Vhf1.Window1Key, W2);
        Reading(Vhf1.Window2Key, W1);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 active 124.805, standby 122.800" }, _speech.All);
    }

    [Fact]
    public void An_outside_swap_whose_windows_land_either_side_of_a_batch_end_is_still_one_sentence()
    {
        Baseline();
        Reading(Vhf1.Window1Key, W2);               // window 1's new value, before a batch end
        BatchEnd();
        Reading(Vhf1.Window2Key, W1);               // window 2's, after it
        BatchEnd();
        Assert.Empty(_speech.All);
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 active 124.805, standby 122.800" }, _speech.All);
        BatchEnd();
        BatchEnd();
        Assert.Single(_speech.All);
    }

    [Fact]
    public void An_outside_transfer_is_one_sentence_too()
    {
        Baseline();
        Reading(Vhf1.TransferKey, 1);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 active 124.805, standby 122.800" }, _speech.All);
    }

    [Fact]
    public void The_second_radio_speaks_under_its_own_name_and_the_first_is_left_alone()
    {
        Baseline();
        Reading(Vhf2.Window2Key, 118000);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 2 standby 118.000" }, _speech.All);
    }

    [Fact]
    public void Only_the_first_batchs_end_runs_the_watches()
    {
        // The A300 has two batches (305 batch-covered variables), each delivered once a second: on every batch end the
        // hold, which has to outlast a subscription period, would be half a second.
        Baseline();
        Reading(Vhf1.Window2Key, 121500);
        for (int i = 0; i < 6; i++)
        {
            _now += 400;
            _def.OnContinuousBatchDelivered(IniA300Definition.RadioWatchBatch + 1);
        }
        Assert.Empty(_speech.All);
        BatchEnd();
        Assert.Empty(_speech.All);
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);
    }

    [Fact]
    public void An_outside_change_is_spoken_through_the_announcer_the_deliveries_came_with()
    {
        Baseline();
        var other = new SpeechCapture();
        Reading(Vhf1.Window2Key, 121500);
        _def.ProcessSimVarUpdate(Vhf1.Window2Key, 121500, other);   // the latest delivery names the announcer
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, other.All);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_change_is_not_swallowed_when_the_values_were_cached_before_any_delivery()
    {
        // The initial snapshot fills the cache without reaching ProcessSimVarUpdate: the baseline forms from the cache,
        // and the first change after it still finds an announcer, the delivery that brought it.
        _cache[Vhf1.Window1Key] = W1;
        _cache[Vhf1.Window2Key] = W2;
        _cache[Vhf1.TransferKey] = 0;
        BatchEnd();
        BatchEnd();
        Reading(Vhf1.Window2Key, 121500);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);
    }

    // ------------------------------------------------------------------------------------------------------------
    // MSFSBA's own changes stay quiet
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_typed_vhf_1_standby_box_gives_only_its_confirmation()
    {
        Baseline();
        Assert.True(Set(A300TypedValues.Com1StandbyKey, 121.5));
        Assert.Equal(new[] { "121500000 (>K:COM_STBY_RADIO_SET_HZ)" }, _sent);
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);   // the typed confirmation
        Reading(Vhf1.Window2Key, 121500);                               // the A300 mirrors the stock standby into its window
        BatchEnd();
        BatchEnd();
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);   // and no second sentence
    }

    [Fact]
    public void An_outside_change_after_the_typed_one_speaks()
    {
        Baseline();
        Set(A300TypedValues.Com1StandbyKey, 121.5);
        Reading(Vhf1.Window2Key, 121500);
        BatchEnd();
        BatchEnd();
        BatchEnd();                                                     // the quiet window has gone
        Reading(Vhf1.Window2Key, 118000);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500", "VHF 1 standby 118.000" }, _speech.All);
    }

    // The quiet window is judged at the change's DELIVERY, not at the batch end that finds it. The A300 takes up to about
    // a second to mirror a stock set into its window, plus up to a second of delivery, and batch 1's end lags a window by
    // 0 to 1 s (fixed for a session): a change delivered at 2.0 s can be found at 2.6 s. Judged by that flush it was an
    // outside change, spoken after the hold, a repeat of the typed confirmation ([CORE-7]; found in review, 2026-10-14).

    [Fact]
    public void A_typed_standby_delivered_inside_the_window_is_quiet_though_the_batch_end_that_finds_it_comes_after_it()
    {
        Baseline();
        long mark = _now;
        Assert.True(Set(A300TypedValues.Com1StandbyKey, 121.5));
        DeliverAt(mark + 2000, Vhf1.Window2Key, 121500);                // the mirror, 2.0 s after the send
        FlushAt(mark + 2600);                                           // the first batch end after it
        FlushAt(mark + 3600);                                           // the one a hold would speak at
        FlushAt(mark + 4600);
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);   // the typed confirmation, once
    }

    [Fact]
    public void A_change_delivered_at_the_end_of_the_window_is_still_quiet_and_just_after_it_is_spoken()
    {
        Baseline();
        long mark = _now;
        Set(A300TypedValues.Com1StandbyKey, 121.5);
        DeliverAt(mark + A300CommandedState.HoldMs, Vhf1.Window2Key, 121500);   // exactly 2.5 s: inclusive, as the commanded state
        FlushAt(mark + 2600);
        FlushAt(mark + 3600);
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);
        DeliverAt(mark + 4000, Vhf1.Window2Key, 118000);                         // after the window: an outside change
        FlushAt(mark + 4500);
        FlushAt(mark + 5500);
        Assert.Equal(new[] { "VHF 1 standby 121.500", "VHF 1 standby 118.000" }, _speech.All);
    }

    [Fact]
    public void A_change_delivered_a_moment_after_the_window_is_spoken()
    {
        Baseline();
        long mark = _now;
        Set(A300TypedValues.Com1StandbyKey, 121.5);
        DeliverAt(mark + 2600, Vhf1.Window2Key, 118000);
        FlushAt(mark + 3000);                                           // the hold
        FlushAt(mark + 4000);
        Assert.Equal(new[] { "VHF 1 standby 121.500", "VHF 1 standby 118.000" }, _speech.All);
    }

    [Fact]
    public void A_transfer_delivered_inside_the_window_is_quiet_though_the_batch_end_that_finds_it_comes_after_it()
    {
        Baseline();
        long mark = _now;
        ReadFreshReturns(new() { [Vhf1.Window1Key] = W1, [Vhf1.Window2Key] = W2, [Vhf1.TransferKey] = 1 });
        Assert.True(Set("A300_CPT_VHF_TFR", 1));
        DeliverAt(mark + 2000, Vhf1.TransferKey, 1);
        FlushAt(mark + 2600);
        FlushAt(mark + 3600);
        Assert.Equal(new[] { "VHF 1 active 124.805" }, _speech.All);   // the read-back, once
    }

    [Fact]
    public void The_surroundings_tune_delivered_inside_the_window_is_quiet_though_the_batch_end_that_finds_it_comes_after_it()
    {
        Baseline();
        long mark = _now;
        _def.NoteOwnCom1Tune();
        DeliverAt(mark + 2000, Vhf1.Window2Key, 118300);
        FlushAt(mark + 2600);
        FlushAt(mark + 3600);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void An_outside_change_delivered_after_an_own_one_is_judged_by_its_own_delivery()
    {
        Baseline();
        long mark = _now;
        Set(A300TypedValues.Com1StandbyKey, 121.5);
        DeliverAt(mark + 1000, Vhf1.Window2Key, 121500);                // the own change
        FlushAt(mark + 1500);
        DeliverAt(mark + 2800, Vhf1.TransferKey, 1);                    // an outside transfer, 2.8 s after the send
        FlushAt(mark + 3200);
        FlushAt(mark + 4200);
        Assert.Equal(new[] { "VHF 1 standby 121.500", "VHF 1 active 121.500, standby 122.800" }, _speech.All);
    }

    [Fact]
    public void A_later_delivery_to_the_other_radio_does_not_move_a_radios_quiet_window()
    {
        Baseline();
        long mark = _now;
        Set(A300TypedValues.Com1StandbyKey, 121.5);
        DeliverAt(mark + 1000, Vhf1.Window2Key, 121500);                // VHF 1's own change, inside its window
        DeliverAt(mark + 3000, Vhf2.Window2Key, 118000);                // an outside change on VHF 2, with no mark
        FlushAt(mark + 3500);
        FlushAt(mark + 4500);
        Assert.Equal(new[] { "VHF 1 standby 121.500", "VHF 2 standby 118.000" }, _speech.All);
    }

    [Fact]
    public void A_typed_squawk_delivered_inside_the_window_is_quiet_though_the_batch_end_that_finds_it_comes_after_it()
    {
        Baseline();
        long mark = _now;
        Assert.True(Set(A300TypedValues.SquawkKey, 5473));
        DeliverAt(mark + 2000, A300RadioWatches.SquawkKey, Code("5473"));
        FlushAt(mark + 2600);
        FlushAt(mark + 3600);
        Assert.Equal(new[] { "Squawk 5473" }, _speech.All);             // the typed confirmation, once
    }

    [Fact]
    public void A_squawk_delivered_after_the_window_is_spoken()
    {
        Baseline();
        long mark = _now;
        Set(A300TypedValues.SquawkKey, 5473);
        DeliverAt(mark + 2600, A300RadioWatches.SquawkKey, Code("7700"));
        FlushAt(mark + 3000);                                           // no hold for a single value
        Assert.Equal(new[] { "Squawk 5473", "Squawk 7700" }, _speech.All);
    }

    [Fact]
    public void A_squawk_delivered_at_the_end_of_the_window_is_still_quiet()
    {
        Baseline();
        long mark = _now;
        Set(A300TypedValues.SquawkKey, 5473);
        DeliverAt(mark + A300CommandedState.HoldMs, A300RadioWatches.SquawkKey, Code("5473"));
        FlushAt(mark + 2600);
        FlushAt(mark + 3600);
        Assert.Equal(new[] { "Squawk 5473" }, _speech.All);
    }

    [Fact]
    public void A_mark_made_before_any_delivery_is_not_a_reason_to_swallow_what_comes_later()
    {
        // The cache filled before anything was delivered (the initial snapshot): a mark made then quiets only what is
        // delivered inside its window.
        _cache[Vhf1.Window1Key] = W1;
        _cache[Vhf1.Window2Key] = W2;
        _cache[Vhf1.TransferKey] = 0;
        long mark = _now;
        _def.NoteOwnCom1Tune();
        FlushAt(mark + 500);                                            // the baseline, from the cache
        DeliverAt(mark + 5000, Vhf1.Window2Key, 121500);
        FlushAt(mark + 5500);
        FlushAt(mark + 6500);
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);
    }

    [Fact]
    public void The_typed_vhf_2_standby_box_quiets_vhf_2_only()
    {
        Baseline();
        Set(A300TypedValues.Com2StandbyKey, 118.5);
        Reading(Vhf2.Window2Key, 118500);
        Reading(Vhf1.Window2Key, 121500);                               // an outside change on the OTHER radio
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 2 standby 118.500", "VHF 1 standby 121.500" }, _speech.All);
    }

    [Fact]
    public void A_refused_typed_standby_marks_nothing()
    {
        Baseline();
        Assert.True(Set(A300TypedValues.Com1StandbyKey, 99.5));         // not a COM frequency: refused aloud, nothing sent
        Assert.Empty(_sent);
        Assert.Single(_speech.All);
        Reading(Vhf1.Window2Key, 121500);
        BatchEnd();
        BatchEnd();
        Assert.Equal(2, _speech.All.Count);
        Assert.Equal("VHF 1 standby 121.500", _speech.All[1]);
    }

    [Fact]
    public void A_transfer_press_gives_only_its_read_back()
    {
        Baseline();
        ReadFreshReturns(new() { [Vhf1.Window1Key] = W1, [Vhf1.Window2Key] = W2, [Vhf1.TransferKey] = 1 });
        Assert.True(Set("A300_CPT_VHF_TFR", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_CPT_VHF_TFR_Set)" }, _sent);
        Assert.Equal(new[] { "VHF 1 active 124.805" }, _speech.All);    // the read-back
        Reading(Vhf1.TransferKey, 1);
        BatchEnd();
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 active 124.805" }, _speech.All);
    }

    [Fact]
    public void An_adf_transfer_press_marks_no_vhf_watch()
    {
        Baseline();
        ReadFreshReturns(new() { [A300Radios.All[2].Window1Key] = 900, [A300Radios.All[2].Window2Key] = 990, [A300Radios.All[2].TransferKey] = 0 });
        Assert.True(Set("A300_ADF1_TFR", 1));
        _speech.All.Clear();                                            // the ADF's own read-back
        Reading(Vhf1.Window2Key, 121500);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);
    }

    [Fact]
    public void The_surroundings_windows_com_1_tune_marks_vhf_1_only()
    {
        // MainForm.TuneCom1FromSurroundings sends the stock standby-set (and swap) events the A300 mirrors into VHF 1's
        // windows, then says what COM 1 holds: its own sentence, so the watch stays quiet.
        Baseline();
        _def.NoteOwnCom1Tune();
        Reading(Vhf1.Window2Key, 118300);
        Reading(Vhf2.Window2Key, 118000);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 2 standby 118.000" }, _speech.All);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Muted rows, a settling load, a context reset, a disposed definition
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_muted_row_is_silent_and_what_it_missed_is_not_replayed_on_unmute()
    {
        Baseline();
        _muted.Add(Vhf1.TransferKey);
        Reading(Vhf1.Window2Key, 121500);
        BatchEnd();
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);
        _muted.Clear();
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);                                      // nothing is replayed ...
        Reading(Vhf1.Window2Key, 118000);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 118.000" }, _speech.All);   // ... and the next change speaks
    }

    [Fact]
    public void Muting_one_row_leaves_the_others()
    {
        Baseline();
        _muted.Add(Vhf1.TransferKey);
        Reading(Vhf1.Window2Key, 121500);
        Reading(Vhf2.Window2Key, 118000);
        Reading(A300RadioWatches.SquawkKey, Code("7700"));
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 2 standby 118.000", "Squawk 7700" }.Order(), _speech.All.Order());
    }

    [Fact]
    public void An_armed_seed_gate_is_silent_and_the_load_settling_is_the_baseline()
    {
        Baseline();
        _def.OnSimContextReset();                                       // a flight load: the seed gate arms
        Assert.True(SeedGate().Armed);
        VhfReads(Vhf1, 118000, 118500, 0);
        Reading(A300RadioWatches.SquawkKey, Code("2000"));
        BatchEnd();                                                     // the baselines, after the reset
        Reading(Vhf1.Window2Key, 119000);                               // the load is still settling: these are not changes
        Reading(A300RadioWatches.SquawkKey, Code("5473"));
        BatchEnd();
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);
        Assert.True(SeedGate().Armed);
        SeedGate().Disarm();                                            // settled
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);                                      // what the load set is the baseline, not a change
        Reading(Vhf1.Window2Key, 121500);
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "VHF 1 standby 121.500" }, _speech.All);
    }

    [Fact]
    public void A_context_reset_makes_the_next_reading_a_baseline()
    {
        Baseline();
        _def.OnSimContextReset();
        SeedGate().Disarm();                                            // so only the reset itself is under test
        Reading(Vhf1.Window2Key, 121500);
        Reading(A300RadioWatches.SquawkKey, Code("5473"));
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);
        Reading(Vhf1.Window2Key, 118000);
        Reading(A300RadioWatches.SquawkKey, Code("7700"));
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "Squawk 7700", "VHF 1 standby 118.000" }.Order(), _speech.All.Order());
    }

    [Fact]
    public void A_context_reset_drops_the_quiet_of_a_typed_value()
    {
        Baseline();
        Set(A300TypedValues.Com1StandbyKey, 121.5);
        _speech.All.Clear();
        _def.OnSimContextReset();
        SeedGate().Disarm();
        BatchEnd(100);                                                  // the baseline
        Reading(Vhf1.Window2Key, 118000);
        BatchEnd(100);
        BatchEnd(100);
        Assert.Equal(new[] { "VHF 1 standby 118.000" }, _speech.All);
    }

    [Fact]
    public void A_disposed_definition_says_nothing()
    {
        Baseline();
        Reading(Vhf1.Window2Key, 121500);
        BatchEnd();
        _def.Dispose();
        _def.Attach(_sim);                                              // so only the disposal itself keeps it quiet
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Nothing_is_said_before_the_manager_is_attached()
    {
        var detached = new IniA300Definition { Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null };
        detached.ProcessSimVarUpdate(Vhf1.Window2Key, 121500, _speech);
        detached.OnContinuousBatchDelivered(IniA300Definition.RadioWatchBatch);
        detached.OnContinuousBatchDelivered(IniA300Definition.RadioWatchBatch);
        Assert.Empty(_speech.All);
    }

    // ------------------------------------------------------------------------------------------------------------
    // The squawk
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_outside_squawk_change_speaks_at_the_next_batch_end()
    {
        Baseline();
        Reading(A300RadioWatches.SquawkKey, Code("5473"));
        BatchEnd();
        Assert.Equal(new[] { "Squawk 5473" }, _speech.All);
        BatchEnd();
        BatchEnd();
        Assert.Single(_speech.All);
    }

    [Fact]
    public void The_typed_squawk_gives_only_its_confirmation()
    {
        Baseline();
        Assert.True(Set(A300TypedValues.SquawkKey, 5473));
        Assert.Equal(new[] { "21619 (>K:XPNDR_SET)" }, _sent);
        Assert.Equal(new[] { "Squawk 5473" }, _speech.All);
        Reading(A300RadioWatches.SquawkKey, Code("5473"));
        BatchEnd();
        BatchEnd();
        BatchEnd();
        BatchEnd();
        Assert.Equal(new[] { "Squawk 5473" }, _speech.All);
    }

    [Fact]
    public void An_outside_squawk_change_after_the_typed_one_speaks()
    {
        Baseline();
        Set(A300TypedValues.SquawkKey, 5473);
        Reading(A300RadioWatches.SquawkKey, Code("5473"));
        BatchEnd();
        BatchEnd();
        BatchEnd();                                                     // 3 s past the typed set: the quiet window has gone
        Reading(A300RadioWatches.SquawkKey, Code("7700"));
        BatchEnd();
        Assert.Equal(new[] { "Squawk 5473", "Squawk 7700" }, _speech.All);
    }

    [Fact]
    public void A_refused_squawk_marks_nothing()
    {
        Baseline();
        Assert.True(Set(A300TypedValues.SquawkKey, 9999));              // not octal: refused aloud, nothing sent
        Assert.Empty(_sent);
        Reading(A300RadioWatches.SquawkKey, Code("7700"));
        BatchEnd();
        Assert.Equal(2, _speech.All.Count);
        Assert.Equal("Squawk 7700", _speech.All[1]);
    }

    [Fact]
    public void A_muted_squawk_row_is_silent()
    {
        Baseline();
        _muted.Add(A300RadioWatches.SquawkKey);
        Reading(A300RadioWatches.SquawkKey, Code("7700"));
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_squawk_is_read_from_the_aircraft_code_variable_not_the_readout()
    {
        // The readout (A300_RO_SQUAWK) is read on request for the X key and the Transponder box; the watch's own key streams.
        Baseline();
        _cache[A300Readouts.SquawkCodeKey] = Code("4321");
        BatchEnd();
        BatchEnd();
        Assert.Empty(_speech.All);
    }
}
