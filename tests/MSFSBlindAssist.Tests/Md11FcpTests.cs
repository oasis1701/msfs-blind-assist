using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The FCP write surface — pinning facts that were established by PROBING A LIVE MD-11 and are
/// recorded nowhere else.
///
/// TFDi document the variable names and the four unit enums. They do not document units, ranges,
/// the write method, or any relationship between the read and write families. Everything asserted
/// here therefore has exactly one source: the probe run on 2026-07-17. If someone later "tidies"
/// one of these constants on the strength of the docs, nothing in the docs will contradict them —
/// these tests are the only record.
/// </summary>
public class Md11FcpTests
{
    /// <summary>
    /// The read and write families do NOT share names, and vertical speed is the trap: it is read
    /// as AFS_**VS** and written as EXTCTL_FCP_**VR**. Anyone deriving one name from the other by
    /// pattern gets a var that does not exist — which fails silently, since a write to a
    /// nonexistent L:var is a no-op.
    /// </summary>
    [Fact]
    public void VerticalSpeed_IsReadAsVsButWrittenAsVr()
    {
        Assert.Equal("MD11_AFS_VS", Md11Fcp.ReadVerticalSpeed);
        Assert.Equal("MD11_EXTCTL_FCP_VR", Md11Fcp.WriteVerticalSpeed);
        Assert.Equal("MD11_EXTCTL_FCP_VR_U", Md11Fcp.WriteVerticalSpeedUnit);
    }

    [Fact]
    public void ReadAndWriteFamilies_AreDistinctVariables()
    {
        var read = new[] { Md11Fcp.ReadSpeed, Md11Fcp.ReadHeading, Md11Fcp.ReadAltitude, Md11Fcp.ReadVerticalSpeed };
        var write = new[] { Md11Fcp.WriteSpeed, Md11Fcp.WriteHeading, Md11Fcp.WriteAltitude, Md11Fcp.WriteVerticalSpeed };

        Assert.Empty(read.Intersect(write));
        Assert.All(read, r => Assert.StartsWith("MD11_AFS_", r));
        Assert.All(write, w => Assert.StartsWith("MD11_EXTCTL_FCP_", w));
    }

    /// <summary>
    /// -1 is the EXTCTL idle sentinel. Proven: after writing 123 to MD11_EXTCTL_FCP_HDG, the var
    /// read back -1 while MD11_AFS_HDG read 123 — i.e. the FCC consumed the command and cleared
    /// the inbox. It is NOT a mirror of the window, so never read an EXTCTL var expecting the
    /// selected value.
    /// </summary>
    [Fact]
    public void ExtctlIdleSentinel_IsMinusOne()
    {
        Assert.Equal(-1, Md11Fcp.Idle);
    }

    /// <summary>
    /// The read-side dash sentinels: -999 (speed/heading) and -9999 (vertical speed). A live
    /// aircraft with no V/S selected reads MD11_AFS_VS = -9999, which must render as "dashed"
    /// rather than as a descent of nine thousand feet a minute.
    /// </summary>
    [Theory]
    [InlineData(-999, true)]
    [InlineData(-9999, true)]
    [InlineData(-1000, true)]
    [InlineData(0, false)]
    [InlineData(250, false)]
    [InlineData(-500, false)]   // a real 500 fpm descent, NOT a dash
    public void IsDashed_RecognisesTheReadbackSentinels(double value, bool expected)
    {
        Assert.Equal(expected, Md11Fcp.IsDashed(value));
    }

    /// <summary>
    /// A live-probe fact with no documentary source: writing SPD_U=1 then SPD=0.82 produced
    /// MD11_AFS_SPD = 0.81999999 — so Mach is a REAL number, not 82 and not 820. The float32
    /// round-trip is why read-outs must round rather than compare exactly.
    /// </summary>
    [Fact]
    public void MachRange_IsARealNumberNotScaled()
    {
        Assert.True(Md11Fcp.MinMach is > 0 and < 1);
        Assert.True(Md11Fcp.MaxMach is > 0 and < 1);
        Assert.True(Md11Fcp.MaxMach > Md11Fcp.MinMach);
    }

    /// <summary>
    /// The speed box accepts knots or Mach and picks the unit from the number's shape. The two
    /// bands cannot overlap — the FCP's Mach range tops out below 1 and its IAS range starts at
    /// 100 kt — so the split is unambiguous for every value either band can hold.
    /// </summary>
    [Fact]
    public void SpeedBands_CannotOverlap()
    {
        Assert.True(Md11Fcp.MaxMach < 10, "Mach band must sit entirely below the knots/Mach split");
        Assert.True(Md11Fcp.MinSpeedKnots > 10, "IAS band must sit entirely above the knots/Mach split");
    }

    /// <summary>Likewise for vertical: an FPA is single-digit degrees, a V/S is hundreds of fpm.</summary>
    [Fact]
    public void VerticalBands_CannotOverlap()
    {
        Assert.True(Md11Fcp.MaxFpaDegrees < 20, "FPA band must sit below the V/S-vs-FPA split");
        Assert.True(Md11Fcp.MaxVerticalSpeedFpm > 20, "V/S band must sit above the split");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(123, 123)]
    [InlineData(359, 359)]
    [InlineData(360, 0)]      // a compass 360 is written as 0
    [InlineData(-10, 350)]
    [InlineData(370, 10)]
    public void NormaliseHeading_WrapsOntoTheCompass(double input, double expected)
    {
        Assert.Equal(expected, Md11Fcp.NormaliseHeading(input));
    }

    /// <summary>
    /// The unit vars' polarity is the L:VAR's, which is INVERTED from the enum compiled into the
    /// binary (DWARF: HDGTrack{Track=0, HDG=1}). Glareshield.xml's own tooltip — and TFDi's docs —
    /// give the L:var as 0=Heading / 1=Track. Taking the internal enum for the L:var's would
    /// invert every heading/track read-out and write.
    /// </summary>
    [Fact]
    public void ModeVars_AreTheDocumentedLvarNames()
    {
        Assert.Equal("MD11_AP_HDG_TRK", Md11Fcp.ModeHeadingIsTrack);
        Assert.Equal("MD11_AP_IAS_MACH", Md11Fcp.ModeSpeedIsMach);
        Assert.Equal("MD11_AP_VS_FPA", Md11Fcp.ModeVerticalIsFpa);
        Assert.Equal("MD11_AP_FT_M", Md11Fcp.ModeAltitudeIsMetres);
    }

    // ---------------------------------------------------------------------------------
    // Altimeter
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Baro is its own EXTCTL command inbox, proven live: writing 29.85 to MD11_EXTCTL_CAP_BARO put
    /// 29.85 into MD11_CAP_ALTIMETER and reset the inbox to -1. It is set in the CURRENT display
    /// unit, which is why the conversion below exists.
    /// </summary>
    [Fact]
    public void Baro_ReadAndWriteVarsAreDistinct()
    {
        Assert.Equal("MD11_CAP_ALTIMETER", Md11Fcp.ReadCaptainBaro);
        Assert.Equal("MD11_EXTCTL_CAP_BARO", Md11Fcp.WriteCaptainBaro);
    }

    /// <summary>29.92 inHg is standard pressure ≡ 1013.25 hPa; the round-trip must hold.</summary>
    [Fact]
    public void BaroConversion_RoundTripsStandardPressure()
    {
        Assert.Equal(1013.25, Md11Fcp.InHgToHpa(29.92), precision: 0);
        Assert.Equal(29.92, Md11Fcp.HpaToInHg(1013.25), precision: 2);
    }

    /// <summary>
    /// The crux of the baro dialog: a typed value is converted to whatever unit the display is
    /// CURRENTLY in, so "1013" and "29.92" each do the right thing regardless of the PFD's unit.
    /// Writing a raw hPa number while the display is in inHg would command a nonsensical setting.
    /// </summary>
    [Theory]
    // display in inHg (29.92): inHg stays, hPa converts down to inHg
    [InlineData(29.85, 29.92, 29.85)]
    [InlineData(1013, 29.92, 29.92)]
    // display in hPa (1013): hPa stays, inHg converts up to hPa
    [InlineData(1005, 1013, 1005)]
    [InlineData(29.92, 1013, 1013)]
    public void BaroToDisplayUnit_ConvertsToTheCurrentDisplayUnit(double typed, double display, double expected)
    {
        Assert.Equal(expected, Md11Fcp.BaroToDisplayUnit(typed, display), precision: 0);
    }

    [Theory]
    [InlineData(29.92, false)]
    [InlineData(30.10, false)]
    [InlineData(1013, true)]
    [InlineData(900, true)]
    public void LooksLikeHpa_SplitsTheTwoUnitsCleanly(double v, bool expectHpa)
    {
        Assert.Equal(expectHpa, Md11Fcp.LooksLikeHpa(v));
    }
}
