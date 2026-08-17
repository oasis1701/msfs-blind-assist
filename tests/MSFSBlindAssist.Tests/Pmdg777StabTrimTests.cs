// PMDG 777 stabiliser trim: degrees -> units.
//
// The FMC TAKEOFF page asks for a trim setting in units and the control-stand indicator is
// marked in units, but the stock ELEVATOR TRIM POSITION SimVar the app reads is in degrees. A
// pilot handed degrees has to convert under time pressure during the takeoff setup, and getting
// it wrong is an over-rotation — which is exactly what happened repeatedly before the offset was
// worked out. The 737 never had this problem: PMDG publishes its trim as an L-var already in
// units, so nothing needed converting.
//
// The offset is MEASURED, not derived — the stabiliser run to both stops with the SimVar read at
// each end — and independently validated by flying takeoffs at (FMC units - 3.75).

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

    [Fact]
    public void The_bottom_stop_never_speaks_a_negative_zero()
    {
        // -0.0 == 0.0 compares true but formats as "-0.00", which at the bottom stop would
        // announce a negative trim on a scale whose lowest graduation is zero.
        Assert.DoesNotContain("-", Pmdg777StabTrim.Describe(-3.75));
        Assert.Equal("Trim 0.00 units", Pmdg777StabTrim.Describe(-3.75));
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

    [Fact]
    public void A_quarter_unit_step_is_a_new_announcement_and_less_is_not()
    {
        // The debounce keys on the value this returns, so quantising here is what makes the
        // callout step a quarter at a time instead of speaking every hundredth of a degree.
        double at = Pmdg777StabTrim.UnitsFromDegrees(0.00);
        Assert.Equal(at, Pmdg777StabTrim.UnitsFromDegrees(0.05), 1e-9);   // still the same quarter
        Assert.NotEqual(at, Pmdg777StabTrim.UnitsFromDegrees(0.25));      // a genuine step
    }

    [Fact]
    public void The_offset_and_step_are_the_only_knobs()
    {
        // Both are stated as constants precisely so a future correction is a one-line change.
        // The residual quarter-unit ambiguity (3.75 vs PMDG's config-comment 4.0) cannot be
        // settled by reading an analogue gauge marked in whole units, so this is where it lands
        // if better evidence ever appears.
        Assert.Equal(3.75, Pmdg777StabTrim.UnitsOffset);
        Assert.Equal(0.25, Pmdg777StabTrim.UnitsStep);
    }
}
