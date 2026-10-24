using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// One Fenix FCU knob a typed set turns ([FNX-20]): its click counter (stepped from the sim's own value,
/// [FNX-5]), the on-request twin it is read back through ([A320-41]), the value one click moves, whether it
/// wraps at 360, and for the V/S the twin of its dashes flag (v3 sends the LIVE V/S while the window is dashed).
/// </summary>
internal sealed record FenixFcuKnob(string Name, string CounterVar, string ReadoutVar, int Step, bool Wraps,
    string? DashedReadoutVar = null)
{
    internal static readonly FenixFcuKnob Heading =
        new("Heading", "E_FCU_HEADING", "N_FCU_HEADING" + FenixA320Definition.ReadoutSuffix, 1, Wraps: true);
    internal static readonly FenixFcuKnob Speed =
        new("Speed", "E_FCU_SPEED", "N_FCU_SPEED" + FenixA320Definition.ReadoutSuffix, 1, Wraps: false);
    internal static readonly FenixFcuKnob Altitude =
        new("Altitude", "E_FCU_ALTITUDE", "N_FCU_ALTITUDE" + FenixA320Definition.ReadoutSuffix, 100, Wraps: false);
    internal static readonly FenixFcuKnob VerticalSpeed =
        new("Vertical speed", "E_FCU_VS", "N_FCU_VS" + FenixA320Definition.ReadoutSuffix, 100, Wraps: false,
            DashedReadoutVar: "B_FCU_VERTICALSPEED_DASHED" + FenixA320Definition.ReadoutSuffix);

    /// <summary>
    /// A value as the pilot hears it, invariant (never "0,78" or a U+2212 minus): a heading in three digits as
    /// the Shift+H readout says it, a speed below 100 as Mach (the speed window keeps Mach times 100), a V/S
    /// signed as its own announcement ("+300"), an altitude plain.
    /// </summary>
    internal string Format(double value)
    {
        double v = Math.Round(value);
        if (this == Heading) return v.ToString("000", CultureInfo.InvariantCulture);
        if (this == Speed && v < 100) return (v / 100).ToString("0.00", CultureInfo.InvariantCulture);
        if (this == VerticalSpeed) return v.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        return v.ToString("0", CultureInfo.InvariantCulture);
    }
}

/// <summary>One read of a knob: its value (null unread) and, for the V/S, whether its window is dashed.</summary>
internal readonly record struct FenixFcuReading(double? Value, bool Dashed = false);

internal enum FenixFcuSetEnd
{
    /// <summary>The knob reads the target.</summary>
    Landed,
    /// <summary>The set stopped elsewhere: the knob did not move, made no progress, or ran out of writes or time.</summary>
    Missed,
    /// <summary>The V/S window was still dashed after a write, so its read-back is the live V/S, not the knob.</summary>
    StillDashed,
    /// <summary>A read gave nothing (a time-out or a disconnect): no write follows an unread knob.</summary>
    Unread,
    /// <summary>A newer set of the same knob, an aircraft switch or a disconnect took over: nothing is spoken.</summary>
    Superseded,
}

/// <summary>How a set ended: the last value read, the writes sent and the time it took.</summary>
internal readonly record struct FenixFcuSetResult(FenixFcuSetEnd End, double? Value, int Writes, long ElapsedMs);

/// <summary>What the loop does to the aircraft, injected so the tests drive it with a fake knob and clock.</summary>
internal sealed class FenixFcuKnobIo
{
    /// <summary>Steps the knob's counter by n clicks from the sim's own value (<c>JumpCounter</c>).</summary>
    public required Action<int> Write { get; init; }
    /// <summary>Reads the knob fresh (its on-request twin), never the cache.</summary>
    public required Func<Task<FenixFcuReading>> Read { get; init; }
    public required Func<int, Task> Delay { get; init; }
    public required Func<long> NowMs { get; init; }
    /// <summary>False once a newer set of the knob, an aircraft switch or a disconnect has taken over.</summary>
    public required Func<bool> StillWanted { get; init; }
}

/// <summary>
/// A typed Fenix FCU set as a closed loop ([FNX-20]). With the Fenix's Encoder Acceleration option on, a large
/// or quick knob turn is multiplied (measured 2026-10-24, 3.0.0.7454: one -80 write took the altitude from
/// 15000 to the 100 ft floor, one +40 write to the 49000 ceiling, 10-count writes 46 ms apart ran it from 100
/// to 30600; single writes of 20 and 5, and five +1 writes about 150 ms apart, landed exactly). So with
/// acceleration on or unread, each write carries at most <see cref="AcceleratedChunkLimit"/> counts, the knob
/// is read back after each write, and the next write is worked out from what it READS, never from a count kept
/// here. With acceleration off, one write carries the whole difference, read back the same way. A knob that has
/// not moved is never written again (the write may still be on its way); a write seen multiplied makes the
/// chunks smaller and the waits longer; the number of writes and the time are bounded.
/// </summary>
internal static class FenixA320FcuKnobSet
{
    /// <summary>The most counts one write carries with acceleration on or unread: 20 landed exactly, 40 and 80
    /// were multiplied (2026-10-24).</summary>
    internal const int AcceleratedChunkLimit = 20;
    /// <summary>The wait after a write before the knob is read, with acceleration on: well past the 46 ms
    /// spacing that was multiplied and the 150 ms that was not. Doubled when a write is seen multiplied.</summary>
    internal const int AcceleratedSettleMs = 500;
    internal const int MaxSettleMs = 2000;
    /// <summary>The wait after a write with acceleration off, before the first read; a knob not yet moved by
    /// then is polled, never written again.</summary>
    internal const int PlainSettleMs = 250;
    /// <summary>How long after a write the knob may stay put before the set gives up on it.</summary>
    internal const int NoMoveWaitMs = 1500;
    internal const int PollMs = 250;
    /// <summary>Reads, <see cref="PollMs"/> apart, for a value that moved other than written to settle.</summary>
    internal const int MaxSettleReads = 4;
    /// <summary>Writes in a row that bring the knob no closer before the set stops.</summary>
    internal const int StallWrites = 4;
    internal const int MaxWrites = 50;
    internal const int MaxSetMs = 30000;
    /// <summary>The time-out of one fresh read of a twin, which answers within a frame or two.</summary>
    internal const int ReadTimeoutMs = 1000;

    /// <summary>The shortest heading change, -180 to +180: 340 to 20 is +40, 20 to 340 is -40.</summary>
    internal static int HeadingDelta(int currentHeading, int targetHeading)
    {
        int delta = targetHeading - currentHeading;
        if (delta > 180) delta -= 360;
        else if (delta < -180) delta += 360;
        return delta;
    }

    /// <summary>Whole clicks from <paramref name="current"/> to <paramref name="target"/>, the short way round
    /// for a heading.</summary>
    internal static int CountsToGo(FenixFcuKnob knob, double current, double target) => knob.Wraps
        ? HeadingDelta((int)Math.Round(current), (int)Math.Round(target))
        : (int)Math.Round((target - current) / knob.Step, MidpointRounding.AwayFromZero);

    /// <summary>The next write: the counts to go, never more than <paramref name="chunkLimit"/> either way.</summary>
    internal static int NextWrite(int countsToGo, int chunkLimit) => Math.Clamp(countsToGo, -chunkLimit, chunkLimit);

    /// <summary>
    /// Turns <paramref name="knob"/> to <paramref name="target"/> (on the knob's grid). <paramref name="encoderAcceleration"/>
    /// null (unread) counts as on. <paramref name="lastWriteMs"/> is the knob's last write by any set: a write that
    /// recent is waited out before the first read, so the read cannot predate it and the first write cannot fall
    /// inside the acceleration window. Deliberately no ConfigureAwait(false): called on the UI thread, the writes
    /// stay there.
    /// </summary>
    internal static async Task<FenixFcuSetResult> RunAsync(FenixFcuKnob knob, double target, bool? encoderAcceleration,
        long? lastWriteMs, FenixFcuKnobIo io)
    {
        long start = io.NowMs();
        bool plain = encoderAcceleration == false;
        int chunkLimit = plain ? int.MaxValue : AcceleratedChunkLimit;
        int settleMs = plain ? PlainSettleMs : AcceleratedSettleMs;
        int writes = 0;
        FenixFcuSetResult End(FenixFcuSetEnd end, double? value) => new(end, value, writes, io.NowMs() - start);

        if (lastWriteMs is long last && last + AcceleratedSettleMs - io.NowMs() is var gap and > 0)
            await io.Delay((int)gap);
        if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, null);

        FenixFcuReading reading = await io.Read();
        if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, reading.Value);
        if (reading.Value is null) return End(FenixFcuSetEnd.Unread, null);

        int best = int.MaxValue, sinceBest = 0;
        while (true)
        {
            double current = reading.Value!.Value;
            // A dashed V/S reads the live V/S: fine to aim the first write from, but after a write it means the
            // knob did not give the window a selected value.
            if (reading.Dashed && writes > 0) return End(FenixFcuSetEnd.StillDashed, current);
            int togo = CountsToGo(knob, current, target);
            if (togo == 0) return End(reading.Dashed ? FenixFcuSetEnd.StillDashed : FenixFcuSetEnd.Landed, current);
            if (writes >= MaxWrites || io.NowMs() - start >= MaxSetMs) return End(FenixFcuSetEnd.Missed, current);
            if (Math.Abs(togo) < best) { best = Math.Abs(togo); sinceBest = 0; }
            else if (++sinceBest >= StallWrites) return End(FenixFcuSetEnd.Missed, current);

            int write = NextWrite(togo, chunkLimit);
            io.Write(write);
            writes++;
            FenixFcuReading before = reading;

            await io.Delay(settleMs);
            if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, before.Value);
            reading = await io.Read();
            if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, reading.Value);
            if (reading.Value is null) return End(FenixFcuSetEnd.Unread, before.Value);
            if (before.Dashed || reading.Dashed) continue;   // nothing to judge against a live V/S

            // Never write again on a knob that has not moved: the write may still be on its way.
            for (int waited = settleMs; SameValue(reading, before) && waited < NoMoveWaitMs; waited += PollMs)
            {
                await io.Delay(PollMs);
                if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, reading.Value);
                FenixFcuReading next = await io.Read();
                if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, next.Value);
                if (next.Value is null) return End(FenixFcuSetEnd.Unread, reading.Value);
                reading = next;
                if (reading.Dashed) break;
            }
            if (reading.Dashed) continue;
            if (SameValue(reading, before)) return End(FenixFcuSetEnd.Missed, reading.Value);

            int moved = CountsToGo(knob, before.Value!.Value, reading.Value!.Value);
            if (moved == write) continue;

            // It moved other than written (multiplied, clamped at a stop, or still moving): judge only once two
            // reads agree, so the next write is worked out from where the knob came to rest.
            for (int i = 0; i < MaxSettleReads; i++)
            {
                await io.Delay(PollMs);
                if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, reading.Value);
                FenixFcuReading next = await io.Read();
                if (!io.StillWanted()) return End(FenixFcuSetEnd.Superseded, next.Value);
                if (next.Value is null) return End(FenixFcuSetEnd.Unread, reading.Value);
                bool settled = SameValue(next, reading) && next.Dashed == reading.Dashed;
                reading = next;
                if (settled) break;
            }
            if (reading.Dashed) continue;

            moved = CountsToGo(knob, before.Value!.Value, reading.Value!.Value);
            if (Multiplied(moved, write))
            {
                // Acceleration after all (a stale "off", or a window longer than the settle): smaller and slower.
                if (chunkLimit > AcceleratedChunkLimit)
                {
                    chunkLimit = AcceleratedChunkLimit;
                    settleMs = AcceleratedSettleMs;
                }
                else
                {
                    chunkLimit = Math.Max(1, chunkLimit / 2);
                    settleMs = Math.Min(settleMs * 2, MaxSettleMs);
                }
            }
        }
    }

    private static bool SameValue(FenixFcuReading a, FenixFcuReading b)
        => a.Value is double x && b.Value is double y && Math.Abs(x - y) < 0.5;

    private static bool Multiplied(int moved, int written)
        => moved != 0 && (Math.Sign(moved) != Math.Sign(written) || Math.Abs(moved) > Math.Abs(written));

    /// <summary>
    /// What a set says when it ends, or null for none: a landed set is silent ([CORE-7]: the screen reader read
    /// what was typed, and the FCU value's own announcement speaks the change), and so is a superseded one. A
    /// miss is said once, in the pilot's terms: "Altitude set to 7100, not 7000."
    /// </summary>
    internal static string? Spoken(FenixFcuKnob knob, FenixFcuSetResult result, double target) => result.End switch
    {
        FenixFcuSetEnd.Landed or FenixFcuSetEnd.Superseded => null,
        FenixFcuSetEnd.StillDashed => $"{knob.Name} still dashed, not {knob.Format(target)}.",
        FenixFcuSetEnd.Missed when result.Value is double value => $"{knob.Name} set to {knob.Format(value)}, not {knob.Format(target)}.",
        _ when result.Writes == 0 => $"{knob.Name} not set: no read-back.",
        _ => $"{knob.Name} not confirmed.",
    };

    /// <summary>
    /// The sets in progress, one turn per knob: a newer set of a knob ends the older one before its next write,
    /// sets of other knobs carry on, and <see cref="Close"/> (the aircraft switch) ends them all. Also keeps each
    /// knob's last write time, which the next set waits out. Locked, though the sets run on the UI thread.
    /// </summary>
    internal sealed class Turns
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _latest = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastWriteMs = new(StringComparer.Ordinal);
        private bool _closed;

        internal int Begin(string counterVar)
        {
            lock (_gate) return _latest[counterVar] = _latest.GetValueOrDefault(counterVar) + 1;
        }

        internal bool IsLatest(string counterVar, int turn)
        {
            lock (_gate) return !_closed && _latest.GetValueOrDefault(counterVar) == turn;
        }

        internal void Wrote(string counterVar, long nowMs)
        {
            lock (_gate) _lastWriteMs[counterVar] = nowMs;
        }

        internal long? LastWriteMs(string counterVar)
        {
            lock (_gate) return _lastWriteMs.TryGetValue(counterVar, out long at) ? at : null;
        }

        internal void Close()
        {
            lock (_gate) _closed = true;
        }
    }
}
