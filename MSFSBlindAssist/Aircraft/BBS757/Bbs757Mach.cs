using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>What the pilot typed into the speed window: knots or a Mach number, never both.</summary>
public readonly record struct Bbs757SpeedEntry(int? Knots, double? Mach)
{
    /// <summary>The Mach in hundredths (78 for 0.78), or null for a knots entry.</summary>
    public int? Hundredths => Mach is double m ? (int)Math.Round(m * 100, MidpointRounding.AwayFromZero) : null;
}

/// <summary>
/// Mach entry through the 757's speed window (gap plan 05, Task 3), as measured at FL290 on
/// 2026-10-26 ([B757-51]): one <c>K:AP_SPD_VAR_INC/DEC</c> moves the Mach target 0.01; bursts speed up in BlueBird's knot
/// tiers (1 an event for events 1-10, 2 for 11-25; a burst of 30 gave 3 an event or hit a ceiling, so no
/// burst is planned past 25); the target stops at .40 but runs past the aircraft's .86 to .95, so the app
/// keeps to <see cref="Min"/>-<see cref="Max"/> itself; and the Mach is read only from the window's wheels
/// (<see cref="Bbs757Mcp.MachFromDrums"/>), since <c>L:BBS_TARGET_MACH</c> stays 0. A set in the units the
/// window is not showing presses IAS/MACH first (ruling R59), which does nothing on the ground (session 8).
/// Pure but for the <see cref="McpIo"/> it is given; every number spoken is invariant.
/// </summary>
public static class Bbs757Mach
{
    public const double Min = 0.40, Max = 0.86;

    /// <summary>The one error for anything the speed window will not take (the plan's words).</summary>
    public const string RangeError = "Enter a speed from 100 to 399 knots, or a Mach number from 0.40 to 0.86.";

    /// <summary>Said when the IAS/MACH press did not change the window's units within <see cref="ChangeoverWaitMs"/>.</summary>
    public const string ChangeoverRefusal = "IAS/MACH changeover is not available now";

    /// <summary>The changeover is judged by reading the Mach flag every this many ms ...</summary>
    public const int ChangeoverPollMs = 250;

    /// <summary>... for at most this long after the press.</summary>
    public const int ChangeoverWaitMs = 2000;

    /// <summary>
    /// The Mach is judged from the window's wheels, which show a burst or a changeover 1-2 s after it
    /// (2026-10-26, measured through the MCP tool, whose own round trip is part of that). After a
    /// burst they are read every <see cref="WheelPollMs"/> until they move, for up to <see cref="WheelWaitMs"/> ...
    /// </summary>
    public const int WheelPollMs = 250;

    /// <summary>... then read again <see cref="WheelSettleMs"/> apart until two reads agree (also up to <see cref="WheelWaitMs"/>).</summary>
    public const int WheelSettleMs = 500;

    /// <summary>The longest wait for the wheels to move, or to agree.</summary>
    public const int WheelWaitMs = 3000;

    /// <summary>The last burst event planned in BlueBird's measured tiers; events past 25 were not measured cleanly.</summary>
    private const int MeasuredBurstMax = 25;

    /// <summary>
    /// A typed speed: knots 100-399 ("250"), or a Mach number 0.40-0.86 in hundredths ("0.78", ".78",
    /// "M.78", "m0.78"; a comma decimal reads the same, [CORE-17]). Anything else, a Mach with three
    /// decimals included, gives <see cref="RangeError"/>.
    /// </summary>
    public static bool TryParse(string? text, out Bbs757SpeedEntry entry, out string error)
    {
        entry = default;
        error = RangeError;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string t = text.Trim();
        bool markedMach = t[0] is 'M' or 'm';
        if (markedMach) t = t[1..].Trim();
        if (!NumberParsing.TryParseTyped(t, out double value)) return false;

        if (value < 1.0)
        {
            double hundredths = value * 100;
            long whole = (long)Math.Round(hundredths, MidpointRounding.AwayFromZero);
            if (Math.Abs(hundredths - whole) > 1e-6 || whole < Math.Round(Min * 100) || whole > Math.Round(Max * 100)) return false;
            entry = new Bbs757SpeedEntry(null, whole / 100.0);
        }
        else
        {
            if (markedMach || value != Math.Floor(value) || value < 100 || value > 399) return false;
            entry = new Bbs757SpeedEntry((int)value, null);
        }
        error = "";
        return true;
    }

    /// <summary>
    /// How a Mach change of <paramref name="hundredths"/> (a magnitude) is sent: as the knots are
    /// (<see cref="Bbs757Mcp.SpeedBursts"/>, the same tiers in hundredths), but never a burst past the
    /// 25th event, whose step was not measured cleanly; what the 25 cannot reach goes in <c>Rest</c>.
    /// </summary>
    public static Bbs757SpeedBurstPlan Bursts(int hundredths)
    {
        if (hundredths <= 0) return new Bbs757SpeedBurstPlan(0, 0);
        var plan = Bbs757Mcp.SpeedBursts(hundredths);
        if (plan.Bulk <= MeasuredBurstMax) return plan;
        return new Bbs757SpeedBurstPlan(MeasuredBurstMax, hundredths - Bbs757Mcp.SpeedBurstKnots(MeasuredBurstMax));
    }

    /// <summary>"Mach 0.78" (two decimals, invariant); "Mach unavailable" when unread.</summary>
    public static string Phrase(double? mach)
        => mach is double m && double.IsFinite(m) ? $"Mach {m.ToString("0.00", CultureInfo.InvariantCulture)}" : "Mach unavailable";

    /// <summary>The Mach the window's wheels show, read fresh; null when either wheel is unread or between digits.</summary>
    public static async Task<double?> ReadMachAsync(McpIo io, CancellationToken ct)
        => Bbs757Mcp.MachFromDrums(await io.Read(Bbs757Mcp.MachTenthsDrumVar, ct), await io.Read(Bbs757Mcp.MachHundredthsDrumVar, ct));

    /// <summary>
    /// Makes the window show the units of the entry before a set (ruling R59): when the Mach flag says
    /// the other units, presses IAS/MACH (<paramref name="onPress"/> first, for the own-write echo), then
    /// reads the flag every <see cref="ChangeoverPollMs"/> for up to <see cref="ChangeoverWaitMs"/>.
    /// Returns null when the window shows the wanted units, else what to say: <see cref="ChangeoverRefusal"/>
    /// when the press did not take (on the ground), or "Mach unavailable" for a Mach set with the flag
    /// unread (the changeover is a toggle: pressed blind it could leave the window in knots). An unread
    /// flag never stops a knots set, as before plan 05.
    /// </summary>
    public static async Task<string?> EnsureUnitsAsync(McpIo io, bool wantMach, Bbs757Press changeover, Action onPress,
        CancellationToken ct)
    {
        double? flag = await io.Read(Bbs757Mcp.MachModeVar, ct);
        if (flag is not double f || !double.IsFinite(f)) return wantMach ? Phrase(null) : null;
        if (f >= 0.5 == wantMach) return null;

        ct.ThrowIfCancellationRequested();
        onPress();
        io.Write(changeover.Down);
        if (changeover.Up != null)
        {
            await io.Delay(changeover.HoldMs, CancellationToken.None);   // a press is always let go
            io.Write(changeover.Up);
        }

        for (int waited = 0; waited < ChangeoverWaitMs; waited += ChangeoverPollMs)
        {
            await io.Delay(ChangeoverPollMs, ct);
            if (await io.Read(Bbs757Mcp.MachModeVar, ct) is double now && now >= 0.5 == wantMach) return null;
        }
        return ChangeoverRefusal;
    }

    /// <summary>
    /// Sets the Mach target to <paramref name="target"/> hundredths with the window already in Mach, the
    /// knots set's closed loop in hundredths, judged from the wheels (which lag; review finding 1 and 4):
    /// <list type="number">
    /// <item>Read the wheels until they show a Mach that holds (<see cref="ReadSettledMachAsync"/>): right
    /// after a changeover they still show knots, which never decode as a Mach. None within
    /// <see cref="WheelWaitMs"/>: nothing is sent ("Mach unavailable").</item>
    /// <item>Send <see cref="Bursts"/>' bulk, wait <see cref="Bbs757Mcp.SpeedBurstGapMs"/>, send its rest.</item>
    /// <item>Wait for the wheels to move off the old Mach, then for two agreeing reads.</item>
    /// <item>Correct a shrinking remainder in linear bursts of at most <see cref="Bbs757Mcp.LinearBurstMax"/>,
    /// up to <see cref="Bbs757Mcp.MaxSpeedCorrections"/> times, but never more in the set's direction than
    /// the planned difference in all: a burst that seems short may still be rolling in, and chasing it could
    /// carry the target past what was typed (near .86, past the aircraft's limit, which BlueBird does not
    /// stop). An overshoot is brought back.</item>
    /// </list>
    /// Speaks the Mach read last.
    /// </summary>
    public static async Task<string> SetMachAsync(McpIo io, int target, CancellationToken ct)
    {
        double? current = await ReadSettledMachAsync(io, ct);
        if (current is not double cur) return Phrase(null);

        int diff = target - Hundredths(cur);
        if (diff == 0) return Phrase(current);
        bool up = diff > 0;
        int remaining = Math.Abs(diff);
        int netToward = remaining;   // hundredths sent in the set's direction, less those sent back

        ct.ThrowIfCancellationRequested();
        var (bulk, rest) = Bursts(remaining);
        if (bulk > 0) io.Write(Bbs757Mcp.SpeedBurstCode(up, bulk));
        if (rest > 0)
        {
            await io.Delay(Bbs757Mcp.SpeedBurstGapMs, ct);
            await Bbs757Mcp.SendLinearBurstsAsync(io, up, rest, ct);
        }

        double? now = current;
        for (int corrections = 0; ; corrections++)
        {
            now = await ReadAfterRollAsync(io, now, ct);
            if (now is not double n) break;

            int left = target - Hundredths(n);
            if (left == 0 || Math.Abs(left) >= remaining || corrections == Bbs757Mcp.MaxSpeedCorrections) break;

            bool toward = left > 0 == up;
            int send = toward ? Math.Min(Math.Abs(left), Math.Abs(diff) - netToward) : Math.Abs(left);
            if (send <= 0) break;
            netToward += toward ? send : -send;
            remaining = Math.Abs(left);
            await Bbs757Mcp.SendLinearBurstsAsync(io, left > 0, send, ct);
        }
        return Phrase(now);
    }

    /// <summary>
    /// The wheels after a burst: read every <see cref="WheelPollMs"/> until they show something other than
    /// <paramref name="before"/> (or <see cref="WheelWaitMs"/> passes: the burst did not move them), then
    /// until a Mach holds.
    /// </summary>
    private static async Task<double?> ReadAfterRollAsync(McpIo io, double? before, CancellationToken ct)
    {
        for (int waited = 0; waited < WheelWaitMs; waited += WheelPollMs)
        {
            await io.Delay(WheelPollMs, ct);
            if (await ReadMachAsync(io, ct) != before) break;
        }
        return await ReadSettledMachAsync(io, ct);
    }

    /// <summary>
    /// A Mach the wheels hold: two reads <see cref="WheelSettleMs"/> apart that agree and decode as a Mach.
    /// Null when none holds within <see cref="WheelWaitMs"/> (still rolling, or showing knots).
    /// </summary>
    private static async Task<double?> ReadSettledMachAsync(McpIo io, CancellationToken ct)
    {
        double? previous = await ReadMachAsync(io, ct);
        for (int waited = 0; waited < WheelWaitMs; waited += WheelSettleMs)
        {
            await io.Delay(WheelSettleMs, ct);
            double? next = await ReadMachAsync(io, ct);
            if (next != null && next == previous) return next;
            previous = next;
        }
        return null;
    }

    private static int Hundredths(double mach) => (int)Math.Round(mach * 100, MidpointRounding.AwayFromZero);
}
