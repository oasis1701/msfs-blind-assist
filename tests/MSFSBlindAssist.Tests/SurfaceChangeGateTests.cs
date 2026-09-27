using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The surface-change callout. Enum values 0 (concrete), 1 (grass) and 4 (asphalt) are MEASURED
/// live in MSFS 2024 — LOWI's GA apron, its 08L/26R grass strip and taxiway Alpha; the rest of the
/// table is the published SDK enum, which is why an unrecognised value must stay silent rather
/// than guess.
///
/// <para>Every distance fed here is what the monitor ACTUALLY feeds: one sample per
/// <see cref="AirportSurroundingsMonitor.PollMs"/>, so the metres a sample carries follow from the
/// ground speed — 10.3 m at 10 kt, 15.4 m at 15 kt. This file used to feed 2 m samples, and a gate
/// that credited a new surface's FIRST reading with the whole distance since the previous one
/// passed every test in it while a single 15 kt poll on the grass confirmed on its own (PR #230
/// review, SC-2): at 2 m a sample the two rules cannot be told apart.</para>
/// </summary>
public class SurfaceChangeGateTests
{
    private const int Concrete = 0, Grass = 1, Asphalt = 4, Gravel = 14, MadeUp = 99;

    /// <summary>Metres covered in ONE monitor poll at this ground speed.</summary>
    private static double PerPoll(double kts) => kts * 1852.0 / 3600.0 * (AirportSurroundingsMonitor.PollMs / 1000.0);

    /// <summary>One poll on <paramref name="surface"/> at <paramref name="kts"/>, carrying that
    /// poll's distance.</summary>
    private static string? OnePoll(SurfaceChangeGate gate, int surface, double kts)
        => gate.Evaluate(surface, true, onGround: true, groundSpeedKts: kts, metresSinceLast: PerPoll(kts));

    /// <summary>Feeds polls at <paramref name="gs"/> until the gate speaks or
    /// <paramref name="metres"/> have been covered.</summary>
    private static string? Drive(SurfaceChangeGate gate, int surface, double metres, double gs = 10.0)
    {
        Assert.True(gs > 0, "Drive covers distance; feed a stopped aircraft through Evaluate directly");
        string? said = null;
        for (double d = 0; d < metres; d += PerPoll(gs))
            said ??= OnePoll(gate, surface, gs);
        return said;
    }

    private static SurfaceChangeGate OnPavement()
    {
        var gate = new SurfaceChangeGate();
        Drive(gate, Asphalt, 40);                       // baseline: silent, establishes "paved"
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
        return gate;
    }

    [Fact]
    public void The_first_surface_of_a_session_is_a_silent_baseline()
    {
        // Spawning on grass is not leaving a taxiway. The pilot is told what they are on only
        // when they DRIVE onto something different.
        var gate = new SurfaceChangeGate();
        Assert.Null(Drive(gate, Grass, 200));
        Assert.Equal(SurfaceFamily.Grass, gate.AnnouncedFamily);
    }

    [Fact]
    public void Leaving_the_pavement_leads_with_the_fact_that_it_was_left()
    {
        var gate = OnPavement();
        Assert.Equal("Off the pavement, on grass.", Drive(gate, Grass, 40));
    }

    [Fact]
    public void Returning_to_the_pavement_says_so_plainly()
    {
        var gate = OnPavement();
        Drive(gate, Grass, 40);
        Assert.Equal("Back on pavement.", Drive(gate, Asphalt, 40));
    }

    [Fact]
    public void Asphalt_to_concrete_is_never_spoken()
    {
        // Measured FOUR times on one 2.35 km LOWI taxi — the apron is concrete and taxiway Alpha
        // is asphalt. A pilot can act on none of it, and announcing it would bury the one that
        // matters.
        var gate = OnPavement();
        Assert.Null(Drive(gate, Concrete, 400));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void A_one_poll_clip_of_grass_at_15_knots_is_not_a_departure()
    {
        // A wheel catching the edge on a tight turn and coming straight back is ONE poll on the
        // grass. That poll arrives 15.4 m after the last asphalt reading — MORE than ConfirmMetres —
        // and how much of those metres lay on the grass is unknowable. The gate once credited all
        // of them to the grass reading, so this clip was announced, which teaches a pilot to
        // ignore the callout that matters.
        Assert.True(PerPoll(15.0) > SurfaceChangeGate.ConfirmMetres,
            "the regression this pins needs one 15 kt poll to exceed the confirmation distance");

        var gate = OnPavement();
        Assert.Null(OnePoll(gate, Grass, 15.0));
        Assert.Null(OnePoll(gate, Asphalt, 15.0));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void Rolling_on_across_the_grass_at_15_knots_confirms_on_the_second_poll()
    {
        // The excursion that matters: the second reading on the grass comes 15.4 m after the
        // first, and that distance is KNOWN to be on it.
        var gate = OnPavement();
        Assert.Null(OnePoll(gate, Grass, 15.0));
        Assert.Equal("Off the pavement, on grass.", OnePoll(gate, Grass, 15.0));
    }

    [Fact]
    public void At_10_knots_it_takes_a_third_poll()
    {
        // 10.3 m a poll: one poll's worth of known grass is still short of ConfirmMetres, two are not.
        Assert.True(PerPoll(10.0) < SurfaceChangeGate.ConfirmMetres);
        Assert.True(2 * PerPoll(10.0) >= SurfaceChangeGate.ConfirmMetres);

        var gate = OnPavement();
        Assert.Null(OnePoll(gate, Grass, 10.0));
        Assert.Null(OnePoll(gate, Grass, 10.0));
        Assert.Equal("Off the pavement, on grass.", OnePoll(gate, Grass, 10.0));
    }

    [Fact]
    public void The_first_reading_of_a_new_surface_is_credited_nothing_however_far_it_came()
    {
        // The direct pin on the rule, and on ConfirmMetres itself: whatever distance arrives WITH
        // the first grass reading — here 200 m, a sampling pause just under the 250 m jump test —
        // was driven from where the old surface was last read. Only what follows counts, to the
        // metre (11.5 + 0.5 is exactly 12.0 in binary, so the boundary is not a rounding accident).
        // LITERAL metres, never derived from ConfirmMetres: "ConfirmMetres - 0.5, then 0.5" sums to
        // the constant whatever it is and would pin nothing. These pin it to (11.5, 12].
        var gate = OnPavement();
        Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 10.0, metresSinceLast: 200.0));
        Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 10.0, metresSinceLast: 11.5));
        Assert.Equal("Off the pavement, on grass.",
            gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 10.0, metresSinceLast: 0.5));
    }

    [Fact]
    public void A_stop_on_the_new_surface_holds_the_evidence_and_rolling_on_adds_to_it()
    {
        // Read on the grass while stopped, then read on it again 15.4 m later: both ends of that
        // distance are on the grass, so it counts. A stop neither confirms nor loses anything —
        // which is why the first reading opens its evidence BEFORE the speed test.
        var gate = OnPavement();
        Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 0.0, metresSinceLast: 0.0));
        Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 0.0, metresSinceLast: 0.0));
        Assert.Equal("Off the pavement, on grass.", OnePoll(gate, Grass, 15.0));
    }

    [Fact]
    public void Confirmation_is_distance_not_time_so_a_stopped_aircraft_never_accumulates_it()
    {
        // A DA40 steers on differential braking, so a taxi is stop-start by nature. Sitting still
        // on a new surface for any number of samples must never confirm it.
        var gate = OnPavement();
        for (int i = 0; i < 500; i++)
            Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 0.0, metresSinceLast: 0.0));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_non_finite_distance_is_no_distance(double metres)
    {
        // Math.Max(0, NaN) is NaN, and NaN is never below ConfirmMetres — a NaN once confirmed on
        // the spot. The haversine itself returns NaN for some finite position pairs (nearly
        // antipodal ones), so this is not only an unreadable-position problem.
        var gate = OnPavement();
        Assert.Null(OnePoll(gate, Grass, 10.0));                  // the evidence opens
        for (int i = 0; i < 50; i++)
            Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 10.0, metresSinceLast: metres));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void A_non_finite_ground_speed_accumulates_nothing()
    {
        // NaN fails every comparison, so "below MinSpeedKts" read FALSE for it and a NaN speed was
        // treated as moving. It must fail closed.
        var gate = OnPavement();
        for (int i = 0; i < 50; i++)
            Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: double.NaN, metresSinceLast: PerPoll(10.0)));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void An_unrecognised_enum_value_is_silent_and_does_not_disturb_what_the_pilot_was_told()
    {
        // Only three values in the table are measured. A value this build does not name must not
        // become "you have left the pavement", and must not reset the pilot's understanding.
        var gate = OnPavement();
        Assert.Null(Drive(gate, MadeUp, 400));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
        Assert.Equal("Off the pavement, on grass.", Drive(gate, Grass, 40));
    }

    [Fact]
    public void An_invalid_surface_reading_is_silent()
    {
        var gate = OnPavement();
        for (int i = 0; i < 200; i++)
            Assert.Null(gate.Evaluate(Grass, surfaceInfoValid: false, onGround: true, groundSpeedKts: 10, metresSinceLast: PerPoll(10.0)));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void Airborne_samples_are_silent()
    {
        var gate = OnPavement();
        for (int i = 0; i < 200; i++)
            Assert.Null(gate.Evaluate(Grass, true, onGround: false, groundSpeedKts: 120, metresSinceLast: PerPoll(120.0)));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void A_reset_makes_the_next_surface_a_baseline_again()
    {
        // A teleport, an aircraft change or a new flight: the pilot has been PLACED somewhere,
        // they have not driven off anything.
        var gate = OnPavement();
        gate.Reset();
        Assert.Null(Drive(gate, Grass, 400));
        Assert.Equal(SurfaceFamily.Grass, gate.AnnouncedFamily);
    }

    [Fact]
    public void Unpaved_is_its_own_family_and_is_named_as_such()
    {
        var gate = OnPavement();
        Assert.Equal("Off the pavement, on an unpaved surface.", Drive(gate, Gravel, 40));
    }

    [Fact]
    public void Grass_to_gravel_neither_leaves_nor_regains_pavement()
    {
        var gate = new SurfaceChangeGate();
        Drive(gate, Grass, 40);                      // baseline
        Assert.Equal("Now on an unpaved surface.", Drive(gate, Gravel, 40));
    }

    // ---- Leaving the pavement is ONE excursion across the non-paved families (review A3-6) ----

    [Fact]
    public void Leaving_the_pavement_over_mixed_ground_is_one_excursion_at_15_knots()
    {
        // Mottled ground at the edge of a taxiway: one reading grass, the next gravel. Kept per
        // family, each change reset the evidence and the drift was never announced. Leaving the
        // pavement pools every non-paved family, and the CONFIRMING reading names the sentence.
        var gate = OnPavement();
        Assert.Null(OnePoll(gate, Grass, 15.0));                     // opens the excursion at 0 m
        Assert.Equal("Off the pavement, on an unpaved surface.", OnePoll(gate, Gravel, 15.0));
    }

    [Fact]
    public void At_10_knots_mixed_ground_confirms_on_the_third_reading_and_is_named_by_it()
    {
        // 10.3 m a poll: the gravel reading carries the evidence to 10.3 m, the next grass reading
        // to 20.6 m — and it is that grass reading that confirms, so it names the surface.
        var gate = OnPavement();
        Assert.Null(OnePoll(gate, Grass, 10.0));
        Assert.Null(OnePoll(gate, Gravel, 10.0));
        Assert.Equal("Off the pavement, on grass.", OnePoll(gate, Grass, 10.0));
    }

    [Fact]
    public void Only_leaving_the_pavement_pools_the_families()
    {
        // Already off the pavement (grass announced): one gravel reading and then one asphalt
        // reading are two different changes, each with nothing behind it. A bucket merged for
        // EVERY announced family would add the two up and say "Back on pavement." after a single
        // asphalt reading.
        var gate = new SurfaceChangeGate();
        Drive(gate, Grass, 40);                                      // baseline: grass
        Assert.Null(OnePoll(gate, Gravel, 15.0));
        Assert.Null(OnePoll(gate, Asphalt, 15.0));
        Assert.Equal(SurfaceFamily.Grass, gate.AnnouncedFamily);
    }

    [Fact]
    public void Flapping_between_non_paved_families_off_the_pavement_stays_silent()
    {
        // Already told "grass": a grass reading resets any pending change, so gravel that never
        // holds for two readings in a row is never announced.
        var gate = new SurfaceChangeGate();
        Drive(gate, Grass, 40);                                      // baseline: grass
        Assert.Null(OnePoll(gate, Gravel, 15.0));
        Assert.Null(OnePoll(gate, Grass, 15.0));
        Assert.Null(OnePoll(gate, Gravel, 15.0));
        Assert.Equal(SurfaceFamily.Grass, gate.AnnouncedFamily);
    }

    [Theory]
    [InlineData(0, SurfaceFamily.Paved)]     // measured: LOWI GA apron
    [InlineData(1, SurfaceFamily.Grass)]     // measured: LOWI 08L/26R grass strip
    [InlineData(4, SurfaceFamily.Paved)]     // measured: EHAM stands, LOWI taxiway Alpha
    [InlineData(2, SurfaceFamily.Water)]
    [InlineData(8, SurfaceFamily.SnowOrIce)]
    [InlineData(9, SurfaceFamily.SnowOrIce)]
    [InlineData(14, SurfaceFamily.Unpaved)]
    [InlineData(99, SurfaceFamily.Unknown)]
    public void The_family_table(int simEnum, SurfaceFamily expected)
        => Assert.Equal(expected, SurfaceFamilies.Of(simEnum));
}
