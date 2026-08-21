// PMDG 777 stabiliser trim: degrees -> units. Why, and how the offset was measured, lives on
// Pmdg777StabTrim (class doc + UnitsOffset) — this file only pins the behaviour.

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class Pmdg777StabTrimTests
{
    [Fact]
    public void The_measured_stops_map_onto_the_indicator_scale()
    {
        // The two readings the whole conversion rests on: full nose-down and full nose-up.
        Assert.Equal(0.00, Pmdg777StabTrim.UnitsFromDegrees(-3.75), 1e-9);
        Assert.Equal(14.50, Pmdg777StabTrim.UnitsFromDegrees(10.75), 1e-9);
    }

    [Theory]
    [InlineData(-3.75)]   // the stop itself: the sum is exactly +0.0
    [InlineData(-3.76)]   // a hair under it: the raw snap is -0.0, which "F2" renders as "-0.00"
    [InlineData(-3.80)]
    public void At_and_just_below_the_bottom_stop_the_phrase_never_carries_a_minus(double degrees)
    {
        // -0.0 == 0.0 compares true but formats as "-0.00"; only a reading just BELOW -3.75
        // reaches that path, so the stop alone cannot pin the normalisation.
        Assert.Equal("Trim 0.00 units", Pmdg777StabTrim.Describe(degrees));
        Assert.False(double.IsNegative(Pmdg777StabTrim.UnitsFromDegrees(degrees)));
    }

    [Fact]
    public void Past_the_bottom_stop_is_clamped_to_zero_but_the_top_is_left_raw()
    {
        // A scale that starts at zero must never speak a negative trim, whatever the SimVar does
        // past the nose-down stop; a reading past 14.50 is deliberately left visible (it is the one
        // signal that PMDG's stop or the offset has moved).
        Assert.Equal(0.00, Pmdg777StabTrim.UnitsFromDegrees(-4.00), 1e-9);
        Assert.Equal("Trim 0.00 units", Pmdg777StabTrim.Describe(-11.0));
        Assert.Equal(14.75, Pmdg777StabTrim.UnitsFromDegrees(11.00), 1e-9);
    }

    [Theory]
    [InlineData(0.34, 4.00)]     // the value BA logged at the gate, pre-departure: 4.09 -> 4.00
    [InlineData(0.35, 4.00)]     // 4.10 is NOT a quarter unit, so it snaps — see the test below
    [InlineData(-2.00, 1.75)]
    [InlineData(1.25, 5.00)]
    [InlineData(-1.50, 2.25)]
    public void Degrees_convert_by_the_measured_offset(double degrees, double expectedUnits)
    {
        Assert.Equal(expectedUnits, Pmdg777StabTrim.UnitsFromDegrees(degrees), 1e-9);
    }

    [Fact]
    public void A_raw_offset_that_is_not_a_quarter_unit_is_snapped_not_spoken()
    {
        // Worth pinning explicitly because it surprises: adding the offset to a degrees reading
        // usually lands BETWEEN graduations (0.35 + 3.75 = 4.10), and the indicator has no 4.10.
        // Announcing it would report a precision the aircraft does not display, so the callout
        // reads the nearest quarter — exactly what a pilot looking at the gauge would.
        Assert.Equal(4.10, 0.35 + Pmdg777StabTrim.UnitsOffset, 1e-9);   // the raw sum
        Assert.Equal(4.00, Pmdg777StabTrim.UnitsFromDegrees(0.35), 1e-9); // what is said
    }

    [Fact]
    public void Everything_lands_on_a_quarter_unit()
    {
        // The indicator is graduated in quarter units, so a finer answer would report a
        // precision the aircraft does not display.
        for (double deg = -3.75; deg <= 10.75; deg += 0.01)
        {
            double units = Pmdg777StabTrim.UnitsFromDegrees(deg);
            double remainder = Math.Abs(units / Pmdg777StabTrim.UnitsStep
                                        - Math.Round(units / Pmdg777StabTrim.UnitsStep));
            Assert.True(remainder < 1e-9, $"{deg:F2}° gave {units} — not a quarter unit");
        }
    }

    [Fact]
    public void The_conversion_is_monotonic_so_trimming_one_way_never_reads_back_the_other()
    {
        double previous = double.NegativeInfinity;
        for (double deg = -3.75; deg <= 10.75; deg += 0.05)
        {
            double units = Pmdg777StabTrim.UnitsFromDegrees(deg);
            Assert.True(units >= previous, $"units went backwards at {deg:F2}°");
            previous = units;
        }
    }

    [Fact]
    public void The_phrase_carries_no_direction_word()
    {
        // Deliberate: the sign is already in the number on a 0-14.5 scale, and "up"/"down"
        // invites the pilot to hear a relative change rather than the absolute position both
        // the FMC and the indicator state. Requested explicitly by the pilot who uses it.
        string phrase = Pmdg777StabTrim.Describe(1.25);
        Assert.Equal("Trim 5.00 units", phrase);
        Assert.DoesNotContain("up", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("down", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("de-DE")]   // comma decimal separator
    [InlineData("fr-FR")]   // comma decimal separator
    [InlineData("en-US")]   // the CI default, kept so the invariant case is still covered
    public void The_phrase_is_invariant_formatted(string cultureName)
    {
        // Under en-US this passes with or without InvariantCulture, so the culture MUST be swapped
        // for the assertion to mean anything (same idiom as GsxBillingTests): a de-DE pilot would
        // otherwise hear "Trim 5,25 units", and this suite would be red on a de-DE dev machine.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            string phrase = Pmdg777StabTrim.Describe(1.50);
            Assert.Equal("Trim 5.25 units", phrase);
            Assert.DoesNotContain(",", phrase, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Staying_inside_a_quarter_is_silent_but_crossing_a_boundary_announces_however_small_the_move()
    {
        // The debounce keys on the value this returns: the spoken VALUE always steps by a
        // quarter, but the TRIGGER is a rounding-boundary crossing — there is no deadband, so a
        // 0.01° move across 0.125° is a new callout while 0.05° inside a bin is not.
        double at = Pmdg777StabTrim.UnitsFromDegrees(0.00);
        Assert.Equal(at, Pmdg777StabTrim.UnitsFromDegrees(0.05), 1e-9);   // still the same quarter
        Assert.NotEqual(at, Pmdg777StabTrim.UnitsFromDegrees(0.25));      // a full quarter: always new
        Assert.Equal(3.75, Pmdg777StabTrim.UnitsFromDegrees(0.12), 1e-9); // just below the 0.125° boundary
        Assert.Equal(4.00, Pmdg777StabTrim.UnitsFromDegrees(0.13), 1e-9); // 0.01° later: a new callout
    }

    [Fact]
    public void The_offset_and_step_are_the_only_knobs()
    {
        // Pinned to their literals so a correction is a deliberate two-file edit (the provenance
        // and the indicator check that rule out 4.0 live on Pmdg777StabTrim.UnitsOffset).
        Assert.Equal(3.75, Pmdg777StabTrim.UnitsOffset);
        Assert.Equal(0.25, Pmdg777StabTrim.UnitsStep);
    }

    // ---- the seam: what ProcessSimVarUpdate actually receives ---------------------------------
    // The debounce itself runs inside ProcessSimVarUpdate against a real ScreenReaderAnnouncer and
    // is sim-verified; the protected virtual it keys on is pure, so probe subclasses pin it here.

    private sealed class Probe777 : PMDG777Definition
    {
        public (double Key, string Phrase) Trim(double degrees) => DescribeElevatorTrim(degrees);
    }

    private sealed class ProbeDefault : PMDG737Definition   // does NOT override the seam
    {
        public (double Key, string Phrase) Trim(double degrees) => DescribeElevatorTrim(degrees);
    }

    [Fact]
    public void The_777_keys_its_debounce_on_the_quantised_units_it_speaks()
    {
        // Dropping the override (CS0114 is only a warning) would silently revert the 777 to
        // degrees; keying on raw degrees would re-speak "Trim 4.00 units" on every hundredth.
        var probe = new Probe777();
        var (key, phrase) = probe.Trim(0.34);
        Assert.Equal(Pmdg777StabTrim.UnitsFromDegrees(0.34), key, 1e-9);
        Assert.Equal(4.00, key, 1e-9);
        Assert.Equal("Trim 4.00 units", phrase);
        Assert.Equal(key, probe.Trim(0.37).Key, 1e-9);      // same quarter -> same key -> silent
        Assert.Equal(4.25, probe.Trim(0.50).Key, 1e-9);     // next quarter -> new key
        Assert.Equal("Trim 4.25 units", probe.Trim(0.50).Phrase);
    }

    [Fact]
    public void Every_other_aircraft_still_speaks_rounded_degrees_with_a_direction_word()
    {
        // The base default is the pre-PR behaviour, byte for byte: Math.Round(deg, 2) as the key,
        // "Trim up/down N.NN" as the phrase (-0.0 reads "up 0.00"). Pinned under the invariant
        // culture because that phrase, unlike the 777's, still formats with the ambient culture.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var probe = new ProbeDefault();
            Assert.Equal((1.23, "Trim up 1.23"), probe.Trim(1.234));
            Assert.Equal((-2.5, "Trim down 2.50"), probe.Trim(-2.5));
            var (key, phrase) = probe.Trim(-0.004);
            Assert.Equal(0.0, key);
            Assert.Equal("Trim up 0.00", phrase);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
