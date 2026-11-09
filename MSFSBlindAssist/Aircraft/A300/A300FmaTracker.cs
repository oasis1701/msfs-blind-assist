namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>The FMA column a callout comes from (or the engagement it reports,
/// <see cref="A300EngagementTracker"/>); each has its own Ctrl+M row.</summary>
public enum A300FmaColumn { Thrust, Pitch, Roll, Combined, Armed, Autopilot, Autothrottle }

/// <summary>One thing to say about the FMA.</summary>
public readonly record struct A300FmaCallout(A300FmaColumn Column, string Phrase);

/// <summary>
/// Decides what to say when the FMA changes, the way the A380's mode call-outs do: a column that
/// changes to a new word speaks it ("Pitch mode: Altitude capture"), a combined mode speaks alone
/// ("Land"), a newly armed mode speaks ("Glide slope armed"), and a column going blank or a mode
/// disarming says nothing.
///
/// Baseline-first: the first reading after a reset is silent, and so is the moment the guidance
/// columns appear (the IRS finishing its alignment), because the annunciator showing up is not a
/// mode change. The reading is remembered whether or not the caller speaks a callout, so a muted
/// change is never spoken late. Pure.
/// </summary>
public sealed class A300FmaTracker
{
    private A300FmaReading? _last;

    public IReadOnlyList<A300FmaCallout> Observe(A300FmaReading reading)
    {
        var last = _last;
        _last = reading;
        if (last == null || !last.IsShown || !reading.IsShown)
            return Array.Empty<A300FmaCallout>();

        var callouts = new List<A300FmaCallout>();
        Column(callouts, A300FmaColumn.Thrust, last.Thrust, reading.Thrust, w => $"Thrust mode: {w}");
        Column(callouts, A300FmaColumn.Combined, last.Common, reading.Common, w => w);
        Column(callouts, A300FmaColumn.Pitch, last.Pitch, reading.Pitch, w => $"Pitch mode: {w}");
        Column(callouts, A300FmaColumn.Roll, last.Roll, reading.Roll, w => $"Roll mode: {w}");
        foreach (var armed in reading.Armed)
            if (!last.Armed.Contains(armed))
                callouts.Add(new A300FmaCallout(A300FmaColumn.Armed, $"{armed} armed"));
        return callouts;
    }

    public void Reset() => _last = null;

    private static void Column(List<A300FmaCallout> callouts, A300FmaColumn column, string? was, string? now, Func<string, string> phrase)
    {
        if (now != null && now != was)
            callouts.Add(new A300FmaCallout(column, phrase(now)));
    }
}
