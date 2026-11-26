using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// What the MCP set loops need from the sim, as delegates so the loops stay pure and a test can
/// stand a simulated MCP behind them: a fresh read of a variable (null when it cannot be read), a
/// calculator write, and an awaitable wait.
/// </summary>
public sealed class McpIo
{
    public required Func<string, CancellationToken, Task<double?>> Read { get; init; }
    public required Action<string> Write { get; init; }
    public required Func<int, CancellationToken, Task> Delay { get; init; }

    /// <summary>
    /// A batched variable's last delivered value, at most one batch period (1 s) old; null when none
    /// has arrived, or when there is no cache (then the readout reads fresh). A fresh read of a batched
    /// variable waits for the next batch, up to a second.
    /// </summary>
    public Func<string, double?>? Cached { get; init; }
}

/// <summary>
/// A speed change cut into bursts (<see cref="Bbs757Mcp.SpeedBursts"/>): <c>Bulk</c>, the events of
/// the one planned burst, then <c>Rest</c>, the knots left for a burst of their own. A record, not a
/// tuple: C# reserves the tuple element name <c>Rest</c>.
/// </summary>
public readonly record struct Bbs757SpeedBurstPlan(int Bulk, int Rest);

/// <summary>
/// The BlueBird 757's MCP (autopilot panel) values: how each is set and read, the typed-input
/// rules and the words spoken. Pure; <see cref="BBS757Definition"/> wires it to the sim.
///
/// What was measured on 2026-10-25 (docs/b757.md, "The MCP value windows"):
///   • Heading <c>L:BBS_AP_TARGET_HEADING</c> and altitude <c>L:BBS_AP_TARGET_ALTITUDE</c> adopt a
///     direct write, so an exact value is one write.
///   • The speed target <c>L:BBS757_ORANGE_AIRSPEED_BUGS</c> is READ-ONLY (a direct write is
///     overwritten) and moves only by <c>K:AP_SPD_VAR_INC/DEC</c>, which BlueBird ACCELERATES within
///     a burst, as a knob turned fast: events 1-10 move 1 kt each, 11-25 2 kt, from the 26th 5 kt
///     (10 events +10, 20 +30, 25 +40, 30 +65; the first flight's 53 +180, and its 47 hit the 100 kt
///     floor). A burst is one calculator call or several back to back; a pause of about 0.3 s
///     starts a new one. So a speed is set by one planned burst and its 0-4 kt rest
///     (<see cref="SpeedBursts"/>, rule B757-3), and the loop reads it back and corrects in bursts
///     that stay on the 1 kt tier.
///   • <c>L:BBS_AP_TARGET_VS</c> is unmeasured: written directly first, stepped by
///     <c>K:AP_VS_VAR_INC/DEC</c> when the window did not take the write.
///   • The stock autopilot SimVars sit at unrelated offsets and are never read here.
///
/// Every spoken value is the one READ back from the MCP's own variable, never the request: a set the
/// aircraft did not take is heard as it is. Numbers are formatted with the invariant culture, since
/// a comma decimal makes the RPN parser reject the string and a locale's minus sign (U+2212 in
/// sv-SE) is not a word a screen reader reads as "minus".
/// </summary>
public static class Bbs757Mcp
{
    public const string HeadingVar = "BBS_AP_TARGET_HEADING";
    public const string AltitudeVar = "BBS_AP_TARGET_ALTITUDE";
    public const string SpeedVar = "BBS757_ORANGE_AIRSPEED_BUGS";
    public const string VsVar = "BBS_AP_TARGET_VS";
    public const string MachModeVar = "BBS_AIRSPEED_IN_MACH";

    /// <summary>1 while the MCP speed window is blank (VNAV owns the speed); Speed Intervene opens it (session 8).</summary>
    public const string IasBlankedVar = "BBS_IAS_BLANKED";

    /// <summary>
    /// The speed window's hundreds wheel; in Mach mode it shows the tenths digit (10·d). The Mach is read
    /// from the wheels: <c>L:BBS_TARGET_MACH</c> stays 0 in Mach mode (measured at FL290 on 2026-10-26, [B757-50]).
    /// </summary>
    public const string MachTenthsDrumVar = "B757_MCP_Gau_SPD_AUTO_100";

    /// <summary>The speed window's tens wheel; in Mach mode it shows the hundredths digit (10·(d+1), so 0 reads 10).</summary>
    public const string MachHundredthsDrumVar = "B757_MCP_Gau_SPD_AUTO_10";

    /// <summary>What Shift+S says while VNAV has blanked the speed window (ruling R58).</summary>
    public const string BlankWindowNote = "MCP speed window blank. Speed Intervene opens it.";

    /// <summary>The wait before a set is read back (the window takes a write within a frame or two; this is generous).</summary>
    public const int ReadBackMs = 1000;

    /// <summary>The wait between a burst of K-events and the first read that judges it.</summary>
    public const int StepSettleMs = 600;

    /// <summary>
    /// After a speed burst the target is read every this many ms until two reads in a row agree: a
    /// burst can land in more than one part, and judging a half-landed one would re-send the part
    /// still to come and overshoot.
    /// </summary>
    public const int SettlePollMs = 150;

    /// <summary>The settle read gives up after this long and judges the last read it has.</summary>
    public const int SettleGiveUpMs = 1500;

    /// <summary>Most times a speed set re-sends its remainder before it speaks what it reads.</summary>
    public const int MaxSpeedCorrections = 3;

    /// <summary>
    /// The pause between two speed bursts, so the second starts BlueBird's count again at 1 kt an
    /// event: twice the measured reset (a pause of about 0.3 s starts a new burst), for margin.
    /// </summary>
    public const int SpeedBurstGapMs = 600;

    /// <summary>Events in one speed burst that each move the target 1 kt: BlueBird's first tier.</summary>
    public const int LinearBurstMax = 10;

    /// <summary>The last event of a burst that moves 2 kt; every later one moves 5.</summary>
    private const int TwoKnotBurstMax = 25;

    /// <summary>Most K-events one round of a V/S set sends: the whole -8000 to 6000 span in hundreds fits (140).</summary>
    public const int MaxVsStepsPerRound = 140;

    /// <summary>The speed target reads inside 0 to this many knots; anything else is an unreadable value, not a speed.</summary>
    public const double SpeedReadMax = 500;

    /// <summary>The V/S target reads inside these limits (feet per minute); anything else is unreadable.</summary>
    public const double VsReadMin = -8000, VsReadMax = 6000;

    public const string MachModeReadNote = "MCP is in Mach mode. Mach is not read yet.";

    private const string VsRangeError = "Vertical speed must be between minus 8000 and 6000 feet per minute";

    // =================================================================================
    // RPN
    // =================================================================================

    /// <summary>
    /// The calls that step the V/S target from <paramref name="current"/> to <paramref name="target"/>:
    /// one <c>K:AP_VS_VAR_INC</c> or <c>DEC</c> per hundred feet per minute of the difference
    /// (assumed, unmeasured: the plan's 100 fpm per event), at most <see cref="MaxVsStepsPerRound"/>.
    /// Empty for less than half a step.
    /// </summary>
    public static IReadOnlyList<string> VsSteps(double current, double target)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return Array.Empty<string>();
        long diff = Round((target - current) / 100.0);
        return Steps("AP_VS_VAR_INC", "AP_VS_VAR_DEC", diff, MaxVsStepsPerRound);
    }

    /// <summary>
    /// The knots a burst of <paramref name="events"/> moves the speed target, by BlueBird's measured
    /// tiers: events 1 to <see cref="LinearBurstMax"/> move 1 kt each, the 11th to the 25th 2 kt, every
    /// one after that 5 kt. 0 for no events.
    /// </summary>
    public static int SpeedBurstKnots(int events)
    {
        if (events <= 0) return 0;
        if (events <= LinearBurstMax) return events;
        if (events <= TwoKnotBurstMax) return LinearBurstMax + 2 * (events - LinearBurstMax);
        return SpeedBurstKnots(TwoKnotBurstMax) + 5 * (events - TwoKnotBurstMax);
    }

    /// <summary>
    /// How a speed change of <paramref name="knots"/> (a magnitude) is sent: <c>Bulk</c>, the most
    /// events one burst can fire without moving past it (<see cref="SpeedBurstKnots"/>), then
    /// <c>Rest</c>, the 0 to 4 knots the 2 and 5 kt steps cannot land on, sent as a burst of its own
    /// after <see cref="SpeedBurstGapMs"/>, so each of its events moves 1 kt. (0, 0) for no change.
    /// </summary>
    public static Bbs757SpeedBurstPlan SpeedBursts(int knots)
    {
        if (knots <= 0) return new Bbs757SpeedBurstPlan(0, 0);
        int twoKnotTop = SpeedBurstKnots(TwoKnotBurstMax);
        int bulk = knots <= LinearBurstMax ? knots
            : knots <= twoKnotTop ? LinearBurstMax + (knots - LinearBurstMax) / 2
            : TwoKnotBurstMax + (knots - twoKnotTop) / 5;
        return new Bbs757SpeedBurstPlan(bulk, knots - SpeedBurstKnots(bulk));
    }

    /// <summary>
    /// One calculator call that fires <paramref name="events"/> <c>K:AP_SPD_VAR_INC</c> (or <c>DEC</c>
    /// going down) as ONE burst: an RPN loop that counts register 0 down to zero, one event a pass
    /// (measured live), so the string stays short for any count. No unique prefix: the definition's
    /// <c>ExecuteCalculatorCodeUnique</c> adds it. The count is written with the invariant culture.
    /// </summary>
    public static string SpeedBurstCode(bool up, int events)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(events);
        string kEvent = up ? "AP_SPD_VAR_INC" : "AP_SPD_VAR_DEC";
        return $"{events.ToString(CultureInfo.InvariantCulture)} sp0 :1 l0 0 > if{{ (>K:{kEvent}) l0 1 - sp0 g1 }}";
    }

    /// <summary>Writes the heading target: 0-359, 360 and above wrap, a negative heading wraps the other way.</summary>
    public static string HeadingWrite(int deg) => Bbs757Rpn.SetLVar(HeadingVar, ((deg % 360) + 360) % 360);

    /// <summary>Writes the altitude target, rounded to the nearest hundred feet (half up).</summary>
    public static string AltitudeWrite(int ft) => Bbs757Rpn.SetLVar(AltitudeVar, RoundToHundred(ft));

    /// <summary>Writes the V/S target in feet per minute, signed.</summary>
    public static string VsWrite(int fpm) => Bbs757Rpn.SetLVar(VsVar, fpm);

    private static IReadOnlyList<string> Steps(string inc, string dec, long diff, int maxSteps)
    {
        if (diff == 0) return Array.Empty<string>();
        return Bbs757Rpn.KeyBurst(diff > 0 ? inc : dec, (int)Math.Min(Math.Abs(diff), maxSteps));
    }

    private static long Round(double value)
        => (long)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), -1e12, 1e12);

    private static int RoundToHundred(double value)
        => (int)(Math.Clamp(Math.Round(value / 100.0, MidpointRounding.AwayFromZero), -1e7, 1e7) * 100);

    // =================================================================================
    // Typed input
    // =================================================================================

    /// <summary>A typed heading, 0-360 (360 is north); <see cref="FcuValueEntry"/>'s ranges and words.</summary>
    public static bool TryHeadingInput(string? input, out int degrees, out string? error)
    {
        degrees = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = FcuValueEntry.InvalidNumber; return false; }
        return FcuValueEntry.TryHeading(value, out degrees, out error);
    }

    /// <summary>A typed altitude, 100-49000, rounded to the nearest hundred feet.</summary>
    public static bool TryAltitudeInput(string? input, out int feet, out string? error)
    {
        feet = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = FcuValueEntry.InvalidNumber; return false; }
        if (!FcuValueEntry.TryAltitude(value, out double ft, out error)) return false;
        feet = RoundToHundred(ft);
        return true;
    }

    /// <summary>A typed vertical speed, minus 8000 to 6000 feet per minute, rounded to the nearest hundred.</summary>
    public static bool TryVerticalSpeedInput(string? input, out int fpm, out string? error)
    {
        fpm = 0;
        if (!NumberParsing.TryParseTyped(input, out double value)) { error = FcuValueEntry.InvalidNumber; return false; }
        if (value < -8000 || value > 6000) { error = VsRangeError; return false; }
        fpm = RoundToHundred(value);
        error = null;
        return true;
    }

    // =================================================================================
    // Phrases
    // =================================================================================

    /// <summary>"Heading 090" from the heading target; three digits, 360 reads as 000. Null or not finite: "Heading unavailable".</summary>
    public static string HeadingPhrase(double? value)
    {
        if (!IsReadable(value)) return "Heading unavailable";
        long deg = ((Round(value!.Value) % 360) + 360) % 360;
        return $"Heading {deg.ToString("D3", CultureInfo.InvariantCulture)}";
    }

    /// <summary>"Speed 250" from the IAS target. Outside 0 to <see cref="SpeedReadMax"/> knots is not a speed: "Speed unavailable".</summary>
    public static string SpeedPhrase(double? value)
        => SpeedReadable(value) ? $"Speed {Plain(Round(value!.Value))}" : "Speed unavailable";

    /// <summary>"Altitude 12000" from the altitude target: a plain integer, no thousands separator.</summary>
    public static string AltitudePhrase(double? value)
        => IsReadable(value) ? $"Altitude {Plain(Round(value!.Value))}" : "Altitude unavailable";

    /// <summary>
    /// "Vertical speed minus 1500", "Vertical speed 1500", "Vertical speed 0" (never "minus 0").
    /// Outside <see cref="VsReadMin"/> to <see cref="VsReadMax"/> is not a vertical speed: "unavailable".
    /// </summary>
    public static string VsPhrase(double? value)
    {
        if (!VsReadable(value)) return "Vertical speed unavailable";
        long fpm = Round(value!.Value);
        return fpm < 0 ? $"Vertical speed minus {Plain(-fpm)}" : $"Vertical speed {Plain(fpm)}";
    }

    private static bool IsReadable(double? value) => value is { } v && double.IsFinite(v);

    /// <summary>
    /// A speed target read that can be believed: the step counts of a set rest on it, so a wild one
    /// (an uninitialized variable, a unit mix-up) must send nothing rather than a flood of K-events.
    /// </summary>
    private static bool SpeedReadable(double? value) => value is { } v && double.IsFinite(v) && v >= 0 && v <= SpeedReadMax;

    private static bool VsReadable(double? value) => value is { } v && double.IsFinite(v) && v >= VsReadMin && v <= VsReadMax;

    /// <summary>Two reads of the same target that agree to within half a knot (or half a step): one value, still.</summary>
    private static bool SameReading(double? a, double? b)
        => a is null || b is null ? a is null && b is null : Math.Abs(a.Value - b.Value) < 0.5;

    private static string Plain(long value) => value.ToString(CultureInfo.InvariantCulture);

    // =================================================================================
    // Set loops
    // =================================================================================

    /// <summary>Writes the heading target, waits, and speaks what the heading target then reads.</summary>
    public static async Task<string> SetHeadingAsync(McpIo io, int degrees, CancellationToken ct)
    {
        io.Write(HeadingWrite(degrees));
        await io.Delay(ReadBackMs, ct);
        return HeadingPhrase(await io.Read(HeadingVar, ct));
    }

    /// <summary>Writes the altitude target, waits, and speaks what the altitude target then reads.</summary>
    public static async Task<string> SetAltitudeAsync(McpIo io, int feet, CancellationToken ct)
    {
        io.Write(AltitudeWrite(feet));
        await io.Delay(ReadBackMs, ct);
        return AltitudePhrase(await io.Read(AltitudeVar, ct));
    }

    /// <summary>
    /// Sets the IAS target in BlueBird's knob tiers, closing the loop on what it reads: read the
    /// target fresh, send the planned burst (<see cref="SpeedBursts"/>' <c>Bulk</c>), wait
    /// <see cref="SpeedBurstGapMs"/> and send its <c>Rest</c> as a burst of its own, wait
    /// <see cref="StepSettleMs"/>, read until the value has SETTLED (<see cref="ReadSettledAsync"/>), and
    /// judge the round only on that value. While the remaining difference is non-zero and SMALLER in
    /// magnitude than before, correct it, up to <see cref="MaxSpeedCorrections"/> times, in bursts of at
    /// most <see cref="LinearBurstMax"/> events, each followed by <see cref="SpeedBurstGapMs"/>, so every
    /// event moves exactly 1 kt. A shrinking remainder counts as progress even when its sign flipped
    /// (an overshoot brought nearer); one that did not shrink is not chased (the aircraft is not taking
    /// the steps, the variable is being overwritten, or the burst overshot by more than it undershot).
    /// Speaks the value read last, never the request. In Mach mode nothing is sent: the caller changes
    /// the window to knots first (<see cref="Bbs757Mach.EnsureUnitsAsync"/>), so Mach here means the
    /// changeover did not hold (<see cref="Bbs757Mach.ChangeoverRefusal"/>); the IAS target is then the
    /// Mach's equivalent and steps would move the Mach. A target outside 0 to <see cref="SpeedReadMax"/>
    /// knots is unreadable ("Speed unavailable") and sends nothing. Throws
    /// <see cref="OperationCanceledException"/> when canceled.
    /// </summary>
    public static async Task<string> SetSpeedAsync(McpIo io, int knots, CancellationToken ct)
    {
        double? mach = await io.Read(MachModeVar, ct);
        if (mach is > 0.5) return Bbs757Mach.ChangeoverRefusal;

        double? current = await io.Read(SpeedVar, ct);
        if (!SpeedReadable(current)) return SpeedPhrase(null);

        long diff = knots - Round(current!.Value);
        long remaining = Math.Abs(diff);
        if (remaining == 0) return SpeedPhrase(current);

        // A newer set may have superseded this one while the read was in flight: its burst must not go
        // out after the newer set's spacing check, or the two would merge into one accelerated burst.
        ct.ThrowIfCancellationRequested();

        // A readable target is 0 to 500 kt, so the change fits an int.
        var (bulk, rest) = SpeedBursts((int)remaining);
        if (bulk > 0) io.Write(SpeedBurstCode(diff > 0, bulk));
        if (rest > 0)
        {
            await io.Delay(SpeedBurstGapMs, ct);
            io.Write(SpeedBurstCode(diff > 0, rest));
        }

        double? now = current;
        for (int corrections = 0; ; corrections++)
        {
            await io.Delay(StepSettleMs, ct);
            now = await ReadSettledAsync(io, SpeedVar, ct);
            if (!SpeedReadable(now)) break;

            long left = knots - Round(now!.Value);
            if (left == 0 || Math.Abs(left) >= remaining || corrections == MaxSpeedCorrections) break;

            remaining = Math.Abs(left);
            await SendLinearBurstsAsync(io, left > 0, remaining, ct);
        }
        return SpeedPhrase(now);
    }

    /// <summary>
    /// A correction: <paramref name="knots"/> sent in bursts of at most <see cref="LinearBurstMax"/>
    /// events, each followed by <see cref="SpeedBurstGapMs"/>, so no burst reaches BlueBird's 2 kt
    /// tier and every event moves the target exactly 1 kt.
    /// </summary>
    internal static async Task SendLinearBurstsAsync(McpIo io, bool up, long knots, CancellationToken ct)
    {
        for (long left = knots; left > 0; left -= LinearBurstMax)
        {
            io.Write(SpeedBurstCode(up, (int)Math.Min(left, LinearBurstMax)));
            await io.Delay(SpeedBurstGapMs, ct);
        }
    }

    /// <summary>
    /// Reads <paramref name="key"/> until two reads <see cref="SettlePollMs"/> apart agree, and returns
    /// that value; after <see cref="SettleGiveUpMs"/> without agreement returns the last read. A K-event
    /// burst can land in more than one part (the aircraft counts them over several frames), and a read
    /// between the parts is not a place to judge from. The radio minimums set reads the same way
    /// (<see cref="Bbs757Altimeters.SetDecisionHeightAsync"/>).
    /// </summary>
    internal static async Task<double?> ReadSettledAsync(McpIo io, string key, CancellationToken ct)
    {
        double? previous = await io.Read(key, ct);
        for (int waited = 0; waited < SettleGiveUpMs; waited += SettlePollMs)
        {
            await io.Delay(SettlePollMs, ct);
            double? next = await io.Read(key, ct);
            if (SameReading(previous, next)) return next;
            previous = next;
        }
        return previous;
    }

    /// <summary>
    /// Writes the V/S target, waits, reads it, and when it is not what was asked steps the
    /// difference in hundreds (the write is unmeasured on this variable; at most
    /// <see cref="MaxVsStepsPerRound"/> events), waits and reads again. Speaks what it reads last. A
    /// read outside <see cref="VsReadMin"/> to <see cref="VsReadMax"/> is unreadable: nothing is
    /// stepped from it and the pilot hears "Vertical speed unavailable".
    /// </summary>
    public static async Task<string> SetVerticalSpeedAsync(McpIo io, int fpm, CancellationToken ct)
    {
        io.Write(VsWrite(fpm));
        await io.Delay(ReadBackMs, ct);
        double? now = await io.Read(VsVar, ct);

        if (VsReadable(now))
        {
            var steps = VsSteps(now!.Value, fpm);
            if (steps.Count > 0)
            {
                foreach (var call in steps) io.Write(call);
                await io.Delay(StepSettleMs, ct);
                now = await io.Read(VsVar, ct);
            }
        }
        return VsPhrase(now);
    }

    // =================================================================================
    // Readouts
    // =================================================================================

    /// <summary>The MCP button lights Shift+H names after the heading, in panel order (labels of <see cref="Bbs757McpLights"/>).</summary>
    public static IReadOnlyList<string> HeadingModeLights { get; } = new[] { "LNAV", "HDG HOLD", "BCRS 1", "LOC", "APP" };

    /// <summary>The lights Shift+S names after the speed.</summary>
    public static IReadOnlyList<string> SpeedModeLights { get; } = new[] { "THR", "SPD", "VNAV", "FLCH" };

    /// <summary>The lights Shift+A names after the altitude.</summary>
    public static IReadOnlyList<string> AltitudeModeLights { get; } = new[] { "VNAV", "FLCH", "V/S", "ALT HOLD" };

    /// <summary>The light Shift+V names after the vertical speed.</summary>
    public static IReadOnlyList<string> VsModeLights { get; } = new[] { "V/S" };

    /// <summary>
    /// "Heading 090, LNAV": the phrase, then each lit button of <paramref name="lit"/> in order; the phrase
    /// alone when none is lit. These are the MCP's button lights, not the flight mode annunciator (which
    /// the FMA reader reads from the aircraft's memory), so an armed mode whose button is lit is named too.
    /// </summary>
    public static string WithModes(string phrase, IReadOnlyList<string> lit)
        => lit.Count == 0 ? phrase : $"{phrase}, {string.Join(", ", lit)}";

    /// <summary>
    /// Which of <paramref name="labels"/>' buttons are lit, in that order. The lights are batched, so each
    /// comes from the cache when there is one, else a fresh read; an unread light counts as not lit.
    /// </summary>
    public static async Task<IReadOnlyList<string>> LitModesAsync(McpIo io, IReadOnlyList<string> labels, CancellationToken ct)
    {
        var lit = new List<string>();
        foreach (string label in labels)
        {
            var light = Bbs757McpLights.All.First(l => l.Label == label);
            double? value = io.Cached?.Invoke(light.Key) ?? await io.Read(light.Key, ct);
            if (value is double v && double.IsFinite(v) && v > 0.5) lit.Add(label);
        }
        return lit;
    }

    public static async Task<string> ReadHeadingAsync(McpIo io, CancellationToken ct)
    {
        double? heading = await io.Read(HeadingVar, ct);
        if (!IsReadable(heading)) return HeadingPhrase(null);
        return WithModes(HeadingPhrase(heading), await LitModesAsync(io, HeadingModeLights, ct));
    }

    public static async Task<string> ReadAltitudeAsync(McpIo io, CancellationToken ct)
    {
        double? altitude = await io.Read(AltitudeVar, ct);
        if (!IsReadable(altitude)) return AltitudePhrase(null);
        return WithModes(AltitudePhrase(altitude), await LitModesAsync(io, AltitudeModeLights, ct));
    }

    public static async Task<string> ReadVerticalSpeedAsync(McpIo io, CancellationToken ct)
    {
        double? vs = await io.Read(VsVar, ct);
        if (!VsReadable(vs)) return VsPhrase(null);
        return WithModes(VsPhrase(vs), await LitModesAsync(io, VsModeLights, ct));
    }

    /// <summary>
    /// Shift+S: what the speed window shows. Read in order, stopping at the first that decides: the
    /// blank flag, then the Mach flag, then the Mach wheels or the IAS target. Never the IAS target
    /// while the window is blank or in Mach: VNAV holds a stale one behind the blank window (250 while
    /// flying 262, measured), and in Mach it is the Mach's IAS equivalent. The blank flag is batched,
    /// so it comes from the cache when there is one: a fresh read of it waited up to a second for the
    /// next batch, the lag Shift+S had over Shift+H and Shift+A (2026-10-27). The rest are read fresh.
    /// The lit speed mode buttons (<see cref="SpeedModeLights"/>) follow the value.
    /// </summary>
    public static async Task<string> ReadSpeedAsync(McpIo io, CancellationToken ct)
    {
        double? blanked = io.Cached?.Invoke(IasBlankedVar) ?? await io.Read(IasBlankedVar, ct);
        if (blanked is >= 0.5) return SpeedReadout(blanked, null, null, null, await LitModesAsync(io, SpeedModeLights, ct));
        double? machFlag = await io.Read(MachModeVar, ct);
        if (machFlag is >= 0.5)
            return SpeedReadout(blanked, machFlag, null,
                MachFromDrums(await io.Read(MachTenthsDrumVar, ct), await io.Read(MachHundredthsDrumVar, ct)),
                await LitModesAsync(io, SpeedModeLights, ct));
        double? knots = await io.Read(SpeedVar, ct);
        if (!SpeedReadable(knots)) return SpeedPhrase(null);
        return SpeedReadout(blanked, machFlag, knots, null, await LitModesAsync(io, SpeedModeLights, ct));
    }

    /// <summary>
    /// The speed window in words: blank ("MCP speed window blank. Speed Intervene opens it."), in Mach
    /// ("MCP Mach 0.78", or <see cref="MachModeReadNote"/> while the Mach is unread), else the IAS target
    /// ("Speed 250"). An unread flag counts as not set, so a window nothing is known about reads as before.
    /// The lit mode buttons in <paramref name="modes"/> follow the value ("Speed 250, SPD"; blank: "MCP
    /// speed window blank, VNAV. Speed Intervene opens it."); never after a value that was not read.
    /// </summary>
    public static string SpeedReadout(double? blanked, double? machFlag, double? knots, double? mach,
        IReadOnlyList<string>? modes = null)
    {
        modes ??= Array.Empty<string>();
        if (blanked is >= 0.5)
            return modes.Count == 0 ? BlankWindowNote : $"{WithModes("MCP speed window blank", modes)}. Speed Intervene opens it.";
        if (machFlag is >= 0.5)
            return mach is double m ? WithModes($"MCP Mach {m.ToString("0.00", CultureInfo.InvariantCulture)}", modes) : MachModeReadNote;
        return SpeedReadable(knots) ? WithModes(SpeedPhrase(knots), modes) : SpeedPhrase(null);
    }

    /// <summary>
    /// The Mach the speed window's wheels show: tenths from the hundreds wheel (10·d), hundredths from
    /// the tens wheel (10·(d+1)); measured at FL290 on 2026-10-26, .40 to .95 ([B757-50]). Null when either is
    /// unread, not a digit (a wheel caught between digits reads between multiples of 10), or below the
    /// .40 floor: the wheels showing knots (100-399 kt) decode as .10-.39, as they do for a second or two
    /// after an IAS/MACH changeover, and must never be taken for a Mach.
    /// </summary>
    public static double? MachFromDrums(double? tenthsWheel, double? hundredthsWheel)
    {
        if (Digit(tenthsWheel, 0) is not int tenths || Digit(hundredthsWheel, 1) is not int hundredths) return null;
        return tenths < 4 ? null : (tenths * 10 + hundredths) / 100.0;

        // A wheel position 10·(d + offset) within 1 of a multiple of 10, for a digit d of 0 to 9.
        static int? Digit(double? wheel, int offset)
        {
            if (wheel is not double w || !double.IsFinite(w)) return null;
            double steps = w / 10.0;
            long nearest = (long)Math.Round(steps, MidpointRounding.AwayFromZero);
            if (Math.Abs(steps - nearest) > 0.1) return null;
            long d = nearest - offset;
            return d is >= 0 and <= 9 ? (int)d : null;
        }
    }
}
