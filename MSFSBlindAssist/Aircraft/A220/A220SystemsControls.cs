using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The pure half of the A220's CommBus CONTROLS: speed-bug readouts, the transponder/TCAS and
/// CTP write payloads and read-back verdicts, the autobrake step walk, the EXT PWR retry and
/// the flight-plan distance phrases. Split from A220Systems.cs only for size; same class.
/// Every number in a payload goes through System.Text.Json and every spoken number through
/// FormattableString.Invariant (VAR-5; pinned under de-DE by SynapticA220SystemsTests).
/// </summary>
internal static partial class A220Systems
{
    // ---- V-speeds (A22X.L PFD Data — the same values the PFD bugs draw) -------------------

    internal static string VSpeedReadout(PfdBlock? p, string which)
    {
        if (p == null) return "V-speeds not available — display link not connected";
        double? v = which switch
        {
            "V1" => p.v1, "VR" => p.vr, "V2" => p.v2, "VREF" => p.vref, "VAC" => p.vac, _ => null
        };
        return v is { } kt && kt > 0 ? Inv($"{which} {kt:0} knots") : $"{which} not set";
    }

    /// <summary>Shift+6: the flap/slat retraction bugs F0-F5 and VT, as the tape labels them.</summary>
    internal static string FormatFlapSpeeds(PfdBlock? p)
    {
        if (p == null) return "Flap speeds not available — display link not connected";
        var parts = new List<string>();
        void Add(string label, double? v) { if (v is { } x && x > 0) parts.Add(Inv($"{label} {x:0}")); }
        Add("F0", p.vf0); Add("F1", p.vf1); Add("F2", p.vf2); Add("F3", p.vf3); Add("F4", p.vf4); Add("F5", p.vf5);
        Add("VT", p.vt);
        return parts.Count == 0 ? "No flap speed bugs set" : "Flap speeds " + string.Join(", ", parts) + " knots";
    }

    /// <summary>
    /// "V-speeds not set" once per take-off roll: true on the sample that carries the IAS up
    /// through <see cref="TakeoffVSpeedCallouts.ArmBelowKnots"/> on the ground with no VR known.
    /// Re-arms below that speed on the ground (a rejected take-off); never fires airborne, so a
    /// landing (decelerating anyway) cannot trip it ([A220-37]).
    /// </summary>
    internal sealed class RollWithoutVrGate
    {
        private bool _armed = true;
        private double _last = double.NaN;

        public bool Observe(double ias, bool onGround, bool vrKnown)
        {
            double last = _last;
            _last = ias;
            if (!onGround) return false;
            if (ias < TakeoffVSpeedCallouts.ArmBelowKnots) { _armed = true; return false; }
            if (_armed && !double.IsNaN(last) && last < TakeoffVSpeedCallouts.ArmBelowKnots)
            {
                _armed = false;
                return !vrKnown;
            }
            return false;
        }

        public void Reset() { _armed = true; _last = double.NaN; }
    }

    // ---- Transponder / TCAS ([A220-35], CNS TransponderControl.tsx) -----------------------

    internal static readonly string[] XpdrModeLabels = { "AUTO", "STBY", "TA/RA", "TA ONLY", "ALT ON", "ALT OFF" };
    internal static readonly string[] AltLimitLabels = { "Normal", "Above", "Below", "Above and Below" };

    internal static string Label(string[] labels, int? i)
        => i is { } n && n >= 0 && n < labels.Length ? labels[n] : "unknown";

    /// <summary>Four octal digits; the CommBus <c>code</c> is those digits read as a decimal
    /// integer (the CNS input parses with parseInt(a, 10)), so squawk 0400 is 400.</summary>
    internal static bool TryParseSquawk(double value, out int code, out string text)
    {
        code = 0;
        text = "";
        if (double.IsNaN(value) || value < 0 || value > 7777 || Math.Abs(value - Math.Round(value)) > 1e-9) return false;
        int c = (int)Math.Round(value);
        string digits = c.ToString("0000", CultureInfo.InvariantCulture);
        if (digits.Any(ch => ch > '7')) return false;
        code = c;
        text = digits;
        return true;
    }

    /// <summary>The stock XPNDR_SET parameter (BCD16), for the verified fallback only.</summary>
    internal static uint SquawkToBcd16(int code)
    {
        uint bcd = 0;
        foreach (char ch in code.ToString("0000", CultureInfo.InvariantCulture)) bcd = (bcd << 4) | (uint)(ch - '0');
        return bcd;
    }

    /// <summary>
    /// The inverse of <see cref="SquawkToBcd16"/>: the squawk (digits read as a decimal integer,
    /// like <c>AtcBlock.code</c>) in a raw <c>TRANSPONDER CODE:1</c> word, or null when the word
    /// is no four-nibble octal code (NaN, fractional, a nibble above 7). The A220 registers that
    /// SimVar with units "Bco16" (SynapticA220Definition.Variables.cs, A22X_XPDR_CODE), which
    /// live 2026-10-15 read the BCD16 word: squawk 2000 as 8192, 2345 as 9029, 1234 as 4660.
    /// (Units "number" read the digits as a decimal, 2000, on this aircraft.) A plain decimal
    /// like 2000 (0x7D0) is therefore NOT a squawk here and reads as null, never guessed.
    /// </summary>
    internal static int? Bcd16ToSquawk(double word)
    {
        if (double.IsNaN(word) || word < 0 || word > 0x7777 || Math.Abs(word - Math.Round(word)) > 1e-9) return null;
        int w = (int)Math.Round(word);
        int code = 0;
        for (int shift = 12; shift >= 0; shift -= 4)
        {
            int digit = (w >> shift) & 0xF;
            if (digit > 7) return null;
            code = code * 10 + digit;
        }
        return code;
    }

    /// <summary>Whether the stock <c>TRANSPONDER CODE:1</c> word read back is the code sent as
    /// <see cref="SquawkToBcd16"/> (the verified XPNDR_SET fallback's compare).</summary>
    internal static bool StockCodeLanded(int code, double stockWord)
        => Bcd16ToSquawk(stockWord) == code;

    internal static string FormatSquawkCode(int? code)
        => code is { } c ? c.ToString("0000", CultureInfo.InvariantCulture) : "----";

    /// <summary>The Transponder panel row: "squawk 2000, TA/RA, altitude limit Normal".</summary>
    internal static string FormatAtc(AtcBlock? a)
    {
        if (a == null) return NotAvailable;
        string s = $"squawk {FormatSquawkCode(a.code)}, {Label(XpdrModeLabels, a.mode)}";
        if (a.mode is { } m && m != 0) s += $", altitude limit {Label(AltLimitLabels, a.tss_limits_sel)}";
        if (a.ident == true) s += ", ident";
        return s;
    }

    /// <summary>X: the bus code and mode, or — when the link is down — the stock
    /// <c>TRANSPONDER CODE:1</c> word (<paramref name="stockCode"/>, BCD16: squawk 2000 is
    /// 8192), or "not available" when that word is no squawk.</summary>
    internal static string FormatSquawkReadout(AtcBlock? a, double stockCode)
    {
        if (a?.code is { } c)
            return $"Squawk {FormatSquawkCode(c)}, transponder {Label(XpdrModeLabels, a.mode)}";
        return Bcd16ToSquawk(stockCode) is { } stock
            ? $"Squawk {FormatSquawkCode(stock)}"
            : $"Squawk {NotAvailable}";
    }

    /// <summary>What a write whose read-back never arrived says: the block (or the field the
    /// verdict reads) is missing, so nothing was read and no state is claimed — never "did not
    /// change — still …", which sends the pilot to press again (a toggle would then undo it).</summary>
    internal static string NotConfirmed(string what) => $"{what} not confirmed: the A220 display link did not report it";

    internal static string? XpdrModeFailure(int want, AtcBlock? after)
        => after?.mode is not { } now ? NotConfirmed("Transponder mode") + "."
         : now == want ? null
         : $"Transponder mode did not change — still {Label(XpdrModeLabels, now)}.";

    internal static string? AltLimitFailure(int want, AtcBlock? after)
        => after?.tss_limits_sel is not { } now ? NotConfirmed("TCAS altitude limit") + "."
         : now == want ? null
         : $"TCAS altitude limit did not change — still {Label(AltLimitLabels, now)}.";

    private static readonly Regex FlightIdPattern = new("^[A-Z0-9]{2,8}$", RegexOptions.CultureInvariant);

    /// <summary>2 to 8 letters or digits (the CNS FLT ID field's /[A-Z0-9]{2,8}/); empty
    /// clears, which the dialog sends as "00000000".</summary>
    internal static bool TryParseFlightId(string? input, out string id)
    {
        string s = (input ?? "").Trim().ToUpperInvariant();
        if (s.Length == 0) { id = "00000000"; return true; }
        if (FlightIdPattern.IsMatch(s)) { id = s; return true; }
        id = "";
        return false;
    }

    internal static bool FlightIdLanded(string sent, string? now)
        => sent == "00000000" ? string.IsNullOrEmpty(now) || now == "00000000" : now == sent;

    internal static string AtcSetModeJson(int mode) => JsonSerializer.Serialize(new { type = "SetMode", mode });
    internal static string AtcSetLimitsJson(int limits) => JsonSerializer.Serialize(new { type = "SetTssLimits", limits });
    internal static string AtcSetCodeJson(int code) => JsonSerializer.Serialize(new { type = "SetCode", code });
    internal static string AtcIdentJson() => JsonSerializer.Serialize(new { type = "Ident" });
    internal static string AtcSetIdJson(string id) => JsonSerializer.Serialize(new { type = "SetAtcId", id });

    // ---- CTP altimeter and minimums ([A220-33]) ---------------------------------------------

    internal const double HpaPerInHg = 33.8639;

    /// <summary>A QNH as the CTP's SetConfig takes it: <c>Type</c> "InHg" or "Hpa".</summary>
    internal readonly record struct QnhSetting(string Type, double Value);

    /// <summary>The Ctrl+B / panel entry: 940-1090 hPa or 27.00-32.00 inches, returned in hPa.</summary>
    internal static bool TryParseQnhInput(double input, out double hpa)
    {
        hpa = 0;
        if (double.IsNaN(input)) return false;
        if (input >= 940 && input <= 1090) { hpa = input; return true; }
        if (input >= 27.0 && input <= 32.0) { hpa = input * HpaPerInHg; return true; }
        return false;
    }

    /// <summary>The setting in the side's OWN unit (the CTP's input parses in its current
    /// unit): whole hectopascals, or inches to two decimals.</summary>
    internal static QnhSetting QnhFor(double hpa, bool unitHpa)
        => unitHpa
            ? new QnhSetting("Hpa", Math.Round(hpa, MidpointRounding.AwayFromZero))
            : new QnhSetting("InHg", Math.Round(hpa / HpaPerInHg, 2, MidpointRounding.AwayFromZero));

    internal static bool? UnitHpaOrNull(CtpBlock? ctp, AdsBlock? ads) => ctp?.qnh?.type switch
    {
        "Hpa" => true,
        "InHg" => false,
        _ => ads?.qnh_unit is { } u ? u == 1 : (bool?)null
    };

    internal static bool UnitIsHpa(CtpBlock? ctp, AdsBlock? ads) => UnitHpaOrNull(ctp, ads) ?? false;

    internal static double? CurrentHpa(CtpBlock? ctp) => ctp?.qnh is { value: { } v } q
        ? (q.type == "Hpa" ? v : v * HpaPerInHg)
        : null;

    internal static bool QnhLanded(CtpBlock? ctp, QnhSetting want)
        => ctp?.qnh is { value: { } v } q && q.type == want.Type
           && Math.Abs(v - want.Value) <= (want.Type == "Hpa" ? 0.6 : 0.006);

    /// <summary>STD as the PFD draws it (ADS <c>qnh_std</c>); the CTP's own flag only when the
    /// ADS store is missing; null when neither says.</summary>
    internal static bool? IsStd(AdsBlock? ads, CtpBlock? ctp) => ads?.qnh_std ?? ctp?.qnh_std;

    internal static string FormatQnh(QnhValue? q) => q switch
    {
        { type: "Hpa", value: { } v } => Inv($"{v:0} hectopascals"),
        { type: "InHg", value: { } v } => Inv($"{v:0.00} inches"),
        _ => "unknown"
    };

    /// <summary>One side's altimeter as its PFD shows it: "standard", "30.13 inches",
    /// "1020 hectopascals"; null when nothing is known.</summary>
    internal static string? SideAltimeter(AdsBlock? ads, CtpBlock? ctp)
    {
        if (IsStd(ads, ctp) == true) return "standard";
        if (ads?.qnh_unit == 1 && ads.qnh_hpa is { } h) return Inv($"{h:0} hectopascals");
        if (ads?.qnh_unit == 0 && ads.qnh_inhg is { } i) return Inv($"{i:0.00} inches");
        return ctp?.qnh != null ? FormatQnh(ctp.qnh) : null;
    }

    /// <summary>B: the captain's altimeter, plus the first officer's only when it differs.</summary>
    internal static string? FormatAltimeterReadout(AdsBlock? lads, CtpBlock? lctp, AdsBlock? rads, CtpBlock? rctp)
    {
        string? cap = SideAltimeter(lads, lctp);
        if (cap == null) return null;
        string? fo = SideAltimeter(rads, rctp);
        return fo != null && fo != cap ? $"Altimeter {cap}, first officer {fo}" : $"Altimeter {cap}";
    }

    internal static string FormatAltimeterFallback(double inHg, double mb)
        => Inv($"Altimeter {inHg:0.00} inches, {mb:0} hectopascals. Standard not known — the display link is not connected.");

    internal static string FormatMinimums(CtpBlock? ctp)
    {
        if (ctp == null) return NotAvailable;
        string mode = ctp.minimums_mode switch { 0 => "baro", 1 => "radio", _ => "unknown mode" };
        return ctp.minimums is { } ft ? Inv($"{mode} {ft:0} feet") : $"{mode}, not set";
    }

    internal static bool TryParseMinimums(double value, out int feet)
    {
        feet = 0;
        if (double.IsNaN(value) || value < 0 || value > 41000) return false;
        feet = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>The Ctrl+B QNH box's starting text, in the side's own unit.</summary>
    internal static string? QnhEntryText(CtpBlock? ctp) => ctp?.qnh switch
    {
        { type: "Hpa", value: { } v } => Inv($"{v:0}"),
        { type: "InHg", value: { } v } => Inv($"{v:0.00}"),
        _ => null
    };

    internal static string SideWord(int side) => side == 2 ? "First officer" : "Captain";

    internal static (CtpBlock? Ctp, AdsBlock? Ads) SideBlocks(SystemsBlocks? s, int side)
        => side == 2 ? (s?.rctp, s?.rads) : (s?.lctp, s?.lads);

    /// <summary>The problems a write left, in order, as one spoken verdict; null when there are none.</summary>
    private static string? Problems(List<string> problems)
        => problems.Count == 0 ? null : string.Join(". ", problems) + ".";

    /// <summary>A QNH read back in a known unit (the field the QNH verdicts read).</summary>
    private static bool QnhReadBack(CtpBlock? ctp) => ctp?.qnh is { type: "Hpa" or "InHg", value: not null };

    internal static string QnhResult(IReadOnlyDictionary<int, QnhSetting> wanted, SystemsBlocks? after)
    {
        var sides = wanted.Keys.OrderBy(side => side).ToList();
        var problems = new List<string>();
        foreach (int side in sides)
        {
            var ctp = SideBlocks(after, side).Ctp;
            if (!QnhReadBack(ctp)) problems.Add(NotConfirmed($"{SideWord(side)} altimeter"));
            else if (!QnhLanded(ctp, wanted[side]))
                problems.Add($"{SideWord(side)} altimeter did not take the setting — reads {FormatQnh(ctp!.qnh)}");
        }
        if (Problems(problems) is { } failed) return failed;
        // What each side READ BACK, not the setting wanted: each side was sent its own unit.
        string text = QnhSidesText(sides, after);
        bool stdStill = sides.Any(side => IsStd(SideBlocks(after, side).Ads, SideBlocks(after, side).Ctp) == true);
        return stdStill ? text + " — standard is still selected" : text;
    }

    internal static string StdResult(IReadOnlyList<int> sides, bool wantStd, SystemsBlocks? after)
    {
        var problems = new List<string>();
        foreach (int side in sides)
        {
            bool? now = IsStd(SideBlocks(after, side).Ads, SideBlocks(after, side).Ctp);
            if (now == null) problems.Add(NotConfirmed($"{SideWord(side)} baro"));
            else if (now != wantStd) problems.Add($"{SideWord(side)} baro did not change — still {(wantStd ? "QNH" : "standard")}");
        }
        if (Problems(problems) is { } failed) return failed;
        if (wantStd) return sides.Count == 2 ? "Altimeter standard, both sides" : $"{SideWord(sides[0])} altimeter standard";
        return QnhSidesText(sides, after);
    }

    /// <summary>The altimeter each named side now reads, from the CTP stores. Two sides are
    /// "both sides" only when they read alike: Standard and the unit switch work from each
    /// side's own value, so they can differ (the file's <see cref="FormatAltimeterReadout"/>
    /// exists for the same reason), and then each side is named, captain first.</summary>
    private static string QnhSidesText(IReadOnlyList<int> sides, SystemsBlocks? after)
    {
        var ordered = sides.OrderBy(side => side).ToList();
        string Reads(int side) => FormatQnh(SideBlocks(after, side).Ctp?.qnh);
        if (ordered.Count != 2) return $"{SideWord(ordered[0])} altimeter {Reads(ordered[0])}";
        string a = Reads(ordered[0]), b = Reads(ordered[1]);
        return a == b
            ? $"Altimeter {a}, both sides"
            : $"{SideWord(ordered[0])} altimeter {a}, {SideWord(ordered[1]).ToLowerInvariant()} {b}";
    }

    internal static string UnitResult(IReadOnlyList<int> sides, bool wantHpa, SystemsBlocks? after)
    {
        string wantType = wantHpa ? "Hpa" : "InHg";
        var problems = new List<string>();
        if (sides.Any(side => QnhReadBack(SideBlocks(after, side).Ctp) && SideBlocks(after, side).Ctp!.qnh!.type != wantType))
            problems.Add($"The A220 did not change the altimeter unit — still {(wantHpa ? "inches" : "hectopascals")}");
        problems.AddRange(sides.Where(side => !QnhReadBack(SideBlocks(after, side).Ctp))
            .Select(side => NotConfirmed($"{SideWord(side)} altimeter unit")));
        return Problems(problems) ?? QnhSidesText(sides, after);
    }

    internal static string MinimumsResult(IReadOnlyList<int> sides, int feet, SystemsBlocks? after)
    {
        var problems = new List<string>();
        foreach (int side in sides)
        {
            var ctp = SideBlocks(after, side).Ctp;
            if (ctp?.minimums is not { } m) problems.Add(NotConfirmed($"{SideWord(side)} minimums"));
            else if (Math.Abs(m - feet) >= 0.5)
                problems.Add(Inv($"{SideWord(side)} minimums did not take {feet} feet — reads {FormatMinimums(ctp)}"));
        }
        if (Problems(problems) is { } failed) return failed;
        return sides.Count == 2
            ? Inv($"Minimums {feet} feet, both sides")
            : $"{SideWord(sides[0])} minimums {FormatMinimums(SideBlocks(after, sides[0]).Ctp)}";
    }

    internal static string? MinimumsModeFailure(IReadOnlyList<int> sides, int mode, SystemsBlocks? after)
    {
        var kept = sides.Where(side => SideBlocks(after, side).Ctp?.minimums_mode is { } m && m != mode).ToList();
        var problems = new List<string>();
        if (kept.Count > 0)
            problems.Add("Minimums mode did not change — " + string.Join(", ", kept.Select(side =>
                $"{SideWord(side).ToLowerInvariant()} still {FormatMinimums(SideBlocks(after, side).Ctp)}")));
        problems.AddRange(sides.Where(side => SideBlocks(after, side).Ctp?.minimums_mode == null)
            .Select(side => NotConfirmed($"{SideWord(side)} minimums mode")));
        return Problems(problems);
    }

    internal static string CtpQnhJson(QnhSetting q) => JsonSerializer.Serialize(new { qnh = new { type = q.Type, value = q.Value } });
    internal static string CtpStdJson(bool std) => JsonSerializer.Serialize(new { qnh_std = std });
    internal static string CtpMinimumsModeJson(int mode) => JsonSerializer.Serialize(new { minimums_mode = mode });
    internal static string CtpMinimumsJson(int feet) => JsonSerializer.Serialize(new { minimums = feet });

    // ---- Autobrake ([A220-34]: 0 RTO, 1 OFF, 2 LO, 3 MED, 4 HI) ------------------------------

    internal static readonly string[] AutobrakeLabels = { "RTO", "OFF", "LO", "MED", "HI" };

    internal static string? AutobrakeStepEvent(int current, int target)
        => target > current ? "INCREASE_AUTOBRAKE_CONTROL" : target < current ? "DECREASE_AUTOBRAKE_CONTROL" : null;

    internal static int AutobrakeNextStep(int current, int target) => current + Math.Sign(target - current);

    internal static string AutobrakeRefusal(int stuckAt, int refusedStep, bool onGround)
        => $"Autobrake {Label(AutobrakeLabels, refusedStep)} not available{(onGround ? " on the ground" : "")} — still {Label(AutobrakeLabels, stuckAt)}";

    internal enum AutobrakeStepVerdict { Moved, Refused, NotConfirmed }

    /// <summary>What a step's read-back means. Only a DELIVERED value equal to where the step started
    /// is a refusal; a read that never arrived (null: timeout, disconnect) says nothing about the knob,
    /// which may well have moved, so it is never spoken as a refusal or as a position.</summary>
    internal static AutobrakeStepVerdict AutobrakeStepVerdictFor(int before, double? after)
        => after is not { } a ? AutobrakeStepVerdict.NotConfirmed
         : (int)Math.Round(a) == before ? AutobrakeStepVerdict.Refused
         : AutobrakeStepVerdict.Moved;

    internal static string AutobrakeNotConfirmed(int target)
        => $"Autobrake {Label(AutobrakeLabels, target)} not confirmed: the selector is not reporting its position.";

    // ---- External power: ONE automatic retry (live 2026-10-12: the first disconnect lost) ---

    internal enum ExtPowerVerdict { Done, Retry, Fail }

    internal static ExtPowerVerdict ExtPowerCheck(bool wantOn, double state, bool gpuAttached, bool alreadyRetried)
    {
        if (state < 0) return ExtPowerVerdict.Done;
        if ((state >= 0.5) == wantOn) return ExtPowerVerdict.Done;
        if (wantOn && !gpuAttached) return ExtPowerVerdict.Fail;
        return alreadyRetried ? ExtPowerVerdict.Fail : ExtPowerVerdict.Retry;
    }

    // ---- Flight plan distances (A220.Flight Plan Update) and the ND TO waypoint -----------

    internal static string FormatDuration(double seconds)
    {
        int total = (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);
        int h = total / 60, m = total % 60;
        string mins = m == 1 ? "1 minute" : Inv($"{m} minutes");
        if (h == 0) return mins;
        string hrs = h == 1 ? "1 hour" : Inv($"{h} hours");
        return m == 0 ? hrs : $"{hrs} {mins}";
    }

    internal static string FormatDistanceToDestination(FplnProgress? p)
        => p?.ok == true && p.dtg is { } d
            ? Inv($"Destination {d:0} miles") + (p.ete is { } s && s > 0 ? $", {FormatDuration(s)}" : "")
            : "Distance to destination not available — the A220 display link has no active flight plan";

    internal static string FormatDistanceToTod(FplnProgress? p)
    {
        if (p?.ok != true) return "Distance to top of descent not available — the A220 display link has no active flight plan";
        if (p.tod is not { } t) return "No top of descent in the active flight plan";
        // tod = dtg - (plan length - TOD from the plan start): negative once it is behind us.
        return t < 0.5 ? "Top of descent passed" : Inv($"Top of descent in {t:0} miles");
    }

    /// <summary>The ND's TO waypoint. A VOR/LOC source has no FMS waypoint, and is named the way
    /// the Radios window names it (<see cref="A220RadioState.SourceName"/>: "VOR 1").</summary>
    internal static string FormatNdWaypoint(NavBlock? n)
    {
        if (n == null) return "Next waypoint not available — display link not connected";
        if (A220RadioState.IsRadioSource(n.source))
            return $"The captain's navigation source is {A220RadioState.SourceName(n.source)} — no FMS waypoint";
        string? ident = n.to is { ValueKind: JsonValueKind.String } t ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(ident)) return "No active waypoint";
        return n.dtg is { } d ? Inv($"Next waypoint {ident}, {d:0.0} miles") : $"Next waypoint {ident}";
    }
}
