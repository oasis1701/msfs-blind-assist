using System.Globalization;
using MSFSBlindAssist.Aircraft;
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

    /// <summary>
    /// A thumbwheel that has not been sampled has no angle to report — and raw 0 IS an angle, the
    /// wheel's 10° end. The callers used to hand 0 in for an unsampled wheel, so right after
    /// connecting with the handle in the take-off detent the read-out said "Dial-A-Flap, 10
    /// degrees" whatever the wheel was set to. It must say the angle is not known instead.
    /// </summary>
    [Fact]
    public void DescribePosition_DialDetent_WithAnUnsampledWheel_SaysTheAngleIsNotYetRead()
    {
        var sys = System();

        Assert.Equal("Dial-A-Flap, angle not yet read", sys.DescribePosition(46.91, null));
        Assert.Equal("Dial-A-Flap, 10 degrees", sys.DescribePosition(46.91, 0));   // 0 is a real angle
    }

    /// <summary>Only the Dial-A-Flap detent needs the wheel; every other position reads the same without it.</summary>
    [Theory]
    [InlineData(0, "Flap Up / Slat Retracted")]
    [InlineData(100, "Flap 50")]
    [InlineData(30, "Flaps in transit")]
    public void DescribePosition_OutsideTheDialDetent_NeedsNoWheelSample(double rng, string expected)
    {
        Assert.Equal(expected, System().DescribePosition(rng, null));
    }

    // ---------------------------------------------------------------------------------
    // The spoken flap read-out: baseline-first, never narrating a connect
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ReadoutDecision_TheFirstCompleteText_IsRecordedSilently()
    {
        Assert.Equal((false, true), Md11FlapSystem.ReadoutDecision("", "Flap 35", complete: true));
    }

    [Fact]
    public void ReadoutDecision_AnIncompleteFirstText_IsNeitherSpokenNorTheBaseline()
    {
        Assert.Equal((false, false), Md11FlapSystem.ReadoutDecision("", "Dial-A-Flap, angle not yet read", complete: false));
    }

    [Fact]
    public void ReadoutDecision_AChangeAfterTheBaseline_IsSpokenAndRecorded()
    {
        Assert.Equal((true, true), Md11FlapSystem.ReadoutDecision("Flap 28", "Flap 35", complete: true));
        // After the baseline, even an incomplete text is news — and true.
        Assert.Equal((true, true), Md11FlapSystem.ReadoutDecision("Flap 28", "Dial-A-Flap, angle not yet read", complete: false));
    }

    [Fact]
    public void ReadoutDecision_ARepeat_IsSilent()
    {
        Assert.Equal((false, false), Md11FlapSystem.ReadoutDecision("Flap 35", "Flap 35", complete: true));
    }

    /// <summary>
    /// Connecting — or switching to the MD-11 in flight — with the handle parked in the take-off
    /// detent. The lever's first sample can land before the wheel's; played through the decision in
    /// that order, NOTHING is spoken (the complete text becomes the baseline), and the next real
    /// change is. The definition's AnnounceFlaps runs exactly this loop.
    /// </summary>
    [Fact]
    public void ReadoutDecision_ConnectingInTheDialDetent_LeverBeforeWheel_SpeaksNothing()
    {
        var sys = System();
        string last = "";
        var spoken = new List<string>();
        void Offer(double rng, double? dial)
        {
            var text = sys.DescribePosition(rng, dial);
            bool complete = dial != null || sys.DetentFor(rng)?.Dial != true;
            var (speak, record) = Md11FlapSystem.ReadoutDecision(last, text, complete);
            if (record) last = text;
            if (speak) spoken.Add(text);
        }

        Offer(46.91, null);    // the lever first; the wheel not yet read
        Offer(46.91, 33.0);    // the wheel's first sample: 15 degrees
        Assert.Empty(spoken);

        Offer(70, 33.0);       // the pilot selects 28
        Assert.Equal(new[] { "Flap 28" }, spoken);
    }

    // ---------------------------------------------------------------------------------
    // A Dial-A-Flap set: silent when it lands, a shortfall is spoken
    // ---------------------------------------------------------------------------------

    /// <summary>Only an EXACT landing is silent: the one direct write round-trips, so nothing else is a landing.</summary>
    [Theory]
    [InlineData(20, 20)]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    public void DialSetShortfall_IsSilentOnlyWhenTheWheelLandedOnThePick(int want, int got)
    {
        Assert.Null(Md11FlapSystem.DialSetShortfall(want, got));
    }

    /// <summary>
    /// A ONE-degree miss speaks. It used to be inside the walk-era ±1° tolerance, so the combo
    /// showed 25 while the wheel and the display row sat on 24 with nothing said — the
    /// silently-failed selection a shortfall sentence exists to make audible.
    /// </summary>
    [Theory]
    [InlineData(20, 19, "Dial-A-Flap 19 degrees, could not reach 20")]
    [InlineData(20, 21, "Dial-A-Flap 21 degrees, could not reach 20")]
    [InlineData(20, 17, "Dial-A-Flap 17 degrees, could not reach 20")]
    [InlineData(10, 25, "Dial-A-Flap 25 degrees, could not reach 10")]
    [InlineData(25, 23, "Dial-A-Flap 23 degrees, could not reach 25")]
    public void DialSetShortfall_NamesWhereTheWheelStopped_AndWhatWasAsked(int want, int got, string expected)
    {
        Assert.Equal(expected, Md11FlapSystem.DialSetShortfall(want, got));
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

    // ---------------------------------------------------------------------------------
    // Seeding a combo from a var that is not keyed the way the combo is
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The wheel's var is CONTINUOUS — TFDi's shipped ReadyToFly state parks it at raw 33.0
    /// (14.95°) — while the combo rows are keyed by whole degrees, so an exact-key lookup misses
    /// on every real value and a combo seeded that way opens with NO selection, where the first
    /// Down-arrow selects row 0 and writes 10° to the take-off wheel. Seed by the nearest degree.
    /// </summary>
    [Theory]
    [InlineData(33.0, 15)]   // the shipped ReadyToFly wheel, 14.95°
    [InlineData(0, 10)]
    [InlineData(100, 25)]
    [InlineData(3.0, 10)]    // 10.45° rounds down
    [InlineData(3.4, 11)]    // 10.51° rounds up
    [InlineData(36.7, 16)]   // 15.505° rounds up
    public void NearestSelectableDegrees_SnapsTheContinuousWheelToAListedDegree(double raw, int expected)
    {
        Assert.Equal(expected, System().NearestSelectableDegrees(raw));
    }

    /// <summary>A value off the end of the wheel's travel seeds the end row, never a row that does not exist.</summary>
    [Theory]
    [InlineData(-5, 10)]
    [InlineData(105, 25)]
    public void NearestSelectableDegrees_ClampsToTheWheelTravel(double raw, int expected)
    {
        Assert.Equal(expected, System().NearestSelectableDegrees(raw));
    }

    /// <summary>
    /// The seed must agree with the two places the same angle is rendered as TEXT — the panel
    /// display row (TryGetDisplayOverride) and the flap read-out (DescribePosition) — which both
    /// round with ToString("0"). A seed that rounded differently would select "14 degrees" under a
    /// display row reading "15 degrees". Swept across the whole travel: the sweep pins that the two
    /// agree everywhere the wheel can sit, not one particular rounding rule at one particular value.
    /// </summary>
    [Fact]
    public void NearestSelectableDegrees_AgreesWithTheSpokenDegreeText()
    {
        var sys = System();

        for (var i = 0; i <= 2000; i++)
        {
            var raw = i * 0.05;
            var spoken = sys.DegreesFor(raw).ToString("0", CultureInfo.InvariantCulture);
            Assert.Equal(spoken, sys.NearestSelectableDegrees(raw).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// The seed is a KEY of DialValueDescriptions, so a combo built from that dictionary selects it
    /// by exact value; and its label is byte-identical to the definition's display row for the same
    /// raw value, so the combo can never show one angle while the status display shows another.
    /// Pinned together so a relabel of either side breaks visibly.
    /// </summary>
    [Fact]
    public void NearestDialChoice_IsAListedKeyWhoseLabelMatchesTheDisplayRow()
    {
        var sys = System();
        var def = new TFDiMD11Definition();
        var choices = sys.DialValueDescriptions();

        for (var i = 0; i <= 400; i++)
        {
            var raw = i * 0.25;
            var key = sys.NearestDialChoice(raw);

            Assert.True(choices.ContainsKey(key), $"raw {raw}: {key} is not a combo key");
            Assert.Equal($"{sys.NearestSelectableDegrees(raw)} degrees", choices[key]);
            Assert.True(def.TryGetDisplayOverride(Md11FlapSystem.DialKey, raw, out var row));
            Assert.Equal(choices[key], row);
        }
    }

    /// <summary>
    /// Both flap combos are keyed by discrete positions over a var that is not discrete: the
    /// thumbwheel's raw value is continuous, and the handle's Dial-A-Flap detent is a BAND
    /// (FLAP_RNG 38–65 — TFDi's ReadyToFly state parks the handle at 46.91). MainForm's combo
    /// lookup is an exact key match, so both opened with NO selection, and in a DropDownList the
    /// first Down-arrow then selects row 0 and COMMITS it — 10° on the wheel, and on the handle
    /// "Flap Up / Slat Retracted", a walk that RETRACTS the flaps. Which key a delivered value
    /// describes is the DEFINITION's to answer (SimVarDefinition.ValueToDescriptionKey), so both
    /// carry a classifier and every value the aircraft can report lands on a listed key.
    /// </summary>
    [Theory]
    [InlineData(Md11FlapSystem.DialKey, 0, "10 degrees")]
    [InlineData(Md11FlapSystem.DialKey, 33.0, "15 degrees")]              // the shipped ReadyToFly wheel
    [InlineData(Md11FlapSystem.DialKey, 47.3, "17 degrees")]              // between two listed degrees
    [InlineData(Md11FlapSystem.DialKey, 100, "25 degrees")]
    [InlineData(Md11FlapSystem.LeverKey, 0, "Flap Up / Slat Retracted")]
    [InlineData(Md11FlapSystem.LeverKey, 46.91, "Dial-A-Flap")]           // the parked handle, inside the band
    [InlineData(Md11FlapSystem.LeverKey, 60, "Dial-A-Flap")]              // elsewhere in the same band
    [InlineData(Md11FlapSystem.LeverKey, 100, "Flap 50")]
    public void BothFlapCombos_ClassifyTheirValueOntoAListedKey(string varKey, double value, string expectLabel)
    {
        var def = new TFDiMD11Definition().GetVariables()[varKey];

        Assert.NotNull(def.ValueToDescriptionKey);
        var key = def.DescriptionKeyFor(value);
        Assert.True(def.ValueDescriptions.ContainsKey(key),
            $"{varKey} {value} classified onto {key}, which the combo does not carry");
        Assert.Equal(expectLabel, def.ValueDescriptions[key]);
    }
}
