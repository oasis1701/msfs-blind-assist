using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Several DISTINCT features legitimately carry one SYNTHESIZED name — NavdataFeatureSource makes
/// a "Cargo ramp" per single-linkage stand cluster, a "Fuel" per fuel cluster, a "GA ramp" per GA
/// cluster — and the gate's identity is kind + name + POSITION, so each is its own track and each
/// can fire. The pilot then hears the same sentence several times about several different
/// buildings with nothing to tell them apart.
///
/// <para>MEASURED on a routed stand-to-runway taxi at each airport, through the production
/// catalog, Rank and gate:</para>
///
/// <code>
///          callouts  distinct  repeated
/// KATL           12         8         4   <- "Cargo ramp" x5
/// OMDB            7         5         2
/// NZAA            2         1         1
/// KSFO           12        12         0
/// LSZH            3         3         0
/// </code>
///
/// <para>KSFO is the control: twelve callouts, twelve different buildings, nothing suppressed.
/// The rule therefore targets SYNTHESIZED names only (<see cref="AirportFeature.NameIsGeneric"/>)
/// — a proper name repeating is either the same building approached twice, which the per-feature
/// memory already covers, or two genuinely different piers that happen to share a name, which is
/// real information a pilot can act on.</para>
/// </summary>
public class PassingCalloutSameNameTests
{
    private const double M = 1.0 / 111_132.0;   // one metre of latitude, in degrees

    private static AirportFeature Cargo(double northMetres, bool generic = true) => new()
    {
        Kind = FeatureKind.Cargo,
        Name = "Cargo ramp",
        NameIsGeneric = generic,
        Lat = northMetres * M,
        Lon = 0,
        Source = FeatureSource.Navdata,
    };

    private static AirportFeature Named(string name, double northMetres) => new()
    {
        Kind = FeatureKind.Cargo,
        Name = name,
        NameIsGeneric = false,
        Lat = northMetres * M,
        Lon = 0,
        Source = FeatureSource.Osm,
    };

    /// <summary>Drives past one feature: in from far, to its closest point, then away — the shape
    /// the gate needs (closing by MinApproachMetres, then opening by OpeningMetres, abeam).</summary>
    private static List<NearbyFeature> At(AirportFeature f, double distance, double relBearing)
        => new() { new NearbyFeature(f, distance, relBearing) };

    private static string? DrivePast(PassingCalloutGate gate, AirportFeature f, DateTime t)
    {
        string? said = null;
        // closing 90 -> 40, then opening to 60. Abeam throughout.
        foreach (double d in new[] { 90.0, 70.0, 55.0, 40.0, 50.0, 60.0 })
        {
            var hit = gate.Evaluate(At(f, d, 90.0), 12.0, t);
            if (hit != null && said == null) said = hit.Feature.SpokenName;
            t = t.AddSeconds(2);
        }
        return said;
    }

    [Fact]
    public void One_generic_name_is_not_said_again_for_a_different_feature()
    {
        var gate = new PassingCalloutGate();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Cargo ramp", DrivePast(gate, Cargo(0), t));

        // A DIFFERENT cluster, far enough away to be its own track, a minute later.
        Assert.Null(DrivePast(gate, Cargo(5000), t.AddMinutes(1)));
        Assert.Null(DrivePast(gate, Cargo(9000), t.AddMinutes(2)));
    }

    [Fact]
    public void The_same_generic_name_is_available_again_after_the_window()
    {
        var gate = new PassingCalloutGate();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Cargo ramp", DrivePast(gate, Cargo(0), t));
        Assert.Equal("Cargo ramp", DrivePast(gate, Cargo(5000), t + PassingCalloutGate.SameNameRepeat + TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void A_PROPER_name_is_held_too_because_the_scenery_repeats_them_most()
    {
        // This assertion is the REVERSE of what it was when the rule first landed, and the
        // reversal is the point. Keyed on NameIsGeneric it covered only synthesized labels, on
        // the strength of four airports where no proper name repeated. Surveying the whole
        // installed set said otherwise: 64 of 109 airports carry a repeated announceable name,
        // 301 of 1,328 scenery features duplicate one, and RJFF has THIRTY called "Fuk City
        // Hangar". Those are proper names, so the narrow rule missed the larger half.
        var gate = new PassingCalloutGate();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("DS Hangar", DrivePast(gate, Named("DS Hangar", 0), t));
        Assert.Null(DrivePast(gate, Named("DS Hangar", 5000), t.AddMinutes(1)));
    }

    [Fact]
    public void Different_proper_names_are_all_spoken()
    {
        // The KSFO control: twelve callouts, twelve different buildings, nothing suppressed. The
        // rule must key on the WORDS, so distinct names never interfere with one another.
        var gate = new PassingCalloutGate();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("FedEx Cargo", DrivePast(gate, Named("FedEx Cargo", 0), t));
        Assert.Equal("UPS Cargo", DrivePast(gate, Named("UPS Cargo", 5000), t.AddMinutes(1)));
        Assert.Equal("DHL Cargo", DrivePast(gate, Named("DHL Cargo", 9000), t.AddMinutes(2)));
    }

    [Fact]
    public void Different_generic_names_do_not_suppress_each_other()
    {
        var gate = new PassingCalloutGate();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Cargo ramp", DrivePast(gate, Cargo(0), t));

        var fuel = new AirportFeature
        {
            Kind = FeatureKind.Fuel, Name = "Fuel", NameIsGeneric = true,
            Lat = 5000 * M, Lon = 0, Source = FeatureSource.Navdata,
        };
        Assert.Equal("Fuel", DrivePast(gate, fuel, t.AddMinutes(1)));
    }

    [Fact]
    public void A_reset_clears_the_generic_name_memory_with_everything_else()
    {
        var gate = new PassingCalloutGate();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Cargo ramp", DrivePast(gate, Cargo(0), t));
        gate.Reset();
        Assert.Equal("Cargo ramp", DrivePast(gate, Cargo(5000), t.AddMinutes(1)));
    }

    [Fact]
    public void Rebaselining_tracks_keeps_the_generic_name_memory()
    {
        // RebaselineTracks forgets approaches in progress after a catalog swap. What the PILOT has
        // already been told is a fact about the pilot, not about the catalog — the same reason the
        // fired memory and the global gap survive it.
        var gate = new PassingCalloutGate();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Cargo ramp", DrivePast(gate, Cargo(0), t));
        gate.RebaselineTracks();
        Assert.Null(DrivePast(gate, Cargo(5000), t.AddMinutes(1)));
    }
}
