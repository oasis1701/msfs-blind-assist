using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;
using Xunit;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// A VHF radio's outside-change call-out ([A300-29]), as a pure class: the aircraft's own two windows and its transfer
/// switch, compared as formatted text. A change speaks only once its (active, standby) pair has read the same at two
/// consecutive flushes (a swap changes BOTH windows, each on its own once-a-second subscription, so the two new values
/// can arrive on opposite sides of a batch end: measured 2026-10-14); MSFSBA's own change is quiet when it is DELIVERED
/// within <see cref="A300CommandedState.HoldMs"/> of the mark (the flush that finds it may come a second later), and a
/// load that is still settling re-baselines.
/// </summary>
public class A300FrequencyWatchTests
{
    private static readonly A300Radio Vhf1 = A300Radios.All.Single(r => r.Name == "VHF 1");
    private static readonly A300Radio Vhf2 = A300Radios.All.Single(r => r.Name == "VHF 2");

    /// <summary>The first window's frequency, 122.800, in use while the transfer switch reads 0; and the second's, 124.805.</summary>
    private const double W1 = 122800;
    private const double W2 = 124805;

    private readonly A300FrequencyWatch _watch = new(Vhf1);
    private long _now = 100_000;

    /// <summary>One flush, a second after the last (the batch period), of readings delivered at the flush itself unless
    /// <paramref name="deliveredAt"/> says when the latest of them arrived.</summary>
    private string? Flush(double? window1, double? window2, double? transfer = 0, bool settling = false, long advance = 1000,
        long? deliveredAt = null)
    {
        _now += advance;
        return _watch.Flush(window1, window2, transfer, deliveredAt ?? _now, settling);
    }

    /// <summary>The first complete reading: 122.800 in use, 124.805 the standby.</summary>
    private void Baseline() => Assert.Null(Flush(W1, W2));

    [Fact]
    public void The_first_complete_reading_is_a_silent_baseline()
    {
        Assert.Null(Flush(W1, W2));
        Assert.Null(Flush(W1, W2));
    }

    [Fact]
    public void A_standby_change_speaks_once_its_pair_has_held_a_flush()
    {
        Baseline();
        Assert.Null(Flush(W1, 121500));                                  // the pair is new: remembered, not spoken
        Assert.Equal("VHF 1 standby 121.500", Flush(W1, 121500));        // it read the same twice
        Assert.Null(Flush(W1, 121500));
    }

    [Fact]
    public void An_active_change_speaks_the_window_in_use()
    {
        Baseline();
        Assert.Null(Flush(118000, W2));
        Assert.Equal("VHF 1 active 118.000", Flush(118000, W2));
    }

    [Fact]
    public void A_swap_of_the_windows_is_one_sentence_naming_both()
    {
        // The stock swap event EXCHANGES the two windows' contents and leaves the transfer switch where it is
        // (measured 2026-10-14: 122800 / 121500 became 121500 / 122800 with the switch at 0).
        Assert.Null(Flush(122800, 121500));
        Assert.Null(Flush(121500, 122800));
        Assert.Equal("VHF 1 active 121.500, standby 122.800", Flush(121500, 122800));
        Assert.Null(Flush(121500, 122800));
    }

    [Fact]
    public void A_transfer_with_the_windows_unchanged_is_the_same_swap_in_words()
    {
        Baseline();
        Assert.Null(Flush(W1, W2, transfer: 1));
        Assert.Equal("VHF 1 active 124.805, standby 122.800", Flush(W1, W2, transfer: 1));
    }

    [Fact]
    public void A_swap_whose_windows_arrive_on_opposite_sides_of_a_flush_is_still_one_sentence()
    {
        // Each window streams on its own subscription, so window 1's new value can be read before one batch end and
        // window 2's after it. Spoken at the first flush that saw a changed pair, it would have been two sentences, the
        // first of them half of the swap: "VHF 1 active 124.805", then "VHF 1 standby 122.800".
        Baseline();                                                      // 122.800 in use, 124.805 standby
        Assert.Null(Flush(W2, W2));                                      // window 1 has its new value, window 2 not yet
        Assert.Null(Flush(W2, W1));                                      // window 2 has its new value too
        Assert.Equal("VHF 1 active 124.805, standby 122.800", Flush(W2, W1));
        Assert.Null(Flush(W2, W1));
        Assert.Null(Flush(W2, W1));
    }

    [Fact]
    public void A_pair_that_changes_back_before_it_has_held_says_nothing()
    {
        Baseline();
        Assert.Null(Flush(W1, 121500));
        Assert.Null(Flush(W1, W2));
        Assert.Null(Flush(W1, W2));
    }

    [Fact]
    public void A_change_that_keeps_moving_speaks_the_difference_from_the_baseline_once_it_holds()
    {
        Baseline();
        Assert.Null(Flush(W1, 121500));
        Assert.Null(Flush(W1, 118000));                                  // moved again: not held
        Assert.Equal("VHF 1 standby 118.000", Flush(W1, 118000));        // against 124.805, the baseline
    }

    [Fact]
    public void A_spoken_change_is_the_new_baseline()
    {
        Baseline();
        Flush(W1, 121500);
        Assert.Equal("VHF 1 standby 121.500", Flush(W1, 121500));
        Assert.Null(Flush(W1, W2));                                      // back again: a change, remembered
        Assert.Equal("VHF 1 standby 124.805", Flush(W1, W2));
    }

    [Fact]
    public void A_swap_of_equal_frequencies_has_nothing_to_say()
    {
        Assert.Null(Flush(121500, 121500));
        Assert.Null(Flush(121500, 121500, transfer: 1));
        Assert.Null(Flush(121500, 121500, transfer: 1));
    }

    [Fact]
    public void The_transfer_switch_value_is_read_as_the_radio_reads_it()
    {
        // VHF: the switch at 0 uses the first window, 1 the second ([A300-26]); a value is rounded to a position.
        Baseline();
        Assert.Null(Flush(W1, W2, transfer: 0.98));
        Assert.Equal("VHF 1 active 124.805, standby 122.800", Flush(W1, W2, transfer: 0.98));
    }

    [Fact]
    public void An_own_mark_is_silent_and_rebaselines_at_every_flush_inside_the_window_and_a_later_change_speaks()
    {
        Baseline();                                                      // 101 s
        _watch.MarkOwn(_now);                                            // quiet through 103.5 s
        Assert.Null(Flush(W1, 121500));                                  // 102 s: MSFSBA typed it, and the baseline moves
        Assert.Null(Flush(W1, 118000));                                  // 103 s: still inside, the baseline moves again
        Assert.Null(Flush(W1, 118000));                                  // 104 s: outside, nothing changed
        Assert.Null(Flush(W1, 119500));                                  // a different change: remembered
        Assert.Equal("VHF 1 standby 119.500", Flush(W1, 119500));        // against 118.000, where the window left it
    }

    [Fact]
    public void A_change_delivered_after_the_window_speaks()
    {
        Baseline();
        long mark = _now;
        _watch.MarkOwn(mark);
        long delivered = mark + A300CommandedState.HoldMs + 1;
        Assert.Null(_watch.Flush(W1, 121500, 0, delivered, settling: false));
        Assert.Equal("VHF 1 standby 121.500", _watch.Flush(W1, 121500, 0, delivered, settling: false));
    }

    [Fact]
    public void The_window_is_the_commanded_states_hold_and_ends_with_it()
    {
        Baseline();
        long mark = _now;
        _watch.MarkOwn(mark);
        Assert.Equal(2500, A300CommandedState.HoldMs);
        // Delivered exactly at the hold it is still quiet (A300CommandedState keeps a commanded value through it), so the
        // baseline moved and the same pair has nothing to hold at the next flush ...
        Assert.Null(_watch.Flush(W1, 121500, 0, mark + A300CommandedState.HoldMs, settling: false));
        Assert.Null(_watch.Flush(W1, 121500, 0, mark + A300CommandedState.HoldMs, settling: false));
        // ... and one delivered a millisecond past it is a change to hold, not a quiet one.
        long late = mark + A300CommandedState.HoldMs + 1;
        Assert.Null(_watch.Flush(W1, 118000, 0, late, settling: false));
        Assert.Equal("VHF 1 standby 118.000", _watch.Flush(W1, 118000, 0, late, settling: false));
    }

    [Fact]
    public void A_change_is_judged_by_when_it_was_delivered_not_by_the_flush_that_finds_it()
    {
        // The watch has no clock of its own: the caller records when each input's change arrived. One delivered 2.0 s after
        // the mark is quiet at every flush that finds it, the first and the ones after (the A300 mirrors a stock set in up to
        // a second and batch 1's end lags by 0 to 1 s, so it can be found at 2.6 s); one delivered at 2.6 s is not.
        Baseline();
        long mark = _now;
        _watch.MarkOwn(mark);
        long inside = mark + 2000;
        Assert.Null(_watch.Flush(W1, 121500, 0, inside, settling: false));
        Assert.Null(_watch.Flush(W1, 121500, 0, inside, settling: false));
        Assert.Null(_watch.Flush(W1, 121500, 0, inside, settling: false));
        long outside = mark + 2600;
        Assert.Null(_watch.Flush(W1, 118000, 0, outside, settling: false));
        Assert.Equal("VHF 1 standby 118.000", _watch.Flush(W1, 118000, 0, outside, settling: false));
    }

    [Fact]
    public void A_change_with_no_delivery_on_record_is_never_an_own_echo()
    {
        Baseline();
        _watch.MarkOwn(_now);
        Assert.Null(_watch.Flush(W1, 121500, 0, null, settling: false));
        Assert.Equal("VHF 1 standby 121.500", _watch.Flush(W1, 121500, 0, null, settling: false));
    }

    [Fact]
    public void A_pending_outside_change_is_swallowed_by_a_mark_made_before_the_flush_that_confirms_it()
    {
        // A known residual ([A300-29]): the change was delivered before MSFSBA acted and seen at one flush; the mark lands
        // before the confirming flush, and a delivery before the mark is no later than the mark's window.
        Baseline();
        long before = _now - 500;
        Assert.Null(_watch.Flush(W1, 121500, 0, before, settling: false));       // seen once: held
        _watch.MarkOwn(_now);                                                    // MSFSBA acts
        Assert.Null(_watch.Flush(W1, 121500, 0, before, settling: false));       // the confirming flush: re-baselined
        Assert.Null(_watch.Flush(W1, 121500, 0, before, settling: false));
    }

    [Fact]
    public void A_mark_only_quiets_its_own_radio()
    {
        var other = new A300FrequencyWatch(Vhf2);
        long t = 100_000;
        other.Flush(W1, W2, 0, t, settling: false);
        _watch.Flush(W1, W2, 0, t, settling: false);
        _watch.MarkOwn(t);
        Assert.Null(other.Flush(W1, 121500, 0, t + 1000, settling: false));
        Assert.Equal("VHF 2 standby 121.500", other.Flush(W1, 121500, 0, t + 2000, settling: false));
    }

    [Fact]
    public void A_load_that_is_settling_only_rebaselines()
    {
        Baseline();
        Assert.Null(Flush(W1, 121500, settling: true));
        Assert.Null(Flush(118000, 121500, settling: true));
        Assert.Null(Flush(118000, 121500));                              // the load has settled: nothing differs from the baseline
        Assert.Null(Flush(118000, 119500));
        Assert.Equal("VHF 1 standby 119.500", Flush(118000, 119500));
    }

    [Fact]
    public void A_settling_load_gives_the_first_reading_its_baseline_too()
    {
        Assert.Null(Flush(W1, W2, settling: true));
        Assert.Null(Flush(W1, W2));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void A_missing_input_gives_no_sentence_and_no_baseline(bool noFirstWindow, bool noSecondWindow, bool noTransfer)
    {
        double? w1 = noFirstWindow ? null : W1, w2 = noSecondWindow ? null : W2, transfer = noTransfer ? null : 0;
        Assert.Null(Flush(w1, w2, transfer));
        Assert.Null(Flush(w1, w2, transfer));
        // The first complete reading is the baseline, whatever it reads: not a change from a reading never made.
        Assert.Null(Flush(W1, 121500));
        Assert.Null(Flush(W1, 121500));
        // And a change from there speaks.
        Assert.Null(Flush(W1, 118000));
        Assert.Equal("VHF 1 standby 118.000", Flush(W1, 118000));
    }

    [Fact]
    public void An_input_that_goes_missing_drops_the_baseline_it_had()
    {
        Baseline();
        Assert.Null(Flush(W1, null));
        Assert.Null(Flush(W1, 121500));                                  // read again, and different: a baseline, not a change
        Assert.Null(Flush(W1, 121500));
        Assert.Null(Flush(W1, 121500));
    }

    [Fact]
    public void Reset_makes_the_next_reading_a_baseline_and_drops_a_pending_mark()
    {
        Baseline();
        _watch.MarkOwn(_now);
        _watch.Reset();
        Assert.Null(Flush(W1, 121500, advance: 100));                    // the reading after a reset: a baseline
        Assert.Null(Flush(W1, 118000, advance: 100));                    // inside the old mark's 2.5 s: the mark went with the reset
        Assert.Equal("VHF 1 standby 118.000", Flush(W1, 118000, advance: 100));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_frequency_keeps_its_point_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Baseline();
            Flush(W1, 121500);
            Assert.Equal("VHF 1 standby 121.500", Flush(W1, 121500));
            Flush(124805, 122800);
            Assert.Equal("VHF 1 active 124.805, standby 122.800", Flush(124805, 122800));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Texts_are_compared_not_raw_doubles()
    {
        // A window that reads 122800.0004 shows 122.800 like 122800: the same text is no change.
        Baseline();
        Assert.Null(Flush(122800.0004, W2));
        Assert.Null(Flush(122800.0004, W2));
        Assert.Null(Flush(W1, 124805.0004));
        Assert.Null(Flush(W1, 124805.0004));
    }

    [Fact]
    public void The_watch_names_its_radio()
    {
        var watch = new A300FrequencyWatch(Vhf2);
        Assert.Same(Vhf2, watch.Radio);
        watch.Flush(W1, W2, 0, 1000, settling: false);
        Assert.Null(watch.Flush(W1, 121500, 0, 2000, settling: false));
        Assert.Equal("VHF 2 standby 121.500", watch.Flush(W1, 121500, 0, 3000, settling: false));
    }
}

/// <summary>The squawk's outside-change call-out ([A300-29]): a single value, so no hold; the same quiet and settle rules.</summary>
public class A300SquawkWatchTests
{
    private static double Code(string digits) => Convert.ToInt32(digits, 16);   // BCO16: each nibble a digit

    private readonly A300SquawkWatch _watch = new();
    private long _now = 100_000;

    private string? Flush(double? code, bool settling = false, long advance = 1000)
    {
        _now += advance;
        return _watch.Flush(code, _now, settling);
    }

    [Fact]
    public void The_first_reading_is_a_silent_baseline()
    {
        Assert.Null(Flush(Code("1200")));
        Assert.Null(Flush(Code("1200")));
    }

    [Fact]
    public void A_change_speaks_the_digits_as_one_number_at_the_first_flush()
    {
        Flush(Code("1200"));
        Assert.Equal("Squawk 5473", Flush(Code("5473")));
        Assert.Null(Flush(Code("5473")));
    }

    [Fact]
    public void A_code_with_leading_zeros_keeps_them()
    {
        Flush(Code("1200"));
        Assert.Equal("Squawk 0042", Flush(Code("0042")));
    }

    [Fact]
    public void The_words_are_the_typed_confirmations()
    {
        Assert.Equal("Squawk 5473", A300Readouts.SquawkPhrase(Code("5473")));
        Assert.Equal("Squawk 5473", A300TypedValues.Plan(A300TypedValues.SquawkKey, 5473, false).Confirmation);
    }

    [Fact]
    public void Texts_are_compared_not_raw_doubles()
    {
        Flush(Code("5473"));
        Assert.Null(Flush(Code("5473") + 0.3));
        Assert.Null(Flush(Code("5473") - 0.2));
    }

    [Fact]
    public void An_own_mark_is_silent_and_a_later_change_speaks()
    {
        Flush(Code("1200"));                                             // 101 s
        _watch.MarkOwn(_now);                                            // quiet through 103.5 s
        Assert.Null(Flush(Code("5473")));                                // 102 s: typed or stepped by MSFSBA, its own read-back says it
        Assert.Null(Flush(Code("5474")));                                // 103 s: still inside, the baseline moves again
        Assert.Equal("Squawk 7700", Flush(Code("7700")));                // 104 s: outside it, a change
    }

    [Fact]
    public void A_change_after_the_window_speaks_and_the_window_is_the_commanded_states_hold()
    {
        Flush(Code("1200"));
        long mark = _now;
        _watch.MarkOwn(mark);
        Assert.Null(_watch.Flush(Code("5473"), mark + A300CommandedState.HoldMs, settling: false));
        Assert.Equal("Squawk 7700", _watch.Flush(Code("7700"), mark + A300CommandedState.HoldMs + 1, settling: false));
    }

    [Fact]
    public void A_code_is_judged_by_when_it_was_delivered_not_by_the_flush_that_finds_it()
    {
        Flush(Code("1200"));
        long mark = _now;
        _watch.MarkOwn(mark);
        Assert.Null(_watch.Flush(Code("5473"), mark + 2000, settling: false));   // found after the window, delivered inside it
        Assert.Null(_watch.Flush(Code("5473"), mark + 2000, settling: false));
        Assert.Equal("Squawk 7700", _watch.Flush(Code("7700"), mark + 2600, settling: false));
    }

    [Fact]
    public void A_code_with_no_delivery_on_record_is_never_an_own_echo()
    {
        Flush(Code("1200"));
        _watch.MarkOwn(_now);
        Assert.Equal("Squawk 5473", _watch.Flush(Code("5473"), null, settling: false));
    }

    [Fact]
    public void A_load_that_is_settling_only_rebaselines()
    {
        Flush(Code("1200"));
        Assert.Null(Flush(Code("5473"), settling: true));
        Assert.Null(Flush(Code("5473")));
        Assert.Equal("Squawk 7700", Flush(Code("7700")));
    }

    [Fact]
    public void A_missing_reading_gives_no_sentence_and_no_baseline()
    {
        Assert.Null(Flush(null));
        Assert.Null(Flush(Code("5473")));                                // the baseline
        Assert.Null(Flush(Code("5473")));
        Assert.Null(Flush(null));
        Assert.Null(Flush(Code("1200")));                                // a baseline again, not a change from 5473
        Assert.Equal("Squawk 7700", Flush(Code("7700")));
    }

    [Fact]
    public void Reset_makes_the_next_reading_a_baseline_and_drops_a_pending_mark()
    {
        Flush(Code("1200"));
        _watch.MarkOwn(_now);
        _watch.Reset();
        Assert.Null(Flush(Code("5473"), advance: 100));
        Assert.Equal("Squawk 7700", Flush(Code("7700"), advance: 100));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_digits_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Flush(Code("1200"));
            Assert.Equal("Squawk 5473", Flush(Code("5473")));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

/// <summary>Which key carries what: the rows, the inputs and the actions of MSFSBA's own that quiet a watch.</summary>
public class A300RadioWatchesTests
{
    [Theory]
    [InlineData(1000L, 1000L, true)]       // delivered exactly at the window's end: inclusive
    [InlineData(1000L, 999L, true)]
    [InlineData(1000L, 0L, true)]          // before the mark too: an outside change not yet flushed when MSFSBA acted
    [InlineData(1000L, 1001L, false)]
    [InlineData(null, 500L, false)]        // no mark
    [InlineData(1000L, null, false)]       // no delivery on record
    [InlineData(null, null, false)]
    public void A_change_is_an_own_echo_when_it_was_delivered_no_later_than_the_marks_window_ends(long? quietUntil, long? deliveredAt, bool echo) =>
        Assert.Equal(echo, A300RadioWatches.IsOwnEcho(quietUntil, deliveredAt));

    [Fact]
    public void Only_the_vhf_radios_are_watched_the_adfs_are_not()
    {
        Assert.Equal(new[] { "VHF 1", "VHF 2" }, A300Radios.Vhf.Select(r => r.Name));
        Assert.All(A300Radios.Vhf, radio => Assert.True(A300RadioWatches.Watches(radio)));
        Assert.All(A300Radios.All.Where(r => !A300Radios.Vhf.Contains(r)), radio => Assert.False(A300RadioWatches.Watches(radio)));
    }

    [Fact]
    public void Each_radios_row_rides_its_transfer_switch_and_is_named_for_its_frequencies()
    {
        var vhf1 = A300Radios.Vhf[0];
        var vhf2 = A300Radios.Vhf[1];
        Assert.Equal((vhf1.TransferKey, "VHF 1 frequencies"), (A300RadioWatches.RowKey(vhf1), A300RadioWatches.RowName(vhf1)));
        Assert.Equal((vhf2.TransferKey, "VHF 2 frequencies"), (A300RadioWatches.RowKey(vhf2), A300RadioWatches.RowName(vhf2)));
        Assert.Equal("Squawk", A300RadioWatches.SquawkRowName);
        Assert.Equal(("TRANSPONDER CODE:1", "BCO16"), (A300RadioWatches.SquawkVar, A300RadioWatches.SquawkUnits));
    }

    [Fact]
    public void The_inputs_are_each_vhf_radios_two_windows_and_switch_and_the_squawk_and_the_rows_ride_three_of_them()
    {
        var inputs = A300Radios.Vhf.SelectMany(r => new[] { r.Window1Key, r.Window2Key, r.TransferKey })
            .Append(A300RadioWatches.SquawkKey).Order(StringComparer.Ordinal);
        Assert.Equal(inputs, A300RadioWatches.InputKeys.Order(StringComparer.Ordinal));
        var rows = new[] { A300Radios.Vhf[0].TransferKey, A300Radios.Vhf[1].TransferKey, A300RadioWatches.SquawkKey }
            .Order(StringComparer.Ordinal);
        Assert.Equal(rows, A300RadioWatches.RowKeys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(A300TypedValues.Com1StandbyKey, "VHF 1")]
    [InlineData(A300TypedValues.Com2StandbyKey, "VHF 2")]
    [InlineData("A300_CPT_VHF_TFR", "VHF 1")]
    [InlineData("A300_FO_VHF_TFR", "VHF 2")]
    public void The_typed_standby_boxes_and_the_vhf_transfer_buttons_are_their_radios_own_changes(string key, string radio) =>
        Assert.Equal(radio, A300RadioWatches.VhfRadioFor(key)?.Name);

    [Theory]
    [InlineData("A300_ADF1_TFR")]
    [InlineData("A300_ADF2_TFR")]
    [InlineData("A300_CPT_VHF_SQL")]
    [InlineData("A300_CPT_ADF1_BIG")]
    [InlineData(A300TypedValues.SquawkKey)]
    [InlineData(A300TypedValues.Vor1FrequencyKey)]
    public void Nothing_else_is_a_vhf_own_change(string key) => Assert.Null(A300RadioWatches.VhfRadioFor(key));

    [Theory]
    [InlineData(A300TypedValues.SquawkKey, true)]
    [InlineData("A300_TCAS_BIG", false)]      // the code knobs are no rows: the typed box is the one way MSFSBA sets a code
    [InlineData("A300_TCAS_SMALL", false)]
    [InlineData("A300_TCAS_MODE", false)]
    [InlineData("A300_TCAS_IDENT", false)]
    [InlineData(A300TypedValues.Com1StandbyKey, false)]
    public void Only_the_typed_squawk_box_is_the_squawks_own_change(string key, bool own) =>
        Assert.Equal(own, A300RadioWatches.IsSquawkOwnChange(key));

    [Fact]
    public void Every_transfer_button_the_radios_name_is_a_button_of_the_map()
    {
        var map = A300ControlMap.Load();
        foreach (var button in A300Radios.ByTransfer.Keys)
            Assert.Equal(A300Kinds.Button, map.FindByKey(button)?.Kind);
    }
}
