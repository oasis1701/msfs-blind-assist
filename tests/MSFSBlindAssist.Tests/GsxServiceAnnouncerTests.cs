using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxServiceAnnouncerTests
{
    private static GsxServiceState Svc(string id, string state, int? paxDone = null, int? paxTotal = null,
                                       string display = "", string? busPhase = null, int? bagsPercent = null) =>
        new()
        {
            Id = id, State = state, DisplayName = display == "" ? id : display,
            PaxDone = paxDone, PaxTotal = paxTotal, BusPhase = busPhase, BagsPercent = bagsPercent,
            StateText = $"{id} is {state}",
        };

    [Fact]
    public void First_update_is_silent_baseline()
    {
        var a = new GsxServiceAnnouncer();
        var said = a.Update(new[] { Svc("Boarding", "available") });
        Assert.Empty(said);
    }

    [Fact]
    public void State_transition_announces_once()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "available") });
        var said = a.Update(new[] { Svc("Boarding", "performing") });
        Assert.Single(said);
        Assert.Contains("Board", said[0], StringComparison.OrdinalIgnoreCase);

        // same state again -> silence
        Assert.Empty(a.Update(new[] { Svc("Boarding", "performing") }));
    }

    [Fact]
    public void Repeated_identical_progress_is_suppressed()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "performing", 10, 100) });
        var first = a.Update(new[] { Svc("Boarding", "performing", 20, 100) });
        Assert.Single(first);

        var repeat = a.Update(new[] { Svc("Boarding", "performing", 20, 100) });
        Assert.Empty(repeat);
    }

    [Fact]
    public void Completion_announces()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Refueling", "performing") });
        var said = a.Update(new[] { Svc("Refueling", "completed") });
        Assert.Single(said);
        Assert.Contains("complete", said[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bus_phase_change_announces()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Deboarding", "performing", busPhase: "approaching") });
        var said = a.Update(new[] { Svc("Deboarding", "performing", busPhase: "in position") });
        Assert.Single(said);
        Assert.Contains("in position", said[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reset_re_baselines_so_next_update_is_silent()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "available") });
        a.Reset();
        Assert.Empty(a.Update(new[] { Svc("Boarding", "performing") }));
    }

    [Fact]
    public void Bags_change_alone_announced_when_pax_present()
    {
        // Regression: service carries both pax and bags data; when only bags changes,
        // must announce bags (not the stale pax phrase)
        var a = new GsxServiceAnnouncer();
        // Baseline: Deboarding with pax done=150/186 and bags=40%
        a.Update(new[] { Svc("Deboarding", "performing", 150, 186, bagsPercent: 40) });

        // Update: pax unchanged, bags rise to 70%
        var said = a.Update(new[] { Svc("Deboarding", "performing", 150, 186, bagsPercent: 70) });
        Assert.Single(said);
        // Must mention bags, not passengers
        Assert.Contains("bags", said[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passenger", said[0], StringComparison.OrdinalIgnoreCase);

        // Same bags value again -> silence (no repeat announcement)
        Assert.Empty(a.Update(new[] { Svc("Deboarding", "performing", 150, 186, bagsPercent: 70) }));
    }

    // ── Progress throttle ────────────────────────────────────────────────────
    // GSX patches /services at ~1 Hz, so an unthrottled announcer speaks once
    // PER PASSENGER. The announcements are queued and never interrupt, so they
    // accumulate: a 186-passenger deboarding buries the pilot's only output
    // channel for minutes with a backlog that grows the whole time.

    /// <summary>Feeds one passenger per tick and returns every phrase spoken.</summary>
    private static List<string> DeboardAll(int total, int? bagsPercent = null)
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Deboarding", "performing", 0, total, bagsPercent: bagsPercent) });

        var said = new List<string>();
        for (int done = 1; done <= total; done++)
            said.AddRange(a.Update(new[] { Svc("Deboarding", "performing", done, total, bagsPercent: bagsPercent) }));
        return said;
    }

    [Fact]
    public void A_186_passenger_deboarding_does_not_speak_186_times()
    {
        var said = DeboardAll(186);

        // 1, then every tenth up to 180 — 19 phrases, not 186.
        Assert.Equal(19, said.Count);
        Assert.All(said, p => Assert.Contains("passengers", p, StringComparison.Ordinal));
    }

    [Fact]
    public void Passenger_cadence_is_one_then_every_tenth()
    {
        var said = DeboardAll(35);

        Assert.Equal(new[]
        {
            "Deboarding 1 of 35 passengers.",
            "Deboarding 10 of 35 passengers.",
            "Deboarding 20 of 35 passengers.",
            "Deboarding 30 of 35 passengers.",
        }, said);
    }

    [Fact]
    public void Passenger_zero_marks_the_start_when_it_is_the_first_count_seen()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Deboarding", "performing") });          // baseline, no pax yet
        var said = a.Update(new[] { Svc("Deboarding", "performing", 0, 186) });

        Assert.Equal("Deboarding 0 of 186 passengers.", Assert.Single(said));
    }

    [Fact]
    public void A_sample_that_skips_the_round_number_still_announces()
    {
        // GSX's ~1 Hz sampling routinely jumps a decade boundary (48 -> 53).
        // Requiring an exact multiple on every announce would silence whole
        // boardings at speed, so the gate compares BUCKETS once something has
        // been said.
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "performing", 39, 180) });          // baseline
        Assert.Single(a.Update(new[] { Svc("Boarding", "performing", 40, 180) }));
        Assert.Empty(a.Update(new[] { Svc("Boarding", "performing", 48, 180) }));   // same bucket

        var said = a.Update(new[] { Svc("Boarding", "performing", 53, 180) });
        Assert.Equal("Boarding 53 of 180 passengers.", Assert.Single(said));
    }

    [Fact]
    public void Joining_mid_decade_stays_quiet_until_the_next_milestone()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "performing", 43, 180) });    // baseline
        Assert.Empty(a.Update(new[] { Svc("Boarding", "performing", 44, 180) }));
        Assert.Empty(a.Update(new[] { Svc("Boarding", "performing", 49, 180) }));
        Assert.Single(a.Update(new[] { Svc("Boarding", "performing", 50, 180) }));
    }

    [Fact]
    public void A_revised_passenger_total_announces_even_mid_decade()
    {
        // "150 of 190" and "150 of 186" are different facts to a blind pilot,
        // and the count alone would never open the milestone gate. The outer
        // gate used not to look at PaxTotal at all.
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "performing", 153, 190) });
        var said = a.Update(new[] { Svc("Boarding", "performing", 153, 186) });

        Assert.Equal("Boarding 153 of 186 passengers.", Assert.Single(said));
    }

    [Fact]
    public void Bags_are_throttled_to_ten_percent_steps()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "performing", bagsPercent: 0) });

        var said = new List<string>();
        for (int pct = 1; pct <= 100; pct++)
            said.AddRange(a.Update(new[] { Svc("Boarding", "performing", bagsPercent: pct) }));

        // 10, 20, … 100 — ten phrases out of a hundred ticks, and 100 % is
        // always among them.
        Assert.Equal(10, said.Count);
        Assert.Equal("Boarding bags 10 percent.", said[0]);
        Assert.Equal("Boarding bags 100 percent.", said[^1]);
    }

    [Fact]
    public void Pax_and_bags_moving_together_stay_bounded()
    {
        // The realistic deboarding shape: both counters tick every second.
        var said = DeboardAll(100, bagsPercent: 50);
        Assert.True(said.Count <= 12, $"expected a handful of phrases, got {said.Count}");
    }

    [Fact]
    public void A_restarted_service_replays_its_milestones_from_zero()
    {
        // Turnaround: deboarding finishes, boarding of the same row starts over.
        // The previous run's high-water mark must not silence the new one.
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "performing", 90, 100) });
        Assert.Single(a.Update(new[] { Svc("Boarding", "performing", 100, 100) }));
        Assert.Single(a.Update(new[] { Svc("Boarding", "completed", 100, 100) }));
        Assert.Single(a.Update(new[] { Svc("Boarding", "performing", 0, 120) }));   // state change
        Assert.Single(a.Update(new[] { Svc("Boarding", "performing", 10, 120) }));
    }

    // ── The pure gates ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(19, 2)]
    [InlineData(20, 3)]
    [InlineData(180, 19)]
    // No upper cap: 110/120/130 keep announcing rather than collapsing into a ceiling.
    [InlineData(1000, 101)]
    public void Passenger_milestone_buckets(int done, int expected)
        => Assert.Equal(expected, GsxServiceAnnouncer.PassengerMilestone(done));

    [Theory]
    [InlineData(0, true)]     // service started, nobody off/on yet
    [InlineData(1, true)]     // it has actually begun
    [InlineData(7, false)]    // mid-decade first sight stays quiet
    [InlineData(20, true)]
    public void First_sight_of_a_count_announces_only_on_a_boundary(int done, bool expected)
        => Assert.Equal(expected, GsxServiceAnnouncer.ShouldAnnouncePassengers(done, null));

    [Theory]
    [InlineData(0, true)]
    [InlineData(37, false)]
    [InlineData(40, true)]
    [InlineData(100, true)]
    public void First_sight_of_a_bag_percentage_announces_only_on_a_step(int percent, bool expected)
        => Assert.Equal(expected, GsxServiceAnnouncer.ShouldAnnounceBags(percent, null));

    [Theory]
    [InlineData(45, 4, false)]   // still inside the bucket already spoken
    [InlineData(52, 4, true)]    // crossed into the next one
    [InlineData(100, 9, true)]   // completion always lands in its own bucket
    [InlineData(100, 10, false)] // …and is never repeated
    public void A_later_bag_percentage_announces_on_a_bucket_change(int percent, int lastSpoken, bool expected)
        => Assert.Equal(expected, GsxServiceAnnouncer.ShouldAnnounceBags(percent, lastSpoken));
}
