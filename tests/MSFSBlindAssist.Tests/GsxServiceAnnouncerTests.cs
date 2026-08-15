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

    // ── Operator attribution (restored — the pre-Remote-API transport spoke it) ──────────

    private static GsxServiceState WithOperator(string id, string state, string? op) =>
        new() { Id = id, DisplayName = id, State = state, Operator = op, StateText = $"{id} is {state}" };

    [Fact]
    public void Available_names_the_operator_when_gsx_publishes_one()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { WithOperator("Refuel", "performing", "United Ground Express") });
        var said = a.Update(new[] { WithOperator("Refuel", "available", "United Ground Express") });
        Assert.Single(said);
        Assert.Equal("Refuel available from United Ground Express.", said[0]);
    }

    [Fact]
    public void Performing_names_the_operator_when_gsx_publishes_one()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { WithOperator("Deboard", "available", "OneJet") });
        var said = a.Update(new[] { WithOperator("Deboard", "performing", "OneJet") });
        Assert.Equal("Deboard in progress by OneJet.", Assert.Single(said));
    }

    [Fact]
    public void Available_without_an_operator_stays_the_plain_phrase()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { WithOperator("Catering", "performing", null) });
        var said = a.Update(new[] { WithOperator("Catering", "available", null) });
        Assert.Equal("Catering available.", Assert.Single(said));
    }

    // ── Fuel quantity — the live wire's detail.fuel {current,target,unit} — time-throttled at 30 s ──

    // Shape verified across full 0→100 % refuel runs in the live captures:
    // {"current":5914,"target":5914,"unit":"lb","startTotal":5549,"aircraftTotal":11464}.
    private static GsxServiceState Fuel(double current, double target, string unit = "lb") => new()
    {
        Id = "Refueling", DisplayName = "Refuel", State = "performing",
        FuelCurrent = current, FuelTarget = target, FuelUnit = unit,
        StateText = "Refueling service is being performed",
    };

    [Fact]
    public void Fuel_progress_speaks_current_of_target_with_unit()
    {
        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        a.Update(new[] { Fuel(0, 5914) }, t0);
        var said = a.Update(new[] { Fuel(820, 5914) }, t0.AddSeconds(2));
        Assert.Equal("Refuel 820 of 5914 lb.", Assert.Single(said));
    }

    [Fact]
    public void Fuel_progress_parses_from_the_wire_shape_and_rounds_fractional_pounds()
    {
        string json = @"[{""id"":""Refueling"",""displayName"":""Refuel"",""state"":""performing"",
            ""detail"":{""phase"":""hose connected"",""fuel"":{""current"":1234.6,""target"":5914,""unit"":""lb"",""startTotal"":5549,""aircraftTotal"":6783.6}},
            ""progressText"":""21%""}]";
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rows = GsxServiceState.ParseList(doc.RootElement.Clone());
        Assert.Equal(1234.6, rows[0].FuelCurrent);
        Assert.Equal(5914, rows[0].FuelTarget);
        Assert.Equal("lb", rows[0].FuelUnit);

        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        a.Update(new[] { Fuel(0, 5914) }, t0);
        Assert.Equal("Refuel 1235 of 5914 lb.", Assert.Single(a.Update(rows, t0.AddSeconds(2))));
    }

    [Fact]
    public void Pre_hose_fuel_row_with_no_current_or_target_is_silent()
    {
        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        var preHose = new GsxServiceState { Id = "Refueling", DisplayName = "Refuel", State = "performing", FuelUnit = "lb" };
        a.Update(new[] { preHose }, t0);
        Assert.Empty(a.Update(new[] { preHose }, t0.AddSeconds(5)));
    }

    [Fact]
    public void Generic_metered_progress_speaks_current_of_total_with_unit()
    {
        var generic = (int c) => new GsxServiceState
        {
            Id = "Water", DisplayName = "Water", State = "performing",
            ProgressCurrent = c, ProgressTotal = 400, ProgressUnit = "l",
        };
        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        a.Update(new[] { generic(0) }, t0);
        Assert.Equal("Water 120 of 400 l.", Assert.Single(a.Update(new[] { generic(120) }, t0.AddSeconds(2))));
    }

    [Fact]
    public void A_pax_unit_progress_row_without_typed_pax_detail_never_uses_the_generic_phrase()
    {
        // GSX clamps progress.total to current on pax rows ("181/181" with five still aboard),
        // so a pax-unit progress row must never reach the generic "X of Y" branch even when
        // detail.pax is absent — pins the unit guard, which the typed-pax test above does not.
        var row = (int c) => new GsxServiceState
        {
            Id = "Boarding", DisplayName = "Board", State = "performing",
            ProgressCurrent = c, ProgressTotal = c, ProgressUnit = "pax",
        };
        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        a.Update(new[] { row(100) }, t0);
        Assert.Empty(a.Update(new[] { row(103) }, t0.AddSeconds(5)));
        Assert.Empty(a.Update(new[] { row(140) }, t0.AddMinutes(5)));
    }

    [Fact]
    public void Fuel_progress_is_throttled_to_the_announcement_interval()
    {
        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        a.Update(new[] { Fuel(0, 5914) }, t0);
        Assert.Single(a.Update(new[] { Fuel(820, 5914) }, t0.AddSeconds(2)));

        // Inside the window: every tick is swallowed, however far the number moves.
        Assert.Empty(a.Update(new[] { Fuel(1500, 5914) }, t0.AddSeconds(10)));
        Assert.Empty(a.Update(new[] { Fuel(4000, 5914) }, t0.AddSeconds(31)));

        // At/after the interval since the LAST SPOKEN one: speaks again.
        var later = a.Update(new[] { Fuel(4800, 5914) },
            t0.AddSeconds(2) + GsxServiceAnnouncer.ProgressAnnouncementInterval);
        Assert.Equal("Refuel 4800 of 5914 lb.", Assert.Single(later));
    }

    [Fact]
    public void Fuel_progress_that_did_not_move_is_silent_even_after_the_interval()
    {
        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        a.Update(new[] { Fuel(0, 5914) }, t0);
        a.Update(new[] { Fuel(820, 5914) }, t0.AddSeconds(2));
        Assert.Empty(a.Update(new[] { Fuel(820, 5914) }, t0.AddMinutes(5)));
    }

    [Fact]
    public void Pax_unit_progress_never_uses_the_generic_phrase()
    {
        // The pax milestone gate owns passenger counts; the generic branch must not
        // second-guess it with "181 of 181 pax" (GSX clamps progress.total to current).
        var a = new GsxServiceAnnouncer();
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        var row0 = new GsxServiceState { Id = "Deboarding", DisplayName = "Deboard", State = "performing",
            PaxDone = 100, PaxTotal = 186, ProgressCurrent = 100, ProgressTotal = 100, ProgressUnit = "pax" };
        var row1 = new GsxServiceState { Id = "Deboarding", DisplayName = "Deboard", State = "performing",
            PaxDone = 103, PaxTotal = 186, ProgressCurrent = 103, ProgressTotal = 103, ProgressUnit = "pax" };
        a.Update(new[] { row0 }, t0);
        Assert.Empty(a.Update(new[] { row1 }, t0.AddSeconds(5))); // 103 is mid-decade: pax gate silent, generic must be too
    }
}
