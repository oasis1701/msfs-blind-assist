using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The BlueBird 757's three altimeters and its radio minimums: the typed input, the stock events
/// that set them, the read-backs and the words. Pure; <see cref="BBS757Definition"/> wires it to the
/// sim through <see cref="McpIo"/> (a fresh read, a calculator write, an awaitable wait).
///
/// Measured 2026-10-25 (docs/b757.md, "Altimeters, minimums and squawk"):
///   • <c>K:KOHLSMAN_SET</c> with 16 × hPa set ALL THREE altimeters (<c>KOHLSMAN SETTING MB:1..3</c>),
///     and the reads lag a few seconds, so a set is read back for up to
///     <see cref="AltimeterReadBackMaxMs"/>. (<c>K:KOHLSMAN_INC</c> moves only altimeter 1: not used.)
///   • The decision height is the stock <c>DECISION HEIGHT</c>, which the ADI shows ("DH200") and
///     which is read-only; each <c>K:INCREASE_DECISION_HEIGHT</c> / <c>DECREASE_</c> moves it 10 ft.
///     So a minimums set steps the difference and closes the loop on what it reads, as the MCP
///     speed set does.
///
/// Every number goes out and is spoken with the invariant culture (VAR-5, CORE-17).
/// </summary>
public static class Bbs757Altimeters
{
    public const string Kohlsman1Key = "BBS757_KOHLSMAN_1";
    public const string Kohlsman2Key = "BBS757_KOHLSMAN_2";
    public const string Kohlsman3Key = "BBS757_KOHLSMAN_3";
    public const string DecisionHeightKey = "BBS757_DECISION_HEIGHT";

    /// <summary>The three altimeters' read-back keys, altimeter 1 first.</summary>
    public static IReadOnlyList<string> KohlsmanKeys { get; } = new[] { Kohlsman1Key, Kohlsman2Key, Kohlsman3Key };

    /// <summary>Standard pressure, 1013.25 hPa (29.92 inHg), as <c>KOHLSMAN_SET</c>'s 16 × hPa.</summary>
    public const uint StandardParameter = 16212;

    /// <summary>The wait between an altimeter set and each read of the three.</summary>
    public const int AltimeterPollMs = 1000;

    /// <summary>The longest an altimeter set waits for all three to follow before it says what they read.</summary>
    public const int AltimeterReadBackMaxMs = 6000;

    /// <summary>Two altimeters within this many hPa read as one setting.</summary>
    private const double AgreeHpa = 0.5;

    /// <summary>The lowest and highest radio minimums a set takes or a read is believed, in feet.</summary>
    public const int MinDh = 0, MaxDh = 2500;

    /// <summary>Most K-events one round of a minimums set sends: the whole 0 to 2500 ft span in tens fits.</summary>
    public const int MaxDhStepsPerRound = 250;

    /// <summary>Most times a minimums set re-sends its remainder before it speaks what it reads.</summary>
    public const int MaxDhCorrections = 3;

    private const string AltimeterRangeError = "Enter hectopascals (940 to 1090) or inches (27.00 to 32.00)";
    private const string AltimeterNumberError = "Enter a pressure like 1013 or 29.92";
    private const string MinimumsRangeError = "Minimums must be between 0 and 2500 feet";

    // ---- altimeters ---------------------------------------------------------------------------

    /// <summary>
    /// The <c>KOHLSMAN_SET</c> parameter, 16 × hPa rounded, for a pressure in hectopascals (100 or
    /// more) or inches of mercury. Standard (29.92 inches by <see cref="AltimeterPhrase.IsStandardInHg"/>,
    /// or 1013 or 1013.25 hPa) is <see cref="StandardParameter"/>, so typing it equals the Standard
    /// button (29.92 inches alone would be 1013.21 hPa, 16211).
    /// </summary>
    public static uint KohlsmanParameter(double hpaOrInHg)
    {
        bool standard = hpaOrInHg >= 100
            ? hpaOrInHg is 1013 or 1013.25
            : AltimeterPhrase.IsStandardInHg(hpaOrInHg);
        if (standard) return StandardParameter;
        double hpa = hpaOrInHg >= 100 ? hpaOrInHg : hpaOrInHg * AltimeterPhrase.HpaPerInHg;
        return (uint)Math.Round(hpa * 16, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A typed altimeter setting (CORE-17: either decimal separator, a thousands group refused):
    /// hectopascals 940 to 1090, or inches 27.00 to 32.00.
    /// </summary>
    public static bool TryAltimeterInput(string? input, out uint parameter, out string? error)
    {
        parameter = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = AltimeterNumberError; return false; }
        if (value is not (>= 940 and <= 1090 or >= 27 and <= 32)) { error = AltimeterRangeError; return false; }
        parameter = KohlsmanParameter(value);
        error = null;
        return true;
    }

    /// <summary>The calculator string that sets all three altimeters.</summary>
    public static string KohlsmanWrite(uint parameter)
        => $"{parameter.ToString(CultureInfo.InvariantCulture)} (>K:KOHLSMAN_SET)";

    /// <summary>
    /// The three altimeters (hPa) as the B key and a set speak them: once when all three were read and
    /// agree ("Altimeter standard", "Altimeter: 1018, 30.05", the words every aircraft uses), else one
    /// by one ("Altimeter 1: 1020, 30.12. Altimeter 2: standard. Altimeter 3: unavailable"); nothing
    /// read: "Altimeters unavailable". Which altimeter is the captain's, the first officer's or the
    /// standby is unmeasured, so they are numbered as the simulator numbers them.
    /// </summary>
    public static string AltimetersPhrase(double? mb1, double? mb2, double? mb3)
    {
        var mbs = new[] { Readable(mb1), Readable(mb2), Readable(mb3) };
        if (mbs.All(m => m == null)) return "Altimeters unavailable";
        if (mbs.All(m => m != null) && mbs.Max()!.Value - mbs.Min()!.Value <= AgreeHpa)
            return AltimeterPhrase.Sentence(mbs[0]!.Value / AltimeterPhrase.HpaPerInHg);

        var parts = new List<string>(3);
        for (int i = 0; i < 3; i++)
            parts.Add($"Altimeter {(i + 1).ToString(CultureInfo.InvariantCulture)}: {OneAltimeter(mbs[i])}");
        return string.Join(". ", parts);
    }

    private static string OneAltimeter(double? mb)
    {
        if (mb is not double hpa) return "unavailable";
        double inHg = hpa / AltimeterPhrase.HpaPerInHg;
        return AltimeterPhrase.IsStandardInHg(inHg)
            ? "standard"
            : AltimeterPhrase.BothUnits((int)Math.Round(hpa, MidpointRounding.AwayFromZero), inHg);
    }

    private static double? Readable(double? mb) => mb is double v && double.IsFinite(v) && v > 0 ? v : null;

    /// <summary>Output B: "Altimeter: 1020, 30.12. DH 200".</summary>
    public static string Readout(double? mb1, double? mb2, double? mb3, double? decisionHeight)
        => $"{AltimetersPhrase(mb1, mb2, mb3)}. {DhPhrase(decisionHeight)}";

    /// <summary>
    /// Sets all three altimeters with one <c>KOHLSMAN_SET</c>, then reads the three every
    /// <see cref="AltimeterPollMs"/> until they all show the set pressure or
    /// <see cref="AltimeterReadBackMaxMs"/> has passed (they lag a few seconds), and speaks what they
    /// read, never the request. Throws <see cref="OperationCanceledException"/> when canceled.
    /// </summary>
    public static async Task<string> SetAltimetersAsync(McpIo io, uint parameter, CancellationToken ct)
    {
        io.Write(KohlsmanWrite(parameter));
        double target = parameter / 16.0;
        double?[] read = { null, null, null };
        for (int waited = 0; waited < AltimeterReadBackMaxMs;)
        {
            await io.Delay(AltimeterPollMs, ct);
            waited += AltimeterPollMs;
            for (int i = 0; i < 3; i++) read[i] = await io.Read(KohlsmanKeys[i], ct);
            if (read.All(m => m is double v && Math.Abs(v - target) <= AgreeHpa)) break;
        }
        return AltimetersPhrase(read[0], read[1], read[2]);
    }

    // ---- radio minimums ------------------------------------------------------------------------

    /// <summary>Typed radio minimums: 0 to 2500 feet, rounded to the nearest 10 (a step is 10 ft).</summary>
    public static bool TryMinimumsInput(string? input, out int feet, out string? error)
    {
        feet = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = "Enter the minimums in feet, like 200"; return false; }
        if (value < MinDh || value > MaxDh) { error = MinimumsRangeError; return false; }
        feet = (int)(Math.Round(value / 10, MidpointRounding.AwayFromZero) * 10);
        error = null;
        return true;
    }

    /// <summary>
    /// The calls that step the decision height from <paramref name="current"/> to
    /// <paramref name="target"/>: one <c>K:INCREASE_DECISION_HEIGHT</c> or <c>DECREASE_</c> per 10 ft of
    /// the difference (rounded), at most <see cref="MaxDhStepsPerRound"/>, in calls the calculator
    /// takes. Empty for less than half a step or a value that is not a number.
    /// </summary>
    public static IReadOnlyList<string> DhSteps(double current, double target)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return Array.Empty<string>();
        long steps = StepsBetween(current, target);
        if (steps == 0) return Array.Empty<string>();
        return Bbs757Rpn.KeyBurst(steps > 0 ? "INCREASE_DECISION_HEIGHT" : "DECREASE_DECISION_HEIGHT",
            (int)Math.Min(Math.Abs(steps), MaxDhStepsPerRound));
    }

    private static long StepsBetween(double current, double target)
        => (long)Math.Clamp(Math.Round((target - current) / 10, MidpointRounding.AwayFromZero), -1e9, 1e9);

    /// <summary>
    /// A decision height read that can be believed (0 to 2500 ft): the step counts of a set rest on
    /// it, so a wild one sends nothing rather than a flood of K-events.
    /// </summary>
    private static bool DhReadable(double? feet) => feet is double v && double.IsFinite(v) && v >= MinDh && v <= MaxDh;

    /// <summary>"DH 200", as the ADI shows it; "DH unavailable" when not read or not a sane height.</summary>
    public static string DhPhrase(double? feet)
        => DhReadable(feet)
            ? $"DH {((long)Math.Round(feet!.Value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)}"
            : "DH unavailable";

    /// <summary>
    /// Sets the radio minimums by stepping the decision height, closing the loop on what it reads, as
    /// the MCP speed set does: read it fresh, send the steps, wait, read until it settles, and while
    /// the remainder is non-zero and smaller than before send it again, up to
    /// <see cref="MaxDhCorrections"/> times. Speaks the value read last. An unreadable start sends
    /// nothing ("DH unavailable"). Throws <see cref="OperationCanceledException"/> when canceled.
    /// </summary>
    public static async Task<string> SetDecisionHeightAsync(McpIo io, int feet, CancellationToken ct)
    {
        double? current = await io.Read(DecisionHeightKey, ct);
        if (!DhReadable(current)) return DhPhrase(null);

        long remaining = Math.Abs(StepsBetween(current!.Value, feet));
        if (remaining == 0) return DhPhrase(current);

        foreach (var call in DhSteps(current.Value, feet)) io.Write(call);

        double? now = current;
        for (int corrections = 0; ; corrections++)
        {
            await io.Delay(Bbs757Mcp.StepSettleMs, ct);
            now = await Bbs757Mcp.ReadSettledAsync(io, DecisionHeightKey, ct);
            if (!DhReadable(now)) break;

            long left = Math.Abs(StepsBetween(now!.Value, feet));
            if (left == 0 || left >= remaining || corrections == MaxDhCorrections) break;

            remaining = left;
            foreach (var call in DhSteps(now.Value, feet)) io.Write(call);
        }
        return DhPhrase(now);
    }
}
