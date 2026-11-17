using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One visual guidance update as the flight recording writes it (VP-2). Pitch nose-up positive, bank
/// right-positive (<c>StandardBank</c>, VG-7), cross-track + right of the centreline facing the
/// runway; <paramref name="T"/> is seconds since <c>Initialize</c> on the manager's clock. Stage 5 of the
/// redesign added the vertical cue's mode (Follow, Hold, Plan, Path, Flare), its target descent angle
/// (degrees, + down) and the learned trim (pitch minus flight path).
/// </summary>
internal readonly record struct VisualGuidanceFrameRecord(double T, string Phase, double Lat, double Lon, double AltMslFt,
    double AglFt, double GsKt, double VsFpm, double TrackMagDeg, double HeadingMagDeg, double PitchDeg, double BankDeg,
    double AoaDeg, double DesiredPitchDeg, double DesiredBankDeg, double TargetFpm, double AltitudeErrorFt,
    string VerticalMode, double TargetDescentDeg, double TrimDeg, double CrossTrackNm, double DistanceNm);

/// <summary>
/// Formats the flight recording's lines: every visual guidance update, everything it spoke, and
/// where an approach starts and stops, so a flight in the sim can be measured afterwards with
/// tools/VirtualPilot/analyze_vg_flight.py (VP-2). In the app the lines go to
/// <c>%APPDATA%\MSFSBlindAssist\logs\visual_guidance_flight.log</c> (CORE-14).
///
/// Each line starts <c>VGREC</c> and holds space-separated <c>key=value</c> fields in the invariant
/// culture; a spoken line's text comes last and runs to the end of the line. Precision is per field,
/// so the analysis reproduces the harness's own measures: latitude and longitude 7 decimals, time 3,
/// angles (pitch, bank, angle of attack, the two commands, the target descent angle and the trim) 4,
/// cross-track and distance in nautical miles 5, everything else 2.
/// </summary>
internal static class VisualGuidanceFlightRecord
{
    /// <summary>The log channel, and so the file <c>visual_guidance_flight.log</c>.</summary>
    public const string LogChannelName = "visual_guidance_flight";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Start(string airportIcao, string runwayId, double thresholdLat, double thresholdLon, double courseTrueDeg,
        double courseMagDeg, double thresholdElevationFt, double glidePathDeg, double glideslopeBiasFt, double flareBiasFt)
        => new StringBuilder("VGREC start")
            .Append(" apt=").Append(Word(airportIcao)).Append(" rwy=").Append(Word(runwayId))
            .Append(" lat=").Append(N(thresholdLat, "0.0000000")).Append(" lon=").Append(N(thresholdLon, "0.0000000"))
            .Append(" crs=").Append(N(courseTrueDeg, "0.00")).Append(" crsMag=").Append(N(courseMagDeg, "0.00"))
            .Append(" elev=").Append(N(thresholdElevationFt, "0.00")).Append(" gp=").Append(N(glidePathDeg, "0.00"))
            .Append(" gsBias=").Append(N(glideslopeBiasFt, "0.00")).Append(" flareBias=").Append(N(flareBiasFt, "0.00"))
            .ToString();

    public static string Frame(in VisualGuidanceFrameRecord r)
        => new StringBuilder("VGREC f")
            .Append(" t=").Append(N(r.T, "0.000")).Append(" ph=").Append(Word(r.Phase))
            .Append(" lat=").Append(N(r.Lat, "0.0000000")).Append(" lon=").Append(N(r.Lon, "0.0000000"))
            .Append(" alt=").Append(N(r.AltMslFt, "0.00")).Append(" agl=").Append(N(r.AglFt, "0.00"))
            .Append(" gs=").Append(N(r.GsKt, "0.00")).Append(" vs=").Append(N(r.VsFpm, "0.00"))
            .Append(" trk=").Append(N(r.TrackMagDeg, "0.00")).Append(" hdg=").Append(N(r.HeadingMagDeg, "0.00"))
            .Append(" pitch=").Append(N(r.PitchDeg, "0.0000")).Append(" bank=").Append(N(r.BankDeg, "0.0000"))
            .Append(" aoa=").Append(N(r.AoaDeg, "0.0000"))
            .Append(" dPitch=").Append(N(r.DesiredPitchDeg, "0.0000")).Append(" dBank=").Append(N(r.DesiredBankDeg, "0.0000"))
            .Append(" tFpm=").Append(N(r.TargetFpm, "0.00")).Append(" altErr=").Append(N(r.AltitudeErrorFt, "0.00"))
            .Append(" vmode=").Append(Word(r.VerticalMode))
            .Append(" tgtDesc=").Append(N(r.TargetDescentDeg, "0.0000")).Append(" trim=").Append(N(r.TrimDeg, "0.0000"))
            .Append(" xte=").Append(N(r.CrossTrackNm, "0.00000")).Append(" dist=").Append(N(r.DistanceNm, "0.00000"))
            .ToString();

    /// <summary>A spoken line; <paramref name="immediate"/> when it interrupted (AnnounceImmediate).</summary>
    public static string Said(double t, string text, bool immediate)
        => $"VGREC say t={N(t, "0.000")} imm={(immediate ? 1 : 0)} text={text.Replace('\r', ' ').Replace('\n', ' ')}";

    public static string Stop(double t) => $"VGREC stop t={N(t, "0.000")}";

    // NaN and infinities in the invariant culture's words ("NaN", "Infinity"), which Python's float() reads.
    private static string N(double v, string format) => v.ToString(format, Inv);

    // A value that must stay one field: no spaces.
    private static string Word(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace(' ', '_');
}
