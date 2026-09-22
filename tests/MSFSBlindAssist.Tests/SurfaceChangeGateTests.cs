using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The surface-change callout. Enum values 0 (concrete), 1 (grass) and 4 (asphalt) are MEASURED
/// live in MSFS 2024 — LOWI's GA apron, its 08L/26R grass strip and taxiway Alpha; the rest of the
/// table is the published SDK enum, which is why an unrecognised value must stay silent rather
/// than guess.
/// </summary>
public class SurfaceChangeGateTests
{
    private const int Concrete = 0, Grass = 1, Asphalt = 4, Gravel = 14, MadeUp = 99;

    /// <summary>Feeds samples at 2 m each until the gate speaks or the budget runs out.</summary>
    private static string? Drive(SurfaceChangeGate gate, int surface, double metres, double gs = 10.0)
    {
        string? said = null;
        for (double d = 0; d < metres; d += 2.0)
            said ??= gate.Evaluate(surface, true, onGround: true, groundSpeedKts: gs, metresSinceLast: 2.0);
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
    public void A_brief_clip_of_grass_at_a_corner_is_not_a_departure()
    {
        // A wheel catching the edge on a tight turn and coming straight back. Announcing it
        // teaches the pilot to ignore the one that matters.
        //
        // The distance here is a LITERAL, deliberately not ConfirmMetres - 2: written against the
        // constant, this test passes for any value of it (at 0 the loop body never runs and the
        // gate is never even called), which a mutation run caught — it survived ConfirmMetres
        // being zeroed, i.e. it pinned nothing at all. Six metres is a wheel-width excursion by
        // any reasonable setting of the constant.
        Assert.True(SurfaceChangeGate.ConfirmMetres > 8.0,
            "this test's literal 6 m must stay comfortably inside the confirmation distance");

        var gate = OnPavement();
        Assert.Null(Drive(gate, Grass, 6.0));
        Assert.Null(Drive(gate, Asphalt, 40));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void Confirmation_really_does_require_distance_on_the_new_surface()
    {
        // The direct pin on ConfirmMetres, independent of the speed gate: moving, on the new
        // surface, but not yet far enough. One 2 m sample must never be enough.
        var gate = OnPavement();
        Assert.Null(gate.Evaluate(Grass, true, onGround: true, groundSpeedKts: 10.0, metresSinceLast: 2.0));
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
            Assert.Null(gate.Evaluate(Grass, surfaceInfoValid: false, onGround: true, groundSpeedKts: 10, metresSinceLast: 2));
        Assert.Equal(SurfaceFamily.Paved, gate.AnnouncedFamily);
    }

    [Fact]
    public void Airborne_samples_are_silent()
    {
        var gate = OnPavement();
        for (int i = 0; i < 200; i++)
            Assert.Null(gate.Evaluate(Grass, true, onGround: false, groundSpeedKts: 120, metresSinceLast: 60));
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
