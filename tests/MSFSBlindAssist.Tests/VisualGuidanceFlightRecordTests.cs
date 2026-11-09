using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The flight recording (VP-2): one line per visual guidance update, read back by
/// tools/VirtualPilot/analyze_vg_flight.py, so its fields, their order and their numbers must not
/// drift, on any PC culture.
/// </summary>
public class VisualGuidanceFlightRecordTests
{
    private static readonly VisualGuidanceFrameRecord Sample = new(
        T: 12.3456, Phase: "GlideslopeTracking", Lat: 47.0123456789, Lon: 8.1234567891, AltMslFt: 2345.678, AglFt: 1845.671,
        GsKt: 140.126, VsFpm: -742.456, TrackMagDeg: 89.876, HeadingMagDeg: 91.234, PitchDeg: 2.12346, BankDeg: -1.23456,
        AoaDeg: 5.01234, DesiredPitchDeg: 1.98766, DesiredBankDeg: -0.54321, TargetFpm: -745.678, AltitudeErrorFt: 12.346,
        VerticalMode: "Path", TargetDescentDeg: 3.0, TrimDeg: 5.0,
        CrossTrackNm: -0.0123456, DistanceNm: 6.1234567);

    private const string SampleLine =
        "VGREC f t=12.346 ph=GlideslopeTracking lat=47.0123457 lon=8.1234568 alt=2345.68 agl=1845.67 gs=140.13 vs=-742.46 "
        + "trk=89.88 hdg=91.23 pitch=2.1235 bank=-1.2346 aoa=5.0123 dPitch=1.9877 dBank=-0.5432 tFpm=-745.68 altErr=12.35 "
        + "vmode=Path tgtDesc=3.0000 trim=5.0000 xte=-0.01235 dist=6.12346";

    [Fact]
    public void A_frame_line_has_every_field_in_order()
    {
        Assert.Equal(SampleLine, VisualGuidanceFlightRecord.Frame(Sample));
    }

    // Stage 5: the vertical mode, the target descent angle and the learned trim follow the height error.
    [Fact]
    public void The_record_carries_the_vertical_mode()
    {
        Assert.Contains(" altErr=12.35 vmode=Path tgtDesc=3.0000 trim=5.0000 xte=", VisualGuidanceFlightRecord.Frame(Sample));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Record_lines_are_culture_invariant(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            string line = VisualGuidanceFlightRecord.Frame(Sample);
            Assert.Equal(SampleLine, line);
            Assert.DoesNotContain(',', line);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Activation_updates_speech_and_stop_are_all_recorded()
    {
        var lines = new List<string>();
        var rig = new VisualGuidanceTestRig(record: lines.Add);
        rig.Place(8, 1.0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(6, 30);   // past the 5.4 s the activation and "Vectoring to final" hold the bank callouts (VG-21)
        rig.Stop();

        Assert.StartsWith("VGREC start apt=ZZVG rwy=09", lines[0]);
        Assert.Equal(181, lines.Count(l => l.StartsWith("VGREC f ", StringComparison.Ordinal)));
        var bankCallout = new Regex(@"^VGREC say t=\S+ imm=1 text=\d+ (left|right)$", RegexOptions.CultureInvariant);
        Assert.Contains(lines, l => bankCallout.IsMatch(l));
        Assert.StartsWith("VGREC stop", lines[^1]);
    }
}
