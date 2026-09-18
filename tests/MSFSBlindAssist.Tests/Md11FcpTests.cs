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
    /// The read-side dash sentinels are EXACT values, not a range, and each belongs to ONE window:
    /// -999 on speed and heading, -9999 on vertical speed. A live aircraft with no V/S selected
    /// reads MD11_AFS_VS = -9999, which must render as "dashed" rather than as a descent of nine
    /// thousand feet a minute — but a real vertical speed runs anywhere in ±6000 fpm, and none of
    /// that range, the round -1000 and a selected -999 included, may ever read as dashed.
    /// </summary>
    [Theory]
    [InlineData(-999, true)]
    [InlineData(-998.7, true)]    // float32 noise around the sentinel
    [InlineData(-9999, false)]    // the V/S window's sentinel is not this window's
    [InlineData(-1000, false)]
    [InlineData(0, false)]
    [InlineData(250, false)]
    public void IsDashedSpeedHeading_IsExactlyMinus999(double value, bool expected)
    {
        Assert.Equal(expected, Md11Fcp.IsDashedSpeedHeading(value));
    }

    [Theory]
    [InlineData(-9999, true)]
    [InlineData(-9998.7, true)]   // float32 noise around the sentinel
    [InlineData(-999, false)]     // a selected 999 fpm descent is a VALUE on this window
    [InlineData(-1000, false)]    // a real 1000 fpm descent, NOT a dash
    [InlineData(-1500, false)]
    [InlineData(-6000, false)]
    [InlineData(-500, false)]     // a real 500 fpm descent, NOT a dash
    [InlineData(0, false)]
    public void IsDashedVerticalSpeed_IsExactlyMinus9999(double value, bool expected)
    {
        Assert.Equal(expected, Md11Fcp.IsDashedVerticalSpeed(value));
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

    /// <summary>
    /// Likewise for vertical: an FPA is single-digit degrees, a V/S is hundreds of fpm. The two
    /// bands meet on exactly one value, 0 (level off), which ResolveVerticalUnit settles from the
    /// current mode — so the NON-ZERO bands must never touch, or a second ambiguous value appears.
    /// </summary>
    [Fact]
    public void VerticalBands_CannotOverlap()
    {
        Assert.True(Md11Fcp.MaxFpaDegrees < Md11Fcp.MinVerticalSpeedFpm, "the FPA band must end below the smallest non-zero V/S");
        Assert.True(Md11Fcp.MaxVerticalSpeedFpm > Md11Fcp.MinVerticalSpeedFpm, "the V/S band must be non-empty");
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

    /// <summary>
    /// A typed standard pressure lands EXACTLY on the standard value of the display's unit. The
    /// raw conversion of 1013 to inches is 29.91, one hundredth off standard, which the read-back
    /// would then speak as a QNH ("Altimeter: 1013, 29.91") instead of "Altimeter standard".
    /// </summary>
    [Theory]
    [InlineData(1013, 29.92, 29.92)]
    [InlineData(1013.25, 29.92, 29.92)]
    [InlineData(29.92, 1013, 1013.25)]
    [InlineData(1013, 1013, 1013.25)]
    [InlineData(1014, 29.92, 29.94)]       // one hectopascal above standard is a real QNH, converted as before
    public void BaroToDisplayUnit_SnapsATypedStandardOntoTheExactStandardValue(double typed, double display, double expected)
    {
        Assert.Equal(expected, Md11Fcp.BaroToDisplayUnit(typed, display), precision: 2);
    }

    /// <summary>
    /// The read-back after Ctrl+B compares what was written with what the export shows, at one
    /// display step of the coarser unit: TFDi's tooltip renders whole hectopascals and two-decimal
    /// inches, so the export may be rounded or truncated (a written 1013.25 may read 1013; a
    /// written 1010.84 may read 1011 or 1010) — or fractional; the tolerance admits all three. A
    /// one-step miss is let through on purpose — a false "not set" is the worse failure.
    /// </summary>
    [Theory]
    [InlineData(1013.25, 1013, true)]
    [InlineData(1010.84, 1011, true)]
    [InlineData(1010.84, 1010, true)]
    [InlineData(1013.25, 1011, false)]
    [InlineData(29.85, 29.85, true)]
    [InlineData(29.678, 29.68, true)]
    [InlineData(29.678, 29.67, true)]
    [InlineData(29.85, 29.92, false)]
    [InlineData(29.92, 1013, true)]        // a display whose unit was unknown at write time: compared across units
    [InlineData(29.92, 1005, false)]
    public void AltimeterAgrees_IsOneDisplayStepOfTheCoarserUnit(double written, double readBack, bool expected)
    {
        Assert.Equal(expected, Md11Fcp.AltimeterAgrees(written, readBack));
    }

    [Fact]
    public void AltimeterShortfall_SpeaksOnlyADisagreement_InTheBKeysWords()
    {
        Assert.Null(Md11Fcp.DescribeAltimeterShortfall("Standby", 29.85, 29.85));
        Assert.Null(Md11Fcp.DescribeAltimeterShortfall("Standby", 29.85, null));   // never read back: no evidence
        Assert.Null(Md11Fcp.DescribeAltimeterShortfall("Standby", 29.85, 0));      // a missing L:var reads 0: no evidence
        Assert.Equal("Standby altimeter not set, reads 1012, 29.88",
            Md11Fcp.DescribeAltimeterShortfall("Standby", 1020, 1012));
        Assert.Equal("First Officer altimeter not set, reads standard",
            Md11Fcp.DescribeAltimeterShortfall("First Officer", 30.12, 29.92));
    }

    /// <summary>
    /// The read-back after Ctrl+B completes on the exports' DELIVERY (ReadFreshAsync), so the
    /// wait before it is only the FCC's allowance for consuming an EXTCTL inbox — the same
    /// unmeasured allowance the typed minimums give theirs — never the two batch periods the old
    /// fixed sleep had to out-wait. Beyond a couple of FCC cycles it is dead time; below a cycle
    /// the fresh read could hand back the pre-write export and speak a false "not set".
    /// </summary>
    [Fact]
    public void AltimeterReadBack_SettlesForTheInboxOnly_ThenReadsOnDelivery()
    {
        Assert.InRange(Md11Fcp.VerifyAfterMs, 500, 2000);
    }

    /// <summary>
    /// All three altimeters take a typed value through their own inbox, proven live 2026-09-06:
    /// MD11_EXTCTL_FO_BARO ← 29.85 put 29.85 in MD11_FO_ALTIMETER, MD11_EXTCTL_STBY_BARO ← 29.80
    /// put 29.8 in MD11_STBY_ALTIMETER, both inboxes back to -1. Ctrl+B writes all three from one
    /// entry, captain first.
    /// </summary>
    [Fact]
    public void AllThreeAltimeters_HaveReadAndWritePairs_CaptainFirst()
    {
        Assert.Equal(new[]
        {
            ("Captain", "MD11_CAP_ALTIMETER", "MD11_EXTCTL_CAP_BARO"),
            ("First Officer", "MD11_FO_ALTIMETER", "MD11_EXTCTL_FO_BARO"),
            ("Standby", "MD11_STBY_ALTIMETER", "MD11_EXTCTL_STBY_BARO"),
        }, Md11Fcp.Altimeters);
        Assert.Equal("MD11_FO_ALTIMETER", Md11Fcp.ReadFoBaro);
        Assert.Equal("MD11_EXTCTL_STBY_BARO", Md11Fcp.WriteStandbyBaro);
    }

    /// <summary>
    /// "Standard" writes standard pressure as a VALUE in each display's own unit (the PMDG/787
    /// dialogs' way), not a knob-push toggle that could flip an already-STD side back to QNH.
    /// </summary>
    [Theory]
    [InlineData(29.85, 29.92)]
    [InlineData(30.12, 29.92)]
    [InlineData(1005, 1013.25)]
    [InlineData(1013, 1013.25)]
    public void StandardFor_IsStandardPressureInTheDisplaysUnit(double display, double expected)
        => Assert.Equal(expected, Md11Fcp.StandardFor(display), precision: 2);

    [Theory]
    [InlineData(29.92, false)]
    [InlineData(30.10, false)]
    [InlineData(1013, true)]
    [InlineData(900, true)]
    public void LooksLikeHpa_SplitsTheTwoUnitsCleanly(double v, bool expectHpa)
    {
        Assert.Equal(expectHpa, Md11Fcp.LooksLikeHpa(v));
    }

    // ---------------------------------------------------------------------------------
    // Reading the altimeter back — both units, PMDG order, "standard" heuristic
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The var carries whichever unit the PFD shows (29.92 live at FL360, ~1013 in hPa mode) and
    /// the read-out must speak BOTH, hPa first, exactly as the PMDG 737/777 and Fenix do — the
    /// report was "B only reads inches". "standard" follows the PMDG rule (29.92 within 0.005 inHg)
    /// and, in hPa mode, admits 1013 / 1013.2 / 1013.25 (TFDi's QNE is "29.92 or 1013.2 Hp").
    /// </summary>
    [Theory]
    [InlineData(29.92, "standard")]        // the live value at FL360
    [InlineData(1013, "standard")]
    [InlineData(1013.2, "standard")]
    [InlineData(30.12, "1020, 30.12")]
    [InlineData(29.85, "1011, 29.85")]
    [InlineData(995, "995, 29.38")]
    [InlineData(1020, "1020, 30.12")]
    [InlineData(1014, "1014, 29.94")]      // one hectopascal above standard is a real QNH
    public void DescribeAltimeter_SpeaksBothUnits_AndStandard(double reading, string expected)
    {
        Assert.Equal(expected, Md11Fcp.DescribeAltimeter(reading));
    }

    [Fact]
    public void AltimeterBothUnits_UsesThePmdgFactor_SoTheAircraftAgree()
    {
        var fromInches = Md11Fcp.AltimeterBothUnits(30.12);
        Assert.Equal(1020, fromInches.Hpa);
        Assert.Equal(30.12, fromInches.InHg);

        var fromHpa = Md11Fcp.AltimeterBothUnits(1013);
        Assert.Equal(1013, fromHpa.Hpa);
        Assert.Equal(29.91, Math.Round(fromHpa.InHg, 2));
    }

    [Theory]
    [InlineData(29.92, true)]
    [InlineData(29.93, false)]
    [InlineData(1013.25, true)]
    [InlineData(1012.4, false)]
    public void IsStandard_UsesTheToleranceOfTheDisplayedUnit(double reading, bool expected)
    {
        Assert.Equal(expected, Md11Fcp.IsStandard(reading));
    }
}
