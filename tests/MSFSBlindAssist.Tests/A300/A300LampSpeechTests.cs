using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// When the A300's light changes are spoken: after a short gather period, netted per light, and only once a
/// bus's light power has settled. The power flags and the fault flags reach MSFSBA on separate once-a-second
/// subscriptions, in either order (measured 2026-10-09: on external power on the faults cleared before the
/// AC light power rose, MSFSBA heard the power first and said "Cabin regulator 2 fault light on", then
/// "off"; on external power off a fault arrived before the power flag).
/// </summary>
public class A300LampSpeechTests
{
    private readonly A300LampSpeech _speech = new();

    private static A300LampChange On(string name) => new(name, true);
    private static A300LampChange Off(string name) => new(name, false);

    [Fact]
    public void A_light_that_went_on_and_back_off_nets_to_nothing() =>
        Assert.Empty(A300LampSpeech.Net(new[] { On("Pack 1 fault light"), Off("Pack 1 fault light") }));

    [Fact]
    public void A_light_that_changed_an_odd_number_of_times_keeps_its_last_change() =>
        Assert.Equal(new[] { Off("Pack 1 fault light") },
            A300LampSpeech.Net(new[] { Off("Pack 1 fault light"), On("Pack 1 fault light"), Off("Pack 1 fault light") }));

    [Fact]
    public void Netted_lights_keep_the_order_of_their_last_change() =>
        Assert.Equal(new[] { On("Pack 2 fault light"), On("Pack 1 fault light") },
            A300LampSpeech.Net(new[] { On("Pack 1 fault light"), On("Pack 2 fault light"), Off("Pack 1 fault light"), On("Pack 1 fault light") }));

    [Fact]
    public void Pending_changes_are_spoken_as_one_sentence_and_then_cleared()
    {
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Add(On("Pack 2 fault light"), 10_000);
        Assert.Equal("2 lights on: Pack 1 fault, Pack 2 fault", _speech.Flush(10_000 + A300LampSpeech.GatherMs));
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void A_first_change_waits_one_gather_period_before_it_is_spoken()
    {
        _speech.Add(On("Pack 1 fault light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs - 1));
        Assert.Equal("Pack 1 fault light on", _speech.Flush(10_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void A_pair_that_nets_to_nothing_while_the_power_settles_says_nothing()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Cabin regulator 2 fault light"), 10_000);
        _speech.Add(Off("Cabin regulator 2 fault light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void After_a_power_change_the_changes_wait_until_the_power_has_settled()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Cabin regulator 2 fault light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs - 1));
        _speech.Add(Off("Cabin regulator 2 fault light"), 11_000);   // the clear, a subscription period later
        _speech.Add(On("Standby generator fault light"), 11_000);
        Assert.Equal("Standby generator fault light on", _speech.Flush(10_000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void A_fault_arriving_just_before_its_power_flag_is_netted_with_it()
    {
        // Measured 2026-10-09, external power off: "2 lights on: AC bus 2 off, Rudder travel limiter system 2
        // fault" a second before the AC light power flag arrived, then the same fault among the lights off.
        _speech.Add(On("Rudder travel limiter system 2 fault light"), 10_000);
        _speech.Add(On("AC bus 2 off light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + 900));                     // a batch ends inside the gather period
        _speech.NotePowerChange(10_000 + 1000);
        _speech.Add(Off("Rudder travel limiter system 2 fault light"), 10_000 + 1000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs));
        Assert.Equal("AC bus 2 off light on", _speech.Flush(10_000 + 1000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void Clearing_drops_the_pending_changes_and_the_hold()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Clear();
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs));
        _speech.Add(On("Pack 1 fault light"), 20_000);
        Assert.Equal("Pack 1 fault light on", _speech.Flush(20_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void The_settle_window_covers_two_subscription_periods_and_the_gather_period_one()
    {
        Assert.InRange(A300LampSpeech.SettleMs, 2000, 3000);
        Assert.InRange(A300LampSpeech.GatherMs, 1000, 1999);
    }

    [Fact]
    public void A_light_going_off_is_spoken_only_once_it_has_stayed_off_for_the_hold()
    {
        _speech.Add(Off("Pack 1 fault light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs));
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.OffHoldMs - 1));
        Assert.Equal("Pack 1 fault light off", _speech.Flush(10_000 + A300LampSpeech.OffHoldMs));
    }

    [Fact]
    public void A_flashing_light_says_on_once_and_off_only_once_it_stops()
    {
        // The autoland warning light flashes about 0.6 s on, 0.6 s off (measured 2026-10-10, its test switch),
        // and streams every change; batches end once a second.
        const string light = "Autoland warning light";
        var said = new List<string>();
        long next = 10_000;
        for (long t = 10_000; t < 20_000; t += 600)
        {
            _speech.Add((t - 10_000) / 600 % 2 == 0 ? On(light) : Off(light), t);
            for (; next <= t; next += 1000)
                if (_speech.Flush(next) is string text)
                    said.Add(text);
        }
        _speech.Add(Off(light), 20_200);   // the test switch released: it stays dark
        for (; next <= 20_200 + A300LampSpeech.OffHoldMs + 1000; next += 1000)
            if (_speech.Flush(next) is string text)
                said.Add(text);
        Assert.Equal(new[] { "Autoland warning light on", "Autoland warning light off" }, said);
    }

    [Fact]
    public void A_flashing_light_is_spoken_on_at_the_first_batch_end_after_its_gather()
    {
        // Live, 2026-10-10: the dark phases netted with the unspoken "on" until a batch end fell in a lit phase,
        // so "on" came 4 s after the light first lit.
        const string light = "Autoland warning light";
        _speech.Add(On(light), 10_000);
        _speech.Add(Off(light), 10_600);
        _speech.Add(On(light), 11_200);
        _speech.Add(Off(light), 11_800);
        Assert.Equal("Autoland warning light on", _speech.Flush(12_000));
    }

    [Fact]
    public void A_light_that_blinks_once_is_spoken_on_then_off_once_it_has_stayed_dark()
    {
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Add(Off("Pack 1 fault light"), 10_500);
        Assert.Equal("Pack 1 fault light on", _speech.Flush(10_000 + A300LampSpeech.GatherMs));
        Assert.Null(_speech.Flush(10_500 + A300LampSpeech.OffHoldMs - 1));
        Assert.Equal("Pack 1 fault light off", _speech.Flush(10_500 + A300LampSpeech.OffHoldMs));
    }

    [Fact]
    public void A_light_power_change_darkening_a_light_is_not_held_past_the_settle()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(Off("Pack 1 fault light"), 10_000);
        Assert.Equal("Pack 1 fault light off", _speech.Flush(10_000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void Clearing_drops_an_off_still_held()
    {
        _speech.Add(Off("Pack 1 fault light"), 10_000);
        _speech.Clear();
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.OffHoldMs));
    }

    [Fact]
    public void The_off_hold_outlasts_a_flash_and_two_subscription_periods() =>
        Assert.InRange(A300LampSpeech.OffHoldMs, 2500, 3500);

    [Fact]
    public void Forgetting_a_light_drops_its_held_off_and_its_pending_changes()
    {
        // A change that is not spoken (muted, a pressed button's own light) leaves the light's earlier, opposite
        // change to be spoken although it is stale, so the definition tells the speech to forget the light.
        _speech.Add(Off("Pack 1 fault light"), 10_000);   // held
        _speech.Add(On("Pack 2 fault light"), 10_000);    // pending
        _speech.Forget("Pack 1 fault light");
        _speech.Forget("Pack 2 fault light");
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.OffHoldMs));
    }

    [Fact]
    public void Forgetting_a_light_leaves_every_other_light_to_be_spoken()
    {
        _speech.Add(Off("Pack 1 fault light"), 10_000);
        _speech.Add(On("Pack 2 fault light"), 10_000);
        _speech.Add(Off("Pack 3 fault light"), 10_000);
        _speech.Forget("Pack 2 fault light");
        Assert.Equal("2 lights off: Pack 1 fault, Pack 3 fault", _speech.Flush(10_000 + A300LampSpeech.OffHoldMs));
    }

    [Fact]
    public void A_light_forgotten_and_then_changed_again_is_spoken_by_that_last_change()
    {
        // On (unspoken), then off without being kept, then on again: without the forget the two ons would net away
        // and the pilot would never hear a light that is lit.
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Forget("Pack 1 fault light");
        _speech.Add(On("Pack 1 fault light"), 11_000);
        Assert.Equal("Pack 1 fault light on", _speech.Flush(11_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void A_change_queued_late_in_a_gather_gets_its_own_gather()
    {
        // The first change's gather is over at 11,500, the second's (queued at 10,700) is not: a power flag arriving
        // at 11,700 must still net with it, not find it already spoken.
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Add(On("Rudder travel limiter system 2 fault light"), 10_700);
        Assert.Null(_speech.Flush(11_500));
        _speech.NotePowerChange(11_700);
        _speech.Add(Off("Rudder travel limiter system 2 fault light"), 11_700);
        Assert.Equal("Pack 1 fault light on", _speech.Flush(14_200));
    }

    [Fact]
    public void A_later_change_never_shortens_the_hold_a_power_change_set()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Add(On("Pack 2 fault light"), 10_500);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs - 1));
        Assert.Equal("2 lights on: Pack 1 fault, Pack 2 fault", _speech.Flush(10_000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void A_light_already_pending_does_not_extend_the_gather()
    {
        // A NEW light's change gets its own gather (the late-change test above); a further change of a light that is
        // already waiting does not, or a light changing every few hundred milliseconds would keep the sentence back.
        _speech.NotePowerChange(10_000);                  // the settle ends at 12,500
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Add(On("Pack 2 fault light"), 11_500);    // a new light: its own gather ends at 13,000
        _speech.Add(Off("Pack 2 fault light"), 12_000);   // pending already: no gather of its own (it would end at 13,500)
        Assert.Null(_speech.Flush(12_999));
        Assert.Equal("Pack 1 fault light on", _speech.Flush(13_000));
    }

    [Fact]
    public void A_light_flashing_through_a_power_settle_does_not_hold_the_sentence_back()
    {
        // Measured on the built class in review (2026-10-13): while a power change settles an off is queued, not held,
        // so the autoland light, flashing every 0.6 s, queued a change every 0.6 s. With each one moving the gather on,
        // the settle never ended while it flashed, and the first sentence came at 22 s instead of 3 s.
        const string flasher = "Autoland warning light", fault = "Battery 1 fault light";
        long start = 10_000;
        var said = new List<(long After, string Text)>();
        _speech.NotePowerChange(start);
        for (long after = 0; after <= 24_000; after += 100)
        {
            long at = start + after;
            if (after == 500)
                _speech.Add(On(fault), at);
            if (after is >= 200 and <= 19_400 && (after - 200) % 600 == 0)
                _speech.Add((after - 200) / 600 % 2 == 0 ? On(flasher) : Off(flasher), at);
            if (after > 0 && after % 1_000 == 0 && _speech.Flush(at) is string text)   // a batch end every second
                said.Add((after, text));
        }
        // The first batch end after the settle (2,500 ms) speaks the fault and the flashing light's net state (five
        // changes: on). The flashes after it are offs held and canceled by the next on: nothing more is said.
        Assert.Equal(new[] { (3_000L, "2 lights on: Battery 1 fault, Autoland warning") }, said);
    }
}
