// Characterization tests for MSFSBlindAssist.Forms.ColdTemperatureCorrectionForm.CorrectedAltitude.
//
// This method is a VERBATIM transcription of FlyByWire's EUROCONTROL cold-temperature
// correction formula (CLAUDE.md), including a redundant term
// (publishedAlt - fieldElevation + fieldElevation, which algebraically cancels to
// publishedAlt but is kept in the source to mirror FBW's TemperatureCorrectionWidget
// exactly) and a round-UP-to-10ft finish. The documented safety invariant is: a warm
// temperature (correction <= 0) must NEVER lower the published altitude - the method
// returns the published altitude unchanged in that case.
//
// Signature (Forms/ColdTemperatureCorrectionForm.cs:60):
//   public static double CorrectedAltitude(double publishedAlt, double fieldElevation, double temperatureC)
// `temperatureC` is the RAW reported aerodrome temperature (not an ISA deviation) - the
// only call site (line ~270) passes it straight from the "Reported temperature, Celsius"
// text box into the method with no ISA-deviation conversion first.
//
// This is characterization, not spec verification: golden values below were computed by
// evaluating the documented formula independently (PowerShell, IEEE-754 double arithmetic)
// and then confirmed by running these tests against the real method; if a literal ever
// disagrees with actual output, the test must be corrected to match real output, not the
// other way around. All three documented invariants (warm no-op, round-up-to-10ft,
// monotonicity) were checked and HOLD against actual behavior - no safety concern found.

using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class ColdTemperatureCorrectionTests
{
    private const double Eps = 1e-6;

    // --- (a) Warm / at-or-above-ISA => no-op (never corrects downward) --------

    [Fact]
    public void CorrectedAltitude_warm_temperature_returns_published_altitude_unchanged()
    {
        // elev 500 ft, published 2000 ft, T = +25 C (well above ISA at this elevation).
        Assert.Equal(2000.0, ColdTemperatureCorrectionForm.CorrectedAltitude(2000, 500, 25), Eps);
    }

    [Fact]
    public void CorrectedAltitude_exactly_at_ISA_temperature_is_a_no_op()
    {
        // T chosen so tAtField == 15 C exactly (correction numerator == 0):
        // T = 15 - 0.00198 * fieldElevation.
        double isaTemp = 15 - 0.00198 * 500;
        Assert.Equal(2000.0, ColdTemperatureCorrectionForm.CorrectedAltitude(2000, 500, isaTemp), Eps);
    }

    [Fact]
    public void CorrectedAltitude_just_above_ISA_is_still_a_no_op()
    {
        // 0.5 C warmer than the ISA-neutral point at this elevation -> negative raw
        // correction -> must still return the published altitude unchanged, not clamp to 0.
        double justAboveIsa = 15 - 0.00198 * 500 + 0.5;
        Assert.Equal(2000.0, ColdTemperatureCorrectionForm.CorrectedAltitude(2000, 500, justAboveIsa), Eps);
    }

    // --- (b) Round UP to the next 10 ft, not nearest-10 ------------------------

    [Fact]
    public void CorrectedAltitude_rounds_up_to_next_10ft_not_nearest_10()
    {
        // published 1731 ft, elev 123 ft, T = -17 C.
        // Raw correction ~200.622393 ft -> raw corrected altitude ~1931.622 ft, which sits
        // strictly between the 1930 and 1940 multiples of 10. Nearest-10 rounding would give
        // 1930; the documented ceil-to-10 behavior must give 1940.
        double result = ColdTemperatureCorrectionForm.CorrectedAltitude(1731, 123, -17);
        Assert.Equal(1940.0, result, Eps);
        Assert.True(result % 10.0 == 0.0, "result must be an exact multiple of 10 ft");
    }

    [Theory]
    [InlineData(2000, 500, -5)]
    [InlineData(2000, 500, -20)]
    [InlineData(1500, -100, -30)]
    [InlineData(1731, 123, -17)]
    public void CorrectedAltitude_is_always_a_multiple_of_10ft_when_corrected(
        double publishedAlt, double fieldElevation, double temperatureC)
    {
        double result = ColdTemperatureCorrectionForm.CorrectedAltitude(publishedAlt, fieldElevation, temperatureC);
        Assert.True(result % 10.0 == 0.0, $"result {result} must be an exact multiple of 10 ft");
    }

    // --- (c) Monotonicity: colder -> correction never decreases ----------------

    [Fact]
    public void CorrectedAltitude_never_decreases_as_temperature_drops()
    {
        const double publishedAlt = 2000;
        const double fieldElevation = 500;

        double previous = double.NegativeInfinity;
        for (int t = 10; t >= -40; t -= 5)
        {
            double result = ColdTemperatureCorrectionForm.CorrectedAltitude(publishedAlt, fieldElevation, t);
            Assert.True(
                result >= previous - Eps,
                $"correction decreased going colder: T={t} gave {result}, previous (warmer) was {previous}");
            previous = result;
        }
    }

    [Fact]
    public void CorrectedAltitude_colder_temperature_never_produces_a_smaller_correction_pairwise()
    {
        // Spot-check a colder/warmer pair directly, independent of the sweep above.
        double warmer = ColdTemperatureCorrectionForm.CorrectedAltitude(2000, 500, -10);
        double colder = ColdTemperatureCorrectionForm.CorrectedAltitude(2000, 500, -20);
        Assert.True(colder >= warmer);
    }

    // --- (d) Captured golden rows at realistic values ---------------------------

    [Fact]
    public void CorrectedAltitude_golden_published2000_elev500_minus20C()
    {
        // Realistic case from the brief: published 2000 ft, airport elev 500 ft, -20 C.
        Assert.Equal(2210.0, ColdTemperatureCorrectionForm.CorrectedAltitude(2000, 500, -20), Eps);
    }

    [Fact]
    public void CorrectedAltitude_golden_published1731_elev123_minus17C()
    {
        Assert.Equal(1940.0, ColdTemperatureCorrectionForm.CorrectedAltitude(1731, 123, -17), Eps);
    }

    [Fact]
    public void CorrectedAltitude_golden_negative_field_elevation()
    {
        // Below-sea-level airport (e.g. Schiphol-style negative elevation): elev -100 ft,
        // published 1500 ft, T = -30 C.
        Assert.Equal(1800.0, ColdTemperatureCorrectionForm.CorrectedAltitude(1500, -100, -30), Eps);
    }

    [Fact]
    public void CorrectedAltitude_golden_zero_height_above_field_is_a_no_op()
    {
        // Published altitude == field elevation (zero height above the airport, e.g. field
        // elevation itself quoted as a "published altitude"): the (publishedAlt -
        // fieldElevation) factor is zero, so the raw correction is exactly zero regardless of
        // how cold it is, and the method must return the value unchanged.
        Assert.Equal(500.0, ColdTemperatureCorrectionForm.CorrectedAltitude(500, 500, -20), Eps);
    }
}
