using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The ADI's rows and spoken lines (plan 27; docs/b757/evidence/displays-adi.md, "Step 4"). The rows are in screen order,
/// each starting with where its item sits: the speed tape on the left, the horizon's area in the center, the glideslope,
/// the decision height and the radio altitude on the right, the localizer at the bottom. A row is there only while its
/// item is drawn; a flag stands in its instrument's place, never beside a value. The flags' and legends' words are the
/// screen's own (the reading carries the layout's). Numbers are always culture-invariant.
/// <para>Only the five flags are ever spoken (ATT, SPD, LOC, G/S, RA), as each appears and goes. Nothing else changes
/// seldom enough to be more than chatter, and nothing here repeats a callout BlueBird plays itself: its 80 knots, V1
/// and rotate, its radio altitude and minimums callouts, its TCAS, windshear and terrain aurals.</para>
/// </summary>
public static class Bbs757AdiText
{
    private const string Left = "Left: ", Center = "Center: ", Right = "Right: ", Bottom = "Bottom: ";

    /// <summary>A pitch within this of level, a flight director pitch bar within this of the symbol, and a pointer
    /// within this many dots of its scale's middle read "level" and "centered": each would print 0.0.</summary>
    private const double Tenth = 0.05;

    /// <summary>A bank within this of wings level and a roll bar within this of the middle would print 0.</summary>
    private const double Half = 0.5;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The window's rows for a reading; none for a dark or corrupt one (the monitor lists why).</summary>
    public static IReadOnlyList<string> Rows(Bbs757AdiReading reading)
    {
        var rows = new List<string>();
        if (!reading.Powered || reading.Corrupt) return rows;

        // ---- left: the speed tape ----
        NotRead(rows, "Speed tape", reading.Speed, reading.SpeedMissing);
        if (reading.Speed is { } s)
        {
            if (s.SpdFlag)
            {
                rows.Add($"{Left}{s.SpdWord} flag");
            }
            else
            {
                if (s.Airspeed is double airspeed) rows.Add(Left + "airspeed " + Rounded(airspeed));
                if (s.TargetSpeed is double target) rows.Add(Left + "target speed " + Rounded(target));
                if (s.V1Inop) rows.Add($"{Left}{s.V1Word} {s.InopWord}");
                if (s.V1 is int v1) rows.Add($"{Left}{s.V1Word} {Whole(v1)}");
                if (s.Vr is int vr) rows.Add(Left + "VR " + Whole(vr));
                if (s.FlapSpeed is double flap) rows.Add(Left + "flap maneuvering speed " + Rounded(flap));
                if (s.Mach is double mach) rows.Add(Left + "Mach " + Mach(mach));
                if (s.GroundSpeed is double groundSpeed) rows.Add(Left + "ground speed " + Whole(groundSpeed));
            }
        }

        // ---- center: the horizon, the flight director, TCAS, the rising runway ----
        NotRead(rows, "Attitude", reading.Attitude, reading.AttitudeMissing);
        if (reading.Attitude is { } a)
        {
            switch (a.State)
            {
                case Bbs757AdiAttitudeState.Flag:
                    rows.Add($"{Center}{a.AttWord} flag");
                    break;
                case Bbs757AdiAttitudeState.Blank:
                    rows.Add(Center + "no attitude");
                    break;
                default:
                    var parts = new List<string>(2);
                    if (a.PitchUp is double pitch) parts.Add(IsZero(pitch, Tenth) ? "level" : "pitch " + PitchAngle(pitch));
                    if (a.BankLeft is double bank)
                        parts.Add(IsZero(bank, Half) ? "wings level" : $"bank {Rounded(Math.Abs(bank))} {(bank > 0 ? "left" : "right")}");
                    if (parts.Count > 0) rows.Add(Center + string.Join(", ", parts));
                    break;
            }
        }

        NotRead(rows, "Flight director", reading.FlightDirector, reading.FlightDirectorMissing);
        if (reading.FlightDirector is { Drawn: true } fd)
        {
            string pitchBar = IsZero(fd.PitchBar, Tenth) ? "centered" : $"{Tenths(Math.Abs(fd.PitchBar))} {(fd.PitchBar > 0 ? "above" : "below")}";
            string rollBar = IsZero(fd.RollBar, Half) ? "centered" : $"{Rounded(Math.Abs(fd.RollBar))} {(fd.RollBar > 0 ? "right" : "left")}";
            rows.Add($"{Center}flight director, pitch bar {pitchBar}, roll bar {rollBar}");
        }

        NotRead(rows, "TCAS", reading.Tcas, reading.TcasMissing);
        if (reading.Tcas is { } tcas)
        {
            if (tcas.KeepAbove is double above) rows.Add(Center + "TCAS, keep the pitch above " + PitchAngle(above));
            if (tcas.KeepBelow is double below) rows.Add(Center + "TCAS, keep the pitch below " + PitchAngle(below));
        }

        NotRead(rows, "Localizer and glideslope", reading.Ils, reading.IlsMissing);
        var ils = reading.Ils;
        // The rising runway takes the localizer pointer's place (25857 draws the pointer only without it) and moves
        // sideways as the pointer would: its row carries the deviation.
        if (ils is { RisingRunway: true })
            rows.Add(Center + "rising runway, " + Deviation(ils.LocalizerDots, ils.LocalizerPinned, "right", "left"));

        // ---- right: the glideslope, ALT, the decision height, the radio altitude ----
        if (ils is not null)
        {
            switch (ils.Glideslope)
            {
                case Bbs757AdiPointer.Flag:
                    rows.Add($"{Right}{ils.GlideslopeWord} flag");
                    break;
                case Bbs757AdiPointer.Pointer:
                    rows.Add(Right + "glideslope, pointer " + Deviation(ils.GlideslopeDots, ils.GlideslopePinned, "up", "down"));
                    break;
                case Bbs757AdiPointer.ScaleOnly:
                    rows.Add(Right + "glideslope scale, no pointer");
                    break;
            }
        }

        NotRead(rows, "Radio altitude and decision height", reading.RadioAltitude, reading.RadioAltitudeMissing);
        if (reading.RadioAltitude is { } ra)
        {
            if (ra.Alt) rows.Add(Right + ra.AltWord);
            if (ra.DecisionHeight is int dh) rows.Add($"{Right}{ra.DhWord} {Whole(dh)}");
            if (ra.RaFlag) rows.Add($"{Right}{ra.RaWord} flag");
            else if (ra.RadioAltitude is int feet) rows.Add(Right + "radio altitude " + Whole(feet));
        }

        // ---- bottom: the localizer ----
        if (ils is not null)
        {
            switch (ils.Localizer)
            {
                case Bbs757AdiPointer.Flag:
                    rows.Add($"{Bottom}{ils.LocWord} flag");
                    break;
                case Bbs757AdiPointer.Pointer when ils.RisingRunway:
                    rows.Add(Bottom + "localizer scale" + (ils.Expanded ? ", expanded" : ""));
                    break;
                case Bbs757AdiPointer.Pointer:
                    rows.Add(Bottom + "localizer, pointer " + Deviation(ils.LocalizerDots, ils.LocalizerPinned, "right", "left")
                        + (ils.Expanded ? ", expanded scale" : ""));
                    break;
                case Bbs757AdiPointer.ScaleOnly:
                    rows.Add(Bottom + "localizer scale, no pointer");
                    break;
            }
        }
        return rows;
    }

    /// <summary>The flags drawn, by their words, in screen order (SPD, ATT, G/S, RA, LOC); none while the display is dark
    /// or the reading corrupt.</summary>
    public static IReadOnlyList<string> Flags(Bbs757AdiReading reading)
    {
        var flags = new List<string>(5);
        if (!reading.Powered || reading.Corrupt) return flags;
        if (reading.Speed is { SpdFlag: true } s) flags.Add(s.SpdWord);
        if (reading.Attitude is { State: Bbs757AdiAttitudeState.Flag } a) flags.Add(a.AttWord);
        if (reading.Ils is { Glideslope: Bbs757AdiPointer.Flag } gs) flags.Add(gs.GlideslopeWord);
        if (reading.RadioAltitude is { RaFlag: true } ra) flags.Add(ra.RaWord);
        if (reading.Ils is { Localizer: Bbs757AdiPointer.Flag } loc) flags.Add(loc.LocWord);
        return flags;
    }

    /// <summary>What every flag line the monitor speaks on its own starts with; the parts that follow "ADI announcements
    /// resumed" go without it (the display is already named).</summary>
    public const string ChangePrefix = "ADI ";

    /// <summary>
    /// What changed from <paramref name="heard"/> to <paramref name="now"/>: "ADI flag: ATT" for the flags that appeared,
    /// then "ADI flag gone: ATT" for those that went, the flags of one line joined in screen order. Flags are compared
    /// between two lit readings only: a display going dark takes no flag away, and what it shows as it lights is the
    /// monitor's silent baseline. Nothing else is ever spoken: no speed, attitude, deviation, radio altitude, decision
    /// height, V1 INOP, ALT, TCAS advisory or rising runway (the window lists them).
    /// </summary>
    public static IReadOnlyList<string> Changes(Bbs757AdiReading heard, Bbs757AdiReading now, bool withPrefix)
    {
        var lines = new List<string>(2);
        if (!heard.Powered || !now.Powered || heard.Corrupt || now.Corrupt) return lines;
        var before = Flags(heard);
        var after = Flags(now);
        var added = after.Where(f => !before.Contains(f, StringComparer.Ordinal)).ToList();
        var gone = before.Where(f => !after.Contains(f, StringComparer.Ordinal)).ToList();
        string prefix = withPrefix ? ChangePrefix : "";
        if (added.Count > 0) lines.Add(prefix + "flag: " + string.Join(", ", added));
        if (gone.Count > 0) lines.Add(prefix + "flag gone: " + string.Join(", ", gone));
        return lines;
    }

    // ---- shared ----------------------------------------------------------------------------------------------

    /// <summary>A part's one row, where its rows would start, when the layout has no such part: "TCAS: not read: not in
    /// this layout file".</summary>
    private static void NotRead(List<string> rows, string part, object? read, string? reason)
    {
        if (read is null && reason is not null) rows.Add(part + ": not read: " + reason);
    }

    /// <summary>A pointer's place on its scale: "at full scale, up" where BlueBird pins it, never a number beyond;
    /// "centered"; else its dots with one decimal and the side.</summary>
    private static string Deviation(double dots, bool pinned, string positive, string negative)
    {
        string side = dots > 0 ? positive : negative;
        if (pinned) return "at full scale, " + side;
        return IsZero(dots, Tenth) ? "centered" : $"{Tenths(Math.Abs(dots))} dots {side}";
    }

    /// <summary>A pitch angle in degrees nose up: "2.5 up", "1.0 down", "level".</summary>
    private static string PitchAngle(double up) =>
        IsZero(up, Tenth) ? "level" : $"{Tenths(Math.Abs(up))} {(up > 0 ? "up" : "down")}";

    private static bool IsZero(double v, double within) => Math.Abs(v) < within;

    /// <summary>One decimal, a 5 away from zero.</summary>
    private static string Tenths(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("F1", Inv);

    /// <summary>The nearest whole number, a half away from zero: a tape's or a bug's value.</summary>
    private static string Rounded(double v) => ((long)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(Inv);

    /// <summary>BlueBird's whole number of a printed value: truncated toward zero.</summary>
    private static string Whole(double v) => ((long)Math.Truncate(v)).ToString(Inv);

    /// <summary>The tape's Mach: three decimals without the leading zero (".780").</summary>
    private static string Mach(double mach)
    {
        string text = mach.ToString("F3", Inv);
        return text.StartsWith("0.", StringComparison.Ordinal) ? text[1..] : text;
    }
}
