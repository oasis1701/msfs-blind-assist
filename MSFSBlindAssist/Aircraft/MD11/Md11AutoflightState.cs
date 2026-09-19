using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Every spoken autoflight string on the MD-11, composed in ONE place so the FCP window, the
/// Shift+H/S/A/V read-outs, the Ctrl+H/S/A/V dialogs and the Autoflight Status panel can never
/// describe the same window with different words.
///
/// WHAT IS KNOWABLE, AND FROM WHERE. The FCP button variables (MD11_CGS_NAV_BT and friends)
/// carry no state — measured 2026-09-06 in the cruise with NAV, PROF, FMS SPD, AP1 and the
/// autothrottle all engaged, every one of them read 0. The aircraft's FMA is drawn inside the
/// WASM and never exported. What IS exported is what the FCP itself shows a sighted pilot, and
/// TFDi's Systems Guide (Forward Panel page) documents the windows' engagement cues:
///   • IAS/MACH window "shows dashes when the AFS is controlling to the FMS flight plan speed"
///   • HDG/TRK window "is blank when the AFS is controlling to the FMS flight plan"
///   • V/S-FPA window "Display is blank if V/S or FPA are not engaged"
/// So a dashed speed window IS "FMS speed engaged", a dashed heading window IS "NAV engaged", and
/// a dashed V/S window is only "V/S or FPA not engaged" — PROF and ALT HOLD look identical there,
/// which is why nothing here ever claims PROF, and nothing claims APPR/LAND (FMA-only).
/// Autopilot and autothrottle engagement come from MD11_AP_STATE (documented 0/1/2/3) and
/// MD11_ATS_STATE (1 measured with the ATS engaged; 0 = off; 2 never observed, treated as on).
/// </summary>
public static class Md11AutoflightState
{
    /// <summary>A noun with its own lower-case form, so "FPA" is never string-lowered to "fPA".</summary>
    public sealed record Noun(string Capitalised, string Lower);

    public static readonly Noun Speed = new("Speed", "speed");
    public static readonly Noun Heading = new("Heading", "heading");
    public static readonly Noun Track = new("Track", "track");
    public static readonly Noun Altitude = new("Altitude", "altitude");
    public static readonly Noun VerticalSpeed = new("Vertical speed", "vertical speed");
    public static readonly Noun Fpa = new("FPA", "FPA");

    public static Noun HeadingNoun(bool track) => track ? Track : Heading;
    public static Noun VerticalNoun(bool fpa) => fpa ? Fpa : VerticalSpeed;

    /// <summary>
    /// What every composer here says for a value that has not been read — a cache emptied by a
    /// SimConnect drop, or a var not yet delivered. Never a number or a default mode: "Speed: 0
    /// knots" and "Autopilot: off" read as facts about the aircraft.
    /// </summary>
    public const string NotAvailable = "not available";

    // ---- engagement ----

    /// <summary>MD11_AP_STATE, documented 0=Off, 1=AP1, 2=AP2, 3=AP1+2.</summary>
    public static string Autopilot(double? apState) => apState switch
    {
        null => NotAvailable,
        >= 2.5 => "AP 1 and 2",
        >= 1.5 => "AP 2",
        >= 0.5 => "AP 1",
        _ => "off",
    };

    /// <summary>MD11_ATS_STATE: 0 off; 1 measured live with the autothrottle engaged; 2 unobserved, treated as on.</summary>
    public static bool AutothrottleOn(double atsState) => atsState >= 0.5;

    public static string Autothrottle(double? atsState)
        => atsState is double ats ? (AutothrottleOn(ats) ? "on" : "off") : NotAvailable;

    /// <summary>
    /// The AUTO FLIGHT button's state: TFDi say it engages "both ATs and one AP", so both halves are
    /// named — and neither is guessed while unread.
    /// </summary>
    public static string Autoflight(double? apState, double? atsState)
    {
        if (apState is not double apValue || atsState is not double atsValue) return NotAvailable;
        var ap = Autopilot(apValue);
        bool ats = AutothrottleOn(atsValue);
        if (ap == "off" && !ats) return "off";
        return $"{(ap == "off" ? "AP off" : ap)}, ATS {(ats ? "on" : "off")}";
    }

    /// <summary>The heading window shows dashes while NAV flies the aircraft (TFDi, Forward Panel); null while the window is unread.</summary>
    public static bool? NavEngaged(double? afsHeading)
        => afsHeading is double hdg ? Md11Fcp.IsDashedSpeedHeading(hdg) : null;

    /// <summary>The speed window shows dashes while FMS speed is engaged (TFDi, Forward Panel); null while the window is unread.</summary>
    public static bool? FmsSpeedEngaged(double? afsSpeed)
        => afsSpeed is double spd ? Md11Fcp.IsDashedSpeedHeading(spd) : null;

    public static string Engaged(bool? engaged) => engaged switch
    {
        true => "engaged",
        false => "not engaged",
        null => NotAvailable,
    };

    // ---- window modes: the word each 0/1 mode var selects (the aircraft's own vars) ----

    public static string SpeedMode(double? isMach) => ModeWord(isMach, "IAS", "Mach");
    public static string HeadingMode(double? isTrack) => ModeWord(isTrack, "Heading", "Track");
    public static string VerticalMode(double? isFpa) => ModeWord(isFpa, "V/S", "FPA");
    public static string AltitudeUnit(double? isMetres) => ModeWord(isMetres, "feet", "metres");

    private static string ModeWord(double? mode, string zero, string one)
        => mode is double m ? (m > 0.5 ? one : zero) : NotAvailable;

    // ---- window values (no noun; the renderers add one) ----

    public static string SpeedValue(double? afsSpeed, bool mach)
    {
        if (afsSpeed is not double spd) return NotAvailable;
        if (Md11Fcp.IsDashedSpeedHeading(spd)) return "dashed, FMS speed engaged";
        // Mach is float32 (0.81999999 for 0.82): format, never compare — to three decimals, the
        // FCP's own "%0.3f", so a selected 0.825 is never spoken as 0.83.
        return mach
            ? $"Mach {spd.ToString("0.000", CultureInfo.InvariantCulture)}"
            : $"{spd.ToString("0", CultureInfo.InvariantCulture)} knots";
    }

    public static string HeadingValue(double? afsHeading)
    {
        if (afsHeading is not double hdg) return NotAvailable;
        return Md11Fcp.IsDashedSpeedHeading(hdg)
            ? "dashed, NAV engaged"
            : hdg.ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// TFDi document no dash sentinel for the altitude window, and neither -999 nor -9999 is a legal
    /// FCP altitude, so both still read "dashed" here — the one window that names both.
    /// </summary>
    public static string AltitudeValue(double? afsAltitude, bool metres)
    {
        if (afsAltitude is not double alt) return NotAvailable;
        return Md11Fcp.IsDashedSpeedHeading(alt) || Md11Fcp.IsDashedVerticalSpeed(alt)
            ? "dashed"
            : $"{alt.ToString("0", CultureInfo.InvariantCulture)} {(metres ? "metres" : "feet")}";
    }

    /// <summary>
    /// A dashed window means V/S or FPA is not engaged — PROF or ALT HOLD, which cannot be told
    /// apart. Only -9999 dashes it: a selected -999 fpm is a value.
    /// </summary>
    public static string VerticalValue(double? afsVerticalSpeed, bool fpa)
    {
        if (afsVerticalSpeed is not double vs) return NotAvailable;
        if (Md11Fcp.IsDashedVerticalSpeed(vs)) return "dashed, not engaged";
        return fpa
            ? $"{vs.ToString("0.0", CultureInfo.InvariantCulture)} degrees"
            : $"{vs.ToString("0", CultureInfo.InvariantCulture)} feet per minute";
    }

    // ---- renderers ----

    /// <summary>"Heading: dashed, NAV engaged" — the FCP window's status rows.</summary>
    public static string Row(Noun noun, string value) => $"{noun.Capitalised}: {value}";

    /// <summary>"Selected heading dashed, NAV engaged" — the Shift+H/S/A/V read-outs.</summary>
    public static string Selected(Noun noun, string value) => $"Selected {noun.Lower} {value}";

    /// <summary>
    /// The Autoflight Status panel's rows have FIXED display names ("Selected heading"), so the
    /// value carries the lower-cased noun only when the window is in its non-default mode:
    /// "track 123", "FPA -3.0 degrees"; a heading-mode 123 stays "123".
    /// </summary>
    public static string PanelValue(Noun defaultNoun, Noun noun, string value)
        => noun == defaultNoun ? value : $"{noun.Lower} {value}";
}
