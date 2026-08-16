namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's Flight Control Panel windows: what each one is called, how it is written, and how
/// its value is encoded.
///
/// EVERYTHING HERE WAS PROVEN AGAINST THE LIVE AIRCRAFT (2026-07-17). TFDi document the variable
/// NAMES and the four unit enums, and nothing else — no units, no ranges, no write method, no
/// commit semantics. Those were established by probing a running MD-11, and the results are
/// recorded here because they are not recoverable from any document:
///
///   • Writing <c>MD11_EXTCTL_FCP_HDG = 123</c> → <c>MD11_AFS_HDG</c> became 123 and
///     <c>MD11_EXTCTL_FCP_HDG</c> reset itself to -1. So the EXTCTL var is a one-shot COMMAND
///     INBOX consumed by the FCC, and -1 is its idle sentinel — NOT a mirror of the window.
///   • Writing <c>SPD_U = 1</c> then <c>SPD = 0.82</c> → <c>MD11_AP_IAS_MACH</c> became 1 and
///     <c>MD11_AFS_SPD</c> read 0.81999999. So Mach is a REAL number (0.82), not 82, and it is
///     stored as float32 — read-outs must round it.
///
/// THE NAMES DO NOT LINE UP. The read side is <c>MD11_AFS_{SPD,HDG,ALT,VS}</c>; the write side is
/// <c>MD11_EXTCTL_FCP_{SPD,HDG,ALT,VR}</c>. Vertical speed is <c>VS</c> when read and <c>VR</c>
/// when written. There is no documented mapping between the two families at all — the pairing is
/// this file's claim, established by the probe above, which is exactly why it is written down.
/// </summary>
public static class Md11Fcp
{
    /// <summary>The idle value of every EXTCTL command var: "no command pending".</summary>
    public const double Idle = -1;

    /// <summary>
    /// TFDi's "this window is showing dashes" read-back sentinels: -999 on speed/heading,
    /// -9999 on vertical speed. Documented as READ-side only — whether writing one dashes the
    /// window is NOT documented and was not probed, so nothing here writes them.
    /// </summary>
    public static bool IsDashed(double readback) => readback <= -999;

    // ---- read side (the FCP windows) ----
    public const string ReadSpeed = "MD11_AFS_SPD";
    public const string ReadHeading = "MD11_AFS_HDG";
    public const string ReadAltitude = "MD11_AFS_ALT";
    public const string ReadVerticalSpeed = "MD11_AFS_VS";

    // ---- read side (the window MODES) ----
    // Each selected value is meaningless without its mode: "250" is knots or Mach depending on
    // IAS_MACH. NOTE the polarity is the L:VAR's, which is INVERTED from the enum inside the
    // binary (DWARF has HDGTrack{Track=0,HDG=1}); Glareshield.xml's own tooltip settles it as
    // 0=Heading/1=Track, matching TFDi's docs. Never take the internal enum for the L:var's.
    public const string ModeSpeedIsMach = "MD11_AP_IAS_MACH";     // 0 = IAS,  1 = Mach
    public const string ModeHeadingIsTrack = "MD11_AP_HDG_TRK";   // 0 = Heading, 1 = Track
    public const string ModeVerticalIsFpa = "MD11_AP_VS_FPA";     // 0 = V/S,  1 = FPA
    public const string ModeAltitudeIsMetres = "MD11_AP_FT_M";    // 0 = Feet, 1 = Metres

    // ---- write side (the command inboxes) ----
    public const string WriteSpeed = "MD11_EXTCTL_FCP_SPD";
    public const string WriteSpeedUnit = "MD11_EXTCTL_FCP_SPD_U";     // 0 = knots, 1 = mach
    public const string WriteHeading = "MD11_EXTCTL_FCP_HDG";
    public const string WriteHeadingUnit = "MD11_EXTCTL_FCP_HDG_U";   // 0 = heading, 1 = track
    public const string WriteAltitude = "MD11_EXTCTL_FCP_ALT";
    public const string WriteAltitudeUnit = "MD11_EXTCTL_FCP_ALT_U";  // 0 = feet, 1 = metres
    public const string WriteVerticalSpeed = "MD11_EXTCTL_FCP_VR";    // "VR", not "VS"
    public const string WriteVerticalSpeedUnit = "MD11_EXTCTL_FCP_VR_U"; // 0 = V/S, 1 = FPA

    // ---------------------------------------------------------------------------------
    // Ranges
    //
    // Not documented. These come from the FCP renderer's own format strings inside md11host.wasm
    // (DATA 22358979-22359495): speed IAS "%1.0f" / MACH "%0.3f", heading "%03d", altitude "%d",
    // vertical rate V/S "%04d" / FPA "%1.2f". The BOUNDS below are conventional MD-11 limits
    // rather than anything the aircraft states, and exist only to stop a typo (a fat-fingered
    // 25000 kt) reaching the FCC — the aircraft remains the authority on what it accepts.
    // ---------------------------------------------------------------------------------

    public const int MinSpeedKnots = 100;
    public const int MaxSpeedKnots = 365;
    public const double MinMach = 0.10;
    public const double MaxMach = 0.95;
    public const int MinAltitudeFt = 0;
    public const int MaxAltitudeFt = 41000;
    public const int MaxVerticalSpeedFpm = 6000;
    public const double MaxFpaDegrees = 9.9;

    // ---------------------------------------------------------------------------------
    // The knobs
    //
    // Speed, heading and altitude are PUSH-PULL (the control map's knob_pp kind): push and pull are
    // distinct physical actions with their own event pairs, on top of the wheel that turns them.
    // The V/S knob is NOT — it has no PUSH_/PULL_ events, because the real one does not push or
    // pull, so nothing must offer those for it. Pinned by Md11FlightControlPanelTests.
    // ---------------------------------------------------------------------------------

    public const string SpeedKnob = "MD11_CGS_SPD_KB";
    public const string HeadingKnob = "MD11_CGS_HDG_KB";
    public const string AltitudeKnob = "MD11_CGS_ALT_KB";
    public const string VerticalSpeedKnob = "MD11_CGS_VS_KB";   // wheel only — no push/pull

    // ---------------------------------------------------------------------------------
    // Altimeter (baro)
    //
    // The captain's altimeter setting is an EXTCTL command inbox exactly like the FCP windows —
    // PROVEN live (2026-07-17): writing 29.85 to MD11_EXTCTL_CAP_BARO put 29.85 into
    // MD11_CAP_ALTIMETER and reset the inbox to -1. It is set in the CURRENT display unit: the
    // read var holds 29.92 in inHg mode and ~1013 in hPa mode, and the write follows it. So a value
    // typed in the OTHER unit must be converted to the display unit before writing, or the FCC
    // reads e.g. "1013 inHg" as nonsense.
    //
    // STD is a MODE, not a value: pushing the baro knob (BaroKnob's press) toggles it, and the
    // "STD" indication lives on the PFD — which is WASM-rendered and unreadable, so the STD state
    // cannot be read back. The push is offered as a toggle, honestly labelled.
    // ---------------------------------------------------------------------------------

    public const string ReadCaptainBaro = "MD11_CAP_ALTIMETER";
    public const string WriteCaptainBaro = "MD11_EXTCTL_CAP_BARO";
    public const string BaroKnob = "MD11_LECP_BAROSET_CAP";   // push = STD toggle

    public const double MinInHg = 26.00;
    public const double MaxInHg = 32.00;
    public const double MinHpa = 900;
    public const double MaxHpa = 1100;

    /// <summary>hPa → inHg. 1013.25 hPa ≡ 29.92 inHg.</summary>
    public static double HpaToInHg(double hpa) => hpa * 0.0295299830714;

    /// <summary>inHg → hPa.</summary>
    public static double InHgToHpa(double inHg) => inHg / 0.0295299830714;

    /// <summary>A typed baro value looks like hPa (three/four digit) rather than inHg (~26-32).</summary>
    public static bool LooksLikeHpa(double v) => v >= 100;

    /// <summary>
    /// Converts a typed value to the unit the display is CURRENTLY in, so "1013" and "29.92" both
    /// do the right thing whichever unit the PFD is set to. <paramref name="displayValue"/> is the
    /// current MD11_CAP_ALTIMETER reading, whose magnitude tells us the unit.
    /// </summary>
    public static double BaroToDisplayUnit(double typed, double displayValue)
    {
        var displayIsHpa = displayValue > 500;
        var typedIsHpa = LooksLikeHpa(typed);
        if (displayIsHpa == typedIsHpa) return typed;
        return displayIsHpa ? InHgToHpa(typed) : HpaToInHg(typed);
    }

    /// <summary>Heading/track is a compass value; 360 is spoken as 360 but written as 0.</summary>
    public static double NormaliseHeading(double degrees)
    {
        var h = degrees % 360;
        if (h < 0) h += 360;
        return h;
    }
}
