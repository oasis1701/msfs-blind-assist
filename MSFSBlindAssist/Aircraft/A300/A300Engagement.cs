namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>The autopilot's engagement ("CMD 1", "CMD 2", "Dual", or null when off) and whether
/// either autothrottle arm switch is on.</summary>
public readonly record struct A300Engagement(string? Autopilot, bool AutothrottleArmed);

/// <summary>
/// Speaks the autopilot and autothrottle engaging and disconnecting, which the FMA columns do not:
/// "Autopilot disconnected", "Autopilot CMD 1", "Autothrottle disconnected", "Autothrottle armed".
/// The A300's autothrottle disconnect trips its arm switches, so both going off is the disconnect;
/// the thrust column then goes blank and says nothing itself. A change the pilot made through MSFSBA
/// is silent (the caller says which): the screen reader or the hotkey's read-back has already said it.
/// Baseline-first. Pure.
/// </summary>
public sealed class A300EngagementTracker
{
    private A300Engagement? _last;

    public IReadOnlyList<A300FmaCallout> Observe(A300Engagement now, bool autopilotOwnPick, bool autothrottleOwnPick)
    {
        var last = _last;
        _last = now;
        if (last is not { } was)
            return Array.Empty<A300FmaCallout>();
        var callouts = new List<A300FmaCallout>(2);
        if (now.Autopilot != was.Autopilot && !autopilotOwnPick)
            callouts.Add(new A300FmaCallout(A300FmaColumn.Autopilot,
                now.Autopilot == null ? "Autopilot disconnected" : $"Autopilot {now.Autopilot}"));
        if (now.AutothrottleArmed != was.AutothrottleArmed && !autothrottleOwnPick)
            callouts.Add(new A300FmaCallout(A300FmaColumn.Autothrottle,
                now.AutothrottleArmed ? "Autothrottle armed" : "Autothrottle disconnected"));
        return callouts;
    }

    public void Reset() => _last = null;
}
