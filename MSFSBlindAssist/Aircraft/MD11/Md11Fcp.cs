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
    /// TFDi's "this window is showing dashes" read-back sentinels, EACH ON ITS OWN WINDOW (their
    /// Variables page): EXACTLY -999 on speed and heading, EXACTLY -9999 on vertical speed. Both are
    /// float32-exact, so a half-unit tolerance is the honest compare, and a caller names the window
    /// it reads. Nothing else counts: a real vertical speed runs anywhere in ±6000 fpm, so "anything
    /// at or below -999" — the rule this once was — called a genuine -1000 fpm descent "dashed", and
    /// one test for both sentinels called a selected -999 fpm "dashed, not engaged". Documented as
    /// READ-side only — whether writing a sentinel dashes the window is not documented and was not
    /// probed, so nothing here writes them.
    /// </summary>
    public static bool IsDashedSpeedHeading(double readback) => Math.Abs(readback - DashedSpeedHeading) < 0.5;

    /// <summary>The V/S-FPA window's dash sentinel, -9999 — see <see cref="IsDashedSpeedHeading"/>.</summary>
    public static bool IsDashedVerticalSpeed(double readback) => Math.Abs(readback - DashedVerticalSpeed) < 0.5;

    public const double DashedSpeedHeading = -999;
    public const double DashedVerticalSpeed = -9999;

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
    /// <summary>
    /// The smallest NON-ZERO vertical speed the Ctrl+V dialog takes — conventional, the FCP's V/S
    /// window renders "%04d" and real-aircraft V/S selection is in 100 fpm steps; below this a
    /// number can only be an FPA or a typo. 0 is the one value both windows accept: level off.
    /// </summary>
    public const int MinVerticalSpeedFpm = 100;
    public const double MaxFpaDegrees = 9.9;

    /// <summary>
    /// Which unit a typed Ctrl+V value is written in — numbered as <see cref="WriteVerticalSpeedUnit"/>
    /// takes it (0 = V/S, 1 = FPA) — or null when the number fits neither window.
    ///
    /// The rule: an entry that is valid in the CURRENT mode keeps it; only an entry that is valid in
    /// the OTHER window alone switches to it. A V/S is 0 or ±100-6000 fpm; an FPA is ±0-9.9°. The two
    /// overlap on exactly one value, 0, and there <paramref name="currentIsFpa"/> (the cached
    /// <see cref="ModeVerticalIsFpa"/>) decides — a pilot who types 0 in V/S mode to level off must
    /// NOT be switched to FPA, or the window and Shift+V say FPA and the wheel steps in tenths of a
    /// degree. "-3" still means an FPA and "-1500" a V/S whichever mode is showing, because each is
    /// valid in only one window. 10-99 and anything past 6000 fit nothing and are refused.
    /// </summary>
    public static Md11VerticalUnit? ResolveVerticalUnit(double v, bool currentIsFpa)
    {
        var a = Math.Abs(v);
        bool vsValid = a == 0 || (a >= MinVerticalSpeedFpm && a <= MaxVerticalSpeedFpm);
        bool fpaValid = a <= MaxFpaDegrees;
        if (vsValid && fpaValid) return currentIsFpa ? Md11VerticalUnit.Fpa : Md11VerticalUnit.VerticalSpeed;
        if (vsValid) return Md11VerticalUnit.VerticalSpeed;
        if (fpaValid) return Md11VerticalUnit.Fpa;
        return null;
    }

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

    /// <summary>
    /// The ONE sentence for an FCP action that could not be DELIVERED — a knob push or pull, a
    /// wheel step, a button, an STD row: "<paramref name="name"/> unavailable".
    ///
    /// One owner, because this sentence is the pilot's only evidence that nothing happened, and the
    /// same refusal now reaches four surfaces: the Ctrl+P window, the Ctrl+H/S/A/V dialogs, the
    /// FCU push/pull hotkeys and the panel's altimeter STD rows. It keeps the wording those
    /// surfaces already used, and it is the same shape as the MCDU's own refusal
    /// (<see cref="Md11McduKeys.UndeliverableRefusal"/>, "Not sent. The MCDU 7 key is
    /// unavailable.") — a second phrasing for one class of failure would leave a blind pilot
    /// learning two.
    /// </summary>
    public static string Unavailable(string name) => $"{name} unavailable";

    // The knobs' SPOKEN names, and how an action on one is named. One owner for the same reason
    // Unavailable is: the Ctrl+P window labels its rows from these, and the window, the dialogs and
    // the hotkeys all refuse in these words, so a pilot hears one name for one knob wherever they
    // reach it.
    public const string SpeedKnobName = "Speed";
    public const string HeadingKnobName = "Heading";
    public const string AltitudeKnobName = "Altitude";
    public const string VerticalSpeedName = "Vertical speed";

    /// <summary>Ctrl+B writes all three altimeters at once, so its refusal names them together.</summary>
    public const string AltimetersName = "Altimeters";

    public static string PushAction(string knobName) => $"{knobName} push";
    public static string PullAction(string knobName) => $"{knobName} pull";
    public static string WheelAction(string knobName, bool up) => $"{knobName} wheel {(up ? "up" : "down")}";

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
    // cannot be read back. That is why the Ctrl+B dialog's "Standard" does NOT push the knob: it
    // writes standard pressure as a VALUE to all three inboxes (StandardFor), the PMDG dialogs'
    // way, so the result is readable. The knob's own EFIS row ("Captain Altimeter Setting") is a
    // READ-ONLY status field — its state var is this export (Md11ExportBacked) — and its push, the
    // STD toggle, is pressed by the "Captain Altimeter STD" row (Md11StdToggles), confirmed by the
    // settled announcement of the setting the push changes.
    // ---------------------------------------------------------------------------------

    public const string ReadCaptainBaro = "MD11_CAP_ALTIMETER";
    public const string WriteCaptainBaro = "MD11_EXTCTL_CAP_BARO";
    public const string BaroKnob = "MD11_LECP_BAROSET_CAP";   // push = STD toggle, pressed by Md11StdToggles.Captain

    // The other two altimeters are inboxes of the same contract, PROVEN live (2026-09-06):
    // MD11_EXTCTL_FO_BARO ← 29.85 → MD11_FO_ALTIMETER 29.85; MD11_EXTCTL_STBY_BARO ← 29.80 →
    // MD11_STBY_ALTIMETER 29.8; both inboxes reset to -1. Each is set in ITS OWN display's unit.
    public const string ReadFoBaro = "MD11_FO_ALTIMETER";
    public const string WriteFoBaro = "MD11_EXTCTL_FO_BARO";
    public const string ReadStandbyBaro = "MD11_STBY_ALTIMETER";
    public const string WriteStandbyBaro = "MD11_EXTCTL_STBY_BARO";

    /// <summary>Every altimeter Ctrl+B sets, captain first: (the side as spoken, the export read back, the inbox written).</summary>
    public static readonly (string Side, string Read, string Write)[] Altimeters =
    {
        ("Captain", ReadCaptainBaro, WriteCaptainBaro),
        ("First Officer", ReadFoBaro, WriteFoBaro),
        ("Standby", ReadStandbyBaro, WriteStandbyBaro),
    };

    /// <summary>
    /// How long after an EXTCTL inbox write its export is read back — Ctrl+B's three altimeters,
    /// and the typed minimums (<c>TFDiMD11Definition.MinimumsSettleMs</c> is this constant). The
    /// read itself completes on the export's next 1 Hz delivery (<c>SimConnectManager.ReadFreshAsync</c>),
    /// so this is only the FCC's allowance for CONSUMING the inbox — "within the next FCC cycle",
    /// a period nobody has measured — kept at the minimums' long-standing figure rather than cut:
    /// a settle shorter than the apply latency lets the next delivery carry the PRE-write export
    /// and speak a false "not set". The old 2500 was sized to out-wait two batch deliveries from
    /// a fixed sleep, which a delivery-completed read no longer needs. Measure it from the
    /// "Altimeter read-back" / "Minimums read-back" lines in debug.log before cutting further.
    /// </summary>
    public const int VerifyAfterMs = 1200;

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

    // ---------------------------------------------------------------------------------
    // Reading the altimeter back
    //
    // MD11_CAP_ALTIMETER carries BOTH units and TFDi disambiguate by magnitude — their tooltip
    // renders it as an integer above 500 (hectopascals, 1013) and to two decimals otherwise
    // (inches, 29.92). The read-out speaks both units in the PMDG/Fenix order ("1013, 29.92") so
    // the MD-11 sounds like every other airliner in this app, and "standard" at 29.92 / 1013.25 —
    // the same heuristic the PMDG definitions use, because the aircraft's STD flag lives on the
    // WASM-rendered PFD and the stock KOHLSMAN SETTING STD is never driven (measured 2026-09-06
    // at FL360: 29.92 on the L:var and on the stock var, STD flag 0).
    // ---------------------------------------------------------------------------------

    public const double StandardInHg = 29.92;
    public const double StandardHpa = 1013.25;

    /// <summary>
    /// Standard pressure as a VALUE in the unit the display is currently in — what the dialog's
    /// "Standard" writes to all three altimeters (the PMDG/787 dialogs' way, KOHLSMAN_SET 29.92).
    /// The knob-push STD toggle on the EFIS panel is the aircraft's own STD flag; it stays
    /// available there, but a toggle cannot be commanded to a known state when the flag lives on
    /// the WASM PFD where nothing can read it.
    /// </summary>
    public static double StandardFor(double displayValue) => IsHpa(displayValue) ? StandardHpa : StandardInHg;

    /// <summary>The PMDG definitions' factor, so a 30.12 reads 1020 here exactly as it does on the 737/777.</summary>
    private const double HpaPerInHg = 33.8639;

    /// <summary>TFDi's own rule: a reading above 500 is hectopascals.</summary>
    public static bool IsHpa(double reading) => reading > 500;

    /// <summary>The reading in both units, whichever unit it arrived in.</summary>
    public static (int Hpa, double InHg) AltimeterBothUnits(double reading)
        => IsHpa(reading)
            ? ((int)Math.Round(reading), HpaToInHg(reading))
            : ((int)Math.Round(reading * HpaPerInHg), reading);

    /// <summary>
    /// Standard pressure within the tolerance of the unit displayed: 0.005 inHg (the PMDG rule) or
    /// 0.75 hPa, which admits 1013, 1013.2 and 1013.25 — TFDi's QNE wording is "29.92 or 1013.2 Hp".
    /// </summary>
    public static bool IsStandard(double reading)
        => IsHpa(reading)
            ? Math.Abs(reading - StandardHpa) < 0.75
            : Math.Abs(reading - StandardInHg) < 0.005;

    /// <summary>"standard", or "1013, 29.92" — hPa first, inches to two decimals, invariant culture.</summary>
    public static string DescribeAltimeter(double reading)
    {
        if (IsStandard(reading)) return "standard";
        var (hpa, inHg) = AltimeterBothUnits(reading);
        return $"{hpa.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {inHg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Converts a typed value to the unit the display is CURRENTLY in, so "1013" and "29.92" both
    /// do the right thing whichever unit the PFD is set to. <paramref name="displayValue"/> is that
    /// altimeter's OWN current reading (captain, first officer or standby export), whose magnitude
    /// tells us the unit.
    /// </summary>
    public static double BaroToDisplayUnit(double typed, double displayValue)
    {
        // A typed standard pressure lands on the exact standard value in the display's unit, so
        // "1013" on an inches display writes 29.92 — which the read-back then calls "standard" —
        // rather than the 29.91 the raw conversion gives, one hundredth off and spoken as a QNH.
        if (IsStandard(typed)) return StandardFor(displayValue);

        var displayIsHpa = IsHpa(displayValue);
        var typedIsHpa = LooksLikeHpa(typed);
        if (displayIsHpa == typedIsHpa) return typed;
        return displayIsHpa ? InHgToHpa(typed) : HpaToInHg(typed);
    }

    /// <summary>
    /// A written setting and its read-back describe the same pressure. TFDi's tooltip renders the
    /// export as whole hectopascals above 500 and two-decimal inches below, so the export MAY be
    /// rounded or truncated (1013 for a written 1013.25; 1010.84 as 1011 or 1010) — unmeasured,
    /// which is why the tolerance is one display step of the coarser unit plus a hair, covering
    /// whole, truncated and fractional exports alike: 1.01 hPa, or 0.011 inHg when both sides
    /// are inches. A value that did not take at all is normally many steps away; a one-step miss
    /// is deliberately let through, since a false "not set" would teach a pilot to distrust the
    /// real one.
    /// </summary>
    public static bool AltimeterAgrees(double written, double readBack)
    {
        if (!IsHpa(written) && !IsHpa(readBack)) return Math.Abs(readBack - written) < 0.011;
        var w = IsHpa(written) ? written : InHgToHpa(written);
        var r = IsHpa(readBack) ? readBack : InHgToHpa(readBack);
        return Math.Abs(r - w) < 1.01;
    }

    /// <summary>
    /// After Ctrl+B, the sentence for an altimeter that did NOT take its value — "Standby
    /// altimeter not set, reads 1012, 29.88" — or null when it did, or when nothing has been read
    /// back (an export that never arrived, or one reading 0 — a nonexistent L:var reads a flat 0,
    /// and "reads 0, 0.00" is neither true nor actionable — is no evidence either way). Only a
    /// disagreement is ever spoken: the entry itself was already announced by the screen reader,
    /// and a captain's setting that CHANGED is confirmed by <see cref="Md11AltimeterAnnouncer"/>.
    /// </summary>
    public static string? DescribeAltimeterShortfall(string side, double written, double? readBack)
    {
        if (readBack is not double r || r <= 0 || AltimeterAgrees(written, r)) return null;
        return $"{side} altimeter not set, reads {DescribeAltimeter(r)}";
    }

    /// <summary>Heading/track is a compass value; 360 is spoken as 360 but written as 0.</summary>
    public static double NormaliseHeading(double degrees)
    {
        var h = degrees % 360;
        if (h < 0) h += 360;
        return h;
    }
}

/// <summary>
/// The V/S window's unit, numbered exactly as <see cref="Md11Fcp.WriteVerticalSpeedUnit"/> takes it,
/// so <c>(double)unit</c> is the inbox value.
/// </summary>
public enum Md11VerticalUnit
{
    VerticalSpeed = 0,
    Fpa = 1,
}
