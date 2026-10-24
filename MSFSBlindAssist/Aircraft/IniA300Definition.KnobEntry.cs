using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The typed values that turn a knob instead of being written ([A300-30]): the flex temperature, which the FMS rewrites
/// on every update, and the landing elevation. The knob is turned the way a click turns it, through its own B: event
/// (<see cref="A300SteppedKnobs"/>), and read back until it reaches the value or stops moving.
/// </summary>
public partial class IniA300Definition
{
    /// <summary>The gap between two sends of a round. A judgment: only how many clicks one send may carry was
    /// measured.</summary>
    public const int KnobSendSpacingMs = 100;

    /// <summary>How long a round waits after its last send before the knob is read back. A judgment.</summary>
    public const int KnobSettleMs = 400;

    /// <summary>The most rounds of clicks one entry sends; the read after the last says where the knob stopped.</summary>
    public const int KnobRounds = 4;

    /// <summary>Each knob's latest entry, by its typed key: an older entry stops at its next wait or read, with nothing
    /// more sent or said. Everything runs on the UI thread.</summary>
    private readonly Dictionary<string, int> _latestKnobEntry = new(StringComparer.Ordinal);
    private int _knobEntrySeq;

    /// <summary>
    /// A typed flex temperature or landing elevation: refused aloud with its range
    /// (<see cref="A300TypedValues.KnobTarget"/>), or the knob turned to it. A round reads the knob fresh, then: within
    /// half a click of the value, it is confirmed (<see cref="A300SteppedKnob.Phrase"/>, a numeric confirmation,
    /// [CORE-7]); not moved since the last round (unpowered, at a limit), or after <see cref="KnobRounds"/> rounds of
    /// clicks, it says where the knob stopped; otherwise the clicks to the value (never more than the knob's whole travel,
    /// whatever the read says: <see cref="A300SteppedKnobs.ClicksFor"/>) go out one string at a time,
    /// <see cref="KnobSendSpacingMs"/> apart, and after <see cref="KnobSettleMs"/> the next round reads it again. A read
    /// that does not answer says the box is unavailable. A newer entry for the same knob, or the definition going away,
    /// ends a run with nothing more sent or said; a refused entry starts nothing and ends nothing. The awaits resume on
    /// the UI thread, where every send stays ([SIM-20]).
    /// </summary>
    private async Task SetByKnobAsync(string key, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        string name)
    {
        var (target, error) = A300TypedValues.KnobTarget(key, value);
        if (target is not double goal)
        {
            announcer.Announce($"{name}: {error}");
            return;
        }
        var knob = A300SteppedKnobs.For(key)!;
        // The knob is no panel row (it is left out, [A300-3]), so its control comes from the map.
        if (!CanLand(sim) || _map.FindByKey(knob.ControlKey) is not { } control)
        {
            announcer.Announce($"{name} unavailable");
            return;
        }
        int entry = ++_knobEntrySeq;
        _latestKnobEntry[key] = entry;
        bool Ended() => _disposed || _latestKnobEntry.GetValueOrDefault(key) != entry;
        try
        {
            double? last = null;
            for (int round = 1; ; round++)
            {
                double? read = await ReadFresh(sim, knob.ReadoutKey, ReadoutTimeoutMs);
                // An aircraft switch disposes this definition before the pending reads are failed, and a delivery can
                // land in between: that value belongs to the old aircraft.
                if (Ended())
                    return;
                if (read is not double now || !double.IsFinite(now))
                {
                    announcer.Announce($"{name} unavailable");
                    return;
                }
                if (Math.Abs(now - goal) <= knob.Step / 2)
                {
                    announcer.Announce(knob.Phrase(now));
                    return;
                }
                bool unmoved = last is double before && Math.Abs(now - before) < A300WritePlan.SameValueTolerance;
                if (unmoved || round > KnobRounds)
                {
                    announcer.Announce(knob.StoppedPhrase(now));
                    return;
                }
                last = now;
                int clicks = A300SteppedKnobs.ClicksFor(now, goal, knob.Step, knob.MaxClicks);
                var sends = A300SteppedKnobs.Sends(control, clicks, knob.ClicksPerSend);
                for (int i = 0; i < sends.Count; i++)
                {
                    if (i > 0)
                    {
                        await TypedDelay(KnobSendSpacingMs);
                        if (Ended())
                            return;
                    }
                    Send(sim, sends[i]);
                }
                await TypedDelay(KnobSettleMs);
                if (Ended())
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"{name} failed: {ex.Message}");
        }
    }
}
