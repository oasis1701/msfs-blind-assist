// Characterization tests for MSFSBlindAssist.Navigation.WaypointConstraintMapper — the mapping
// from a navdata WaypointFix onto the Waypoint Flight Director's per-slot guidance parameters
// (crossing altitude + constraint type + inbound course), used when tracking a fix straight from
// the Electronic Flight Bag route viewer.
//
// Ports the golden cases from tools/WaypointFdProbe/Program.cs so CI runs them. See
// WaypointFlightDirectorGeometryTests for the rationale.
//
// The load-bearing rule under test: the constraint TYPE comes from the raw ARINC alt_descriptor
// ("" / "A" / "+" / "-" / "B") — the unambiguous source — while the NUMBERS come from the robust
// MinAltitude (=alt1) / MaxAltitude (=alt2) ints. The formatted AltitudeRestriction string is only
// a fallback for fixes that carry no raw descriptor.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class WaypointConstraintMapperTests
{
    private const double Eps = 1e-6;

    private static WaypointFix Fix(string restriction, int? min, int? max, double? course,
                                   bool isTrueCourse = false, string descriptor = "")
        => new()
        {
            Ident = "X",
            AltitudeRestriction = restriction,
            AltDescriptor = descriptor,
            MinAltitude = min,
            MaxAltitude = max,
            Course = course,
            IsTrueCourse = isTrueCourse
        };

    // --- ARINC "to altitude" legs (CA/FA/VA), which carry no fix --------------

    [Fact]
    public void The_ANUT1D_climb_leg_maps_to_a_course_and_a_terminating_altitude()
    {
        // Taken verbatim from the fs2024 navdata for VCBI ANUT1D (approach_id 48466, leg 2):
        // type CA, alt_descriptor "+", altitude1 500, course 220, and NO fix — fix_ident, fix_laty
        // and fix_lonx are all null, which is why the leg reaches the FD at (0,0).
        //
        // This is what makes such a leg trackable despite having no position: the mapper yields
        // everything needed to fly it — a course to hold and an altitude to stop the climb at. The
        // EFB's track gate tests exactly these three, so a regression here silently makes the
        // initial climb of most SIDs untrackable again.
        var (alt, upper, constraint, course) =
            WaypointConstraintMapper.FromFix(Fix("", 500, null, 220.0, descriptor: "+"));

        Assert.Equal(500.0, alt!.Value, Eps);
        Assert.Null(upper);
        Assert.Equal(AltitudeConstraintType.AtOrAbove, constraint);
        Assert.Equal(220.0, course!.Value, Eps);
    }

    // --- Formatted-string fallback path (no raw descriptor) ----------------

    [Fact]
    public void AtOrAbove_with_a_magnetic_course()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("AT OR ABOVE 5000 FT", 5000, 0, 70));
        Assert.Equal(AltitudeConstraintType.AtOrAbove, r.constraint);
        Assert.Equal(5000.0, r.crossingAltitude!.Value, Eps);
        Assert.Equal(70.0, r.course!.Value, Eps);
    }

    [Fact]
    public void AtOrBelow_with_no_course()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("AT OR BELOW 6000 FT", 6000, 0, 0));
        Assert.Equal(AltitudeConstraintType.AtOrBelow, r.constraint);
        Assert.Equal(6000.0, r.crossingAltitude!.Value, Eps);
        Assert.Null(r.course);
    }

    [Fact]
    public void Between_orders_the_bounds_low_then_high()
    {
        // alt1/alt2 order is not guaranteed by ARINC — the mapper sorts them.
        var r = WaypointConstraintMapper.FromFix(Fix("BETWEEN 24000 AND 29000 FT", 29000, 24000, 0));
        Assert.Equal(AltitudeConstraintType.Between, r.constraint);
        Assert.Equal(24000.0, r.crossingAltitude!.Value, Eps);
        Assert.Equal(29000.0, r.crossingAltitudeUpper!.Value, Eps);
    }

    [Fact]
    public void At_maps_to_a_hard_crossing()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("AT 10000 FT", 10000, 0, 0));
        Assert.Equal(AltitudeConstraintType.At, r.constraint);
        Assert.Equal(10000.0, r.crossingAltitude!.Value, Eps);
    }

    [Fact]
    public void Nothing_at_all_maps_to_lateral_only()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", null, null, 0));
        Assert.Equal(AltitudeConstraintType.None, r.constraint);
        Assert.Null(r.crossingAltitude);
        Assert.Null(r.crossingAltitudeUpper);
        Assert.Null(r.course);
    }

    [Fact]
    public void A_course_with_no_altitude_stays_a_course_leg()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", null, null, 88));
        Assert.Equal(AltitudeConstraintType.None, r.constraint);
        Assert.Equal(88.0, r.course!.Value, Eps);
    }

    [Fact]
    public void A_true_course_is_dropped_so_the_leg_flies_direct_to()
    {
        // Converting a true course would need the fix's magnetic variation at map time; the FD
        // falls back to direct-to instead of guessing.
        var r = WaypointConstraintMapper.FromFix(Fix("AT 10000 FT", 10000, 0, 120, isTrueCourse: true));
        Assert.Null(r.course);
        Assert.Equal(AltitudeConstraintType.At, r.constraint);
    }

    [Fact]
    public void A_bare_altitude_with_no_descriptor_is_treated_as_At()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", 8000, 0, 0));
        Assert.Equal(AltitudeConstraintType.At, r.constraint);
        Assert.Equal(8000.0, r.crossingAltitude!.Value, Eps);
    }

    // --- Raw ARINC descriptor path (the primary source) --------------------

    [Fact]
    public void Descriptor_plus_maps_to_AtOrAbove()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", 5000, 0, 0, descriptor: "+"));
        Assert.Equal(AltitudeConstraintType.AtOrAbove, r.constraint);
        Assert.Equal(5000.0, r.crossingAltitude!.Value, Eps);
    }

    [Fact]
    public void Descriptor_minus_maps_to_AtOrBelow()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", 6000, 0, 0, descriptor: "-"));
        Assert.Equal(AltitudeConstraintType.AtOrBelow, r.constraint);
        Assert.Equal(6000.0, r.crossingAltitude!.Value, Eps);
    }

    [Fact]
    public void Descriptor_A_maps_to_At()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", 12000, 0, 0, descriptor: "A"));
        Assert.Equal(AltitudeConstraintType.At, r.constraint);
        Assert.Equal(12000.0, r.crossingAltitude!.Value, Eps);
    }

    [Fact]
    public void Descriptor_B_with_two_bounds_maps_to_an_ordered_Between()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", 29000, 24000, 0, descriptor: "B"));
        Assert.Equal(AltitudeConstraintType.Between, r.constraint);
        Assert.Equal(24000.0, r.crossingAltitude!.Value, Eps);
        Assert.Equal(29000.0, r.crossingAltitudeUpper!.Value, Eps);
    }

    [Fact]
    public void Descriptor_B_with_one_bound_becomes_a_floor_and_is_never_dropped()
    {
        // A single-bounded block is the ARINC "floor" convention. Dropping it would silently lose
        // the only altitude the leg carries.
        var r = WaypointConstraintMapper.FromFix(Fix("", 5000, null, 0, descriptor: "B"));
        Assert.Equal(AltitudeConstraintType.AtOrAbove, r.constraint);
        Assert.Equal(5000.0, r.crossingAltitude!.Value, Eps);
    }

    [Fact]
    public void The_raw_descriptor_wins_over_a_conflicting_formatted_string()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("AT 9999 FT", 5000, 0, 0, descriptor: "+"));
        Assert.Equal(AltitudeConstraintType.AtOrAbove, r.constraint);
        Assert.Equal(5000.0, r.crossingAltitude!.Value, Eps);
    }

    // --- Guards ------------------------------------------------------------

    [Theory]
    [InlineData("A")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("B")]
    public void A_zero_altitude_is_lateral_only_whatever_the_descriptor(string descriptor)
    {
        // A zero/absent crossing altitude must drop the constraint entirely, or the FD's vertical
        // guidance would command a descent toward sea level.
        var r = WaypointConstraintMapper.FromFix(Fix("", 0, 0, 0, descriptor: descriptor));
        Assert.Equal(AltitudeConstraintType.None, r.constraint);
        Assert.Null(r.crossingAltitude);
        Assert.Null(r.crossingAltitudeUpper);
    }

    [Fact]
    public void A_negative_course_is_ignored()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", null, null, -10));
        Assert.Null(r.course);
    }

    [Fact]
    public void A_360_course_normalises_to_zero()
    {
        var r = WaypointConstraintMapper.FromFix(Fix("", null, null, 360));
        Assert.Equal(0.0, r.course!.Value, Eps);
    }

    [Fact]
    public void A_single_bound_carried_in_alt2_is_still_picked_up()
    {
        // Non-navdata sources may put the single figure in alt2 rather than alt1.
        var r = WaypointConstraintMapper.FromFix(Fix("", null, 7000, 0, descriptor: "+"));
        Assert.Equal(AltitudeConstraintType.AtOrAbove, r.constraint);
        Assert.Equal(7000.0, r.crossingAltitude!.Value, Eps);
    }
}
