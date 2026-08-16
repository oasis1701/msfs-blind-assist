using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the MD-11's combined flap/slat handle and Dial-A-Flap thumbwheel.
///
/// These pin the two facts that are easy to get wrong and impossible to notice in the air:
///   1. Dial-A-Flap is a RANGE detent (FLAP_RNG 38–65), not a point. Nearest-value matching puts
///      RNG 60–65 into "Flap 28" — i.e. a handle in the take-off detent with the wheel toward 25°
///      reads out as 28. The range test is what prevents that.
///   2. The take-off angle is a SEPARATE fact from the handle position, so the read-out has to
///      carry both. "Dial-A-Flap" alone does not tell a pilot what they are rotating on.
/// </summary>
public class Md11FlapSystemTests
{
    private static Md11FlapSystem System() => new(Md11ControlMap.Load());

    // ---------------------------------------------------------------------------------
    // The embedded map has to load at all — everything else on this aircraft depends on it.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ControlMap_LoadsFromEmbeddedResource()
    {
        var map = Md11ControlMap.Load();

        Assert.NotEmpty(map.Controls);
        Assert.NotEmpty(map.ExportVars);
    }

    [Fact]
    public void ControlMap_ContainsFlapLeverAndDial()
    {
        var sys = System();

        Assert.NotNull(sys.Lever);
        Assert.NotNull(sys.Dial);
    }

    /// <summary>
    /// The docs list these as exports and the wasm has them, but the generator's original
    /// prefix scan missed all of them — they are single tokens or prefixes it did not cover.
    /// On an aircraft with no readable speed tape these ARE the V-speeds, so pin them.
    /// </summary>
    [Theory]
    [InlineData("MD11_V1")]
    [InlineData("MD11_VR")]
    [InlineData("MD11_V2")]
    [InlineData("MD11_VSR")]
    [InlineData("MD11_VFR")]
    [InlineData("MD11_CAP_MINIMUMS")]
    [InlineData("MD11_FO_MINIMUMS")]
    [InlineData("MD11_CAP_ALTIMETER")]
    [InlineData("MD11_ATS_STATE")]
    public void ExportVars_IncludeReadoutSurface(string varName)
    {
        Assert.Contains(varName, Md11ControlMap.Load().ExportVars);
    }

    // ---------------------------------------------------------------------------------
    // Detent structure
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Six detents, clean → fully extended. Five is the bug: that is what the tooltip's %{case}
    /// map alone yields, with Dial-A-Flap — the whole point of the thumbwheel — missing.
    /// </summary>
    [Fact]
    public void Detents_AreSixInOrderCleanToExtended()
    {
        var names = System().Detents.Select(d => d.Name).ToList();

        Assert.Equal(new[]
        {
            "Flap Up / Slat Retracted",
            "Flap 0 / Slat Extended",
            "Dial-A-Flap",
            "Flap 28",
            "Flap 35",
            "Flap 50",
        }, names);
    }

    [Fact]
    public void Detents_ExactlyOneIsTheDialDetent()
    {
        var dial = System().Detents.Where(d => d.Dial).ToList();

        Assert.Single(dial);
        Assert.Equal("Dial-A-Flap", dial[0].Name);
    }

    [Theory]
    [InlineData(0, "Flap Up / Slat Retracted")]
    [InlineData(20, "Flap 0 / Slat Extended")]
    [InlineData(70, "Flap 28")]
    [InlineData(82, "Flap 35")]
    [InlineData(100, "Flap 50")]
    public void DetentFor_ResolvesPointDetents(double rng, string expected)
    {
        Assert.Equal(expected, System().DetentFor(rng)?.Name);
    }

    /// <summary>
    /// The whole Dial-A-Flap band resolves to Dial-A-Flap. 60–65 is the regression that matters:
    /// nearest-value matching against the representative value 50 hands those to "Flap 28",
    /// because 60 is the midpoint of 50 and 70.
    /// </summary>
    [Theory]
    [InlineData(38)]
    [InlineData(45)]
    [InlineData(50)]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(63)]
    [InlineData(65)]
    public void DetentFor_ResolvesWholeDialBand(double rng)
    {
        Assert.Equal("Dial-A-Flap", System().DetentFor(rng)?.Name);
    }

    [Theory]
    [InlineData(37)]   // just below the band
    [InlineData(66)]   // just above it
    public void DetentFor_OutsideDialBand_IsNotDialAFlap(double rng)
    {
        Assert.NotEqual("Dial-A-Flap", System().DetentFor(rng)?.Name);
    }

    /// <summary>
    /// The aircraft's OWN shipped ReadyToFly state, verbatim: TFDi park the handle at
    /// targetFlapHandlePos 46.91 with commandedFlapsDeg 14.95. Not a synthetic value — it is the
    /// first flap read-out a pilot ever gets on a ReadyToFly load, before touching anything.
    ///
    /// Its worth is as external corroboration: the two shipped numbers confirm the band and the
    /// angle formula against each other. Interpolating the handle across the 38–65 band gives
    /// 10 + (46.91-38)/27*15 = 14.95°, which is TFDi's own commandedFlapsDeg to the decimal — so
    /// "the Dial-A-Flap band spans 38–65" and "the wheel spans 10–25°" are the same fact, checked
    /// from an independent source rather than from the tooltip we parsed them out of.
    ///
    /// (Nearest-value would happen to resolve 46.91 correctly, since the Dial detent's
    /// representative value is 50. The band's teeth are at 60–65 — see DetentFor_ResolvesWholeDialBand.)
    /// </summary>
    [Fact]
    public void DetentFor_ShippedReadyToFlyHandlePosition_IsDialAFlap()
    {
        var sys = System();

        Assert.Equal("Dial-A-Flap", sys.DetentFor(46.91)?.Name);
        // The shipped commandedFlapsDeg, reproduced from the band geometry.
        Assert.Equal(14.95, 10 + (46.91 - 38) / 27 * 15, precision: 2);
    }

    [Fact]
    public void DetentFor_BetweenDetents_IsNull()
    {
        // Mid-travel between 0/EXT (20) and the Dial band (38+): in transit, not a detent.
        Assert.Null(System().DetentFor(30));
    }

    // ---------------------------------------------------------------------------------
    // Dial-A-Flap degrees
    // ---------------------------------------------------------------------------------

    /// <summary>TFDi's own tooltip formula: degrees = 10 + raw / 6.6667.</summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 25)]
    public void DegreesFor_SpansTenToTwentyFive(double raw, double expectedDeg)
    {
        Assert.Equal(expectedDeg, System().DegreesFor(raw), precision: 2);
    }

    [Fact]
    public void SelectableDegrees_AreWholeDegreesTenToTwentyFive()
    {
        var degrees = System().SelectableDegrees().ToList();

        Assert.Equal(16, degrees.Count);
        Assert.Equal(10, degrees.First());
        Assert.Equal(25, degrees.Last());
    }

    /// <summary>Raw→degrees→raw must round-trip, or the combo's value cannot map back to state.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(18)]
    [InlineData(22)]
    [InlineData(25)]
    public void DialSpec_RoundTripsDegreesThroughRaw(int degrees)
    {
        var spec = System().DialSpec;

        Assert.Equal(degrees, spec.ToDegrees(spec.ToRaw(degrees)), precision: 3);
    }

    // ---------------------------------------------------------------------------------
    // Read-out wording
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The Dial-A-Flap detent must always carry its angle. Speaking "Dial-A-Flap" alone tells the
    /// pilot the handle is in the take-off detent but not what take-off setting they are about to
    /// rotate on — which is the one thing the thumbwheel exists to decide.
    /// </summary>
    [Fact]
    public void DescribePosition_DialDetent_IncludesSelectedAngle()
    {
        var sys = System();
        var raw15 = sys.DialSpec.ToRaw(15);

        Assert.Equal("Dial-A-Flap, 15 degrees", sys.DescribePosition(50, raw15));
    }

    [Fact]
    public void DescribePosition_DialDetent_TracksTheWheel()
    {
        var sys = System();

        Assert.Equal("Dial-A-Flap, 10 degrees", sys.DescribePosition(50, sys.DialSpec.ToRaw(10)));
        Assert.Equal("Dial-A-Flap, 25 degrees", sys.DescribePosition(50, sys.DialSpec.ToRaw(25)));
    }

    /// <summary>A non-dial detent has a fixed angle, so appending one would be noise.</summary>
    [Theory]
    [InlineData(0, "Flap Up / Slat Retracted")]
    [InlineData(70, "Flap 28")]
    [InlineData(100, "Flap 50")]
    public void DescribePosition_PointDetent_HasNoAngleSuffix(double rng, string expected)
    {
        Assert.Equal(expected, System().DescribePosition(rng, dialRaw: 50));
    }

    [Fact]
    public void DescribePosition_BetweenDetents_ReportsTransit()
    {
        Assert.Equal("Flaps in transit", System().DescribePosition(30, 0));
    }

    // ---------------------------------------------------------------------------------
    // Combo wiring
    // ---------------------------------------------------------------------------------

    [Fact]
    public void LeverValueDescriptions_CoverAllSixDetents()
    {
        var d = System().LeverValueDescriptions();

        Assert.Equal(6, d.Count);
        Assert.Equal("Flap Up / Slat Retracted", d[0]);
        Assert.Equal("Dial-A-Flap", d[50]);
        Assert.Equal("Flap 50", d[100]);
    }

    [Fact]
    public void DialValueDescriptions_AreKeyedByRawAndLabelledInDegrees()
    {
        var sys = System();
        var d = sys.DialValueDescriptions();

        Assert.Equal(16, d.Count);
        Assert.Equal("10 degrees", d[Math.Round(sys.DialSpec.ToRaw(10), 4)]);
        Assert.Equal("25 degrees", d[Math.Round(sys.DialSpec.ToRaw(25), 4)]);
    }

    /// <summary>Half a degree in raw units — anything inside rounds to the requested whole degree.</summary>
    [Fact]
    public void DialTolerance_IsHalfADegreeInRawUnits()
    {
        var sys = System();

        Assert.Equal(sys.DialSpec.UnitsPerDeg / 2.0, sys.DialToleranceRaw, precision: 4);
    }
}
