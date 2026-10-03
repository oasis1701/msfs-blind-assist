using System;
using System.Collections.Generic;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The DA40's momentary controls that are being held down right now, one entry per L:var.
///
/// The airframe zeroes these every frame, so a press is a REPEATING write for a fixed time
/// (see <c>CowsDA40Definition.HoldLVar</c>). This is the bookkeeping, kept pure so it can be
/// tested: which vars to re-write on a tick, which have run their time and are released, and
/// whose completion to run.
///
/// ⚠️ ONE HOLD PER VAR, NOT ONE FOR THE WHOLE AEROPLANE. There used to be a single hold, and
/// starting any press released whatever was held — so a trim nudge, a gyro cage or the fuel
/// wire pressed during the 26-second ECU test ended the test early, silently, with no result.
/// A second press of the SAME var still replaces the first (a double press cannot leave a
/// control stuck down), and the replaced press's completion is dropped: it did not complete.
/// </summary>
internal sealed class DA40HoldSet
{
    private sealed class Hold
    {
        public long UntilTicks;
        public Action? OnComplete;
    }

    private readonly Dictionary<string, Hold> _holds = new(StringComparer.Ordinal);

    public bool IsEmpty => _holds.Count == 0;

    public bool IsHeld(string lvar) => _holds.ContainsKey(lvar);

    /// <summary>Holds <paramref name="lvar"/> until <paramref name="untilTicks"/>; true when it
    /// replaced a hold of the same var.</summary>
    public bool Start(string lvar, long untilTicks, Action? onComplete)
    {
        bool replaced = _holds.ContainsKey(lvar);
        _holds[lvar] = new Hold { UntilTicks = untilTicks, OnComplete = onComplete };
        return replaced;
    }

    /// <summary>
    /// One tick: the vars still held (write 1), the vars whose time is up (write 0), and the
    /// completions of those — which run only while <paramref name="connected"/>, because a
    /// press cut off by a disconnect did not complete. A disconnect releases everything.
    /// </summary>
    public (List<string> Write, List<string> Release, List<Action> Complete) Tick(long nowTicks, bool connected)
    {
        var write = new List<string>();
        var release = new List<string>();
        var complete = new List<Action>();

        foreach (var (lvar, hold) in _holds)
        {
            if (!connected || nowTicks >= hold.UntilTicks)
            {
                release.Add(lvar);
                if (connected && hold.OnComplete != null) complete.Add(hold.OnComplete);
            }
            else
            {
                write.Add(lvar);
            }
        }

        foreach (string lvar in release) _holds.Remove(lvar);
        return (write, release, complete);
    }

    /// <summary>Drops one hold without completing it; true when it was held.</summary>
    public bool Release(string lvar) => _holds.Remove(lvar);

    /// <summary>Drops every hold without completing any, returning the vars to write 0.</summary>
    public List<string> ReleaseAll()
    {
        var all = new List<string>(_holds.Keys);
        _holds.Clear();
        return all;
    }
}
