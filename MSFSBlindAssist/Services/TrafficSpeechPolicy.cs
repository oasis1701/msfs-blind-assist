namespace MSFSBlindAssist.Services;

/// <summary>What a ground-traffic callout is, for deciding how it may be spoken.</summary>
public enum TrafficCalloutKind
{
    Warning,        // "Stop, … very close"                       — interrupts
    RunwayCritical, // new runway occupant / short final / landing while ON the runway — interrupts
    Caution,        // "Slow down, …"                              — interrupts
    Converging,     // "… converging from the left, about 20 seconds."
    OnRoute,        // "… on your route, taxiway B, 800 feet ahead, …"
    Awareness,      // "Delta A320, ahead and to the left, 500 feet, stopped."
    QueueMoving,    // "Delta A320 ahead is moving."
    MoveUp,         // "Move up. …"
    RunwayInfo,     // runway status / changes at a hold
    QueuePosition,  // "Number 3 in the departure queue."
}

/// <summary>A candidate line; <see cref="OnEmitted"/> commits its one-shot state and runs ONLY if it is spoken.</summary>
public sealed record TrafficCallout(TrafficCalloutKind Kind, double DistFt, string Message, Action OnEmitted);

/// <summary>What one evaluation speaks: at most one interrupt, at most one alert line, and every informational line.</summary>
public sealed record SpeechPlan(TrafficCallout? Interrupt, TrafficCallout? Alert, IReadOnlyList<TrafficCallout> Info)
{
    public static readonly SpeechPlan Empty = new(null, null, Array.Empty<TrafficCallout>());
}

/// <summary>
/// Who may interrupt, and how much goes out per evaluation (PR #247 review R7/L4/L11; owner decision
/// 2026-09-23: only safety callouts interrupt).
///
/// <para>An interrupt (<c>AnnounceImmediate</c>) cancels whatever is being spoken AND everything
/// queued behind it, and evaluations run about once a second, so "one interrupt per evaluation" still
/// let the next second's ping cut a "Stop" off and wipe one-shot lines queued before it. Here only
/// <see cref="TrafficCalloutKind.Warning"/>, <see cref="TrafficCalloutKind.RunwayCritical"/> and
/// <see cref="TrafficCalloutKind.Caution"/> interrupt; everything else is queued (<c>Announce</c>).
/// Alert lines are rate-limited to one per <see cref="AlertLineSpacingMs"/> (a rate limit, not an
/// estimate of how long speech takes); informational lines always go out. While the announcer is
/// suppressed (aircraft-switch grace) nothing but the interrupt is planned, so nothing queued is lost
/// behind a latch (L4). Planning never marks anything spoken — the caller runs
/// <see cref="TrafficCallout.OnEmitted"/> for what it actually speaks.</para>
/// </summary>
public static class TrafficSpeechPolicy
{
    public const int AlertLineSpacingMs = 3000;

    /// <summary>
    /// An interrupt of EQUAL OR LESSER urgency is withheld for this long after a more urgent one, so a
    /// second "Stop" for another aircraft cannot cut off the "Stop" spoken for the first a second earlier
    /// (PR #247 integration follow-up R3 — with the faster polling this task's own timing brought, a
    /// same-kind "Stop" routinely followed within a second, so equal urgency had to join lesser urgency
    /// here). Only a STRICTLY MORE urgent interrupt may still cut in. A withheld interrupt is not simply
    /// dropped: it is handled like the other alerts (queued, subject to <see cref="AlertLineSpacingMs"/>)
    /// — see <see cref="Plan"/>. It stays unmarked either way and is re-evaluated next sweep.
    /// </summary>
    public const int InterruptProtectMs = 3000;

    public static bool Interrupts(TrafficCalloutKind kind)
        => kind is TrafficCalloutKind.Warning or TrafficCalloutKind.RunwayCritical or TrafficCalloutKind.Caution;

    public static bool IsInfo(TrafficCalloutKind kind)
        => kind is TrafficCalloutKind.RunwayInfo or TrafficCalloutKind.QueuePosition;

    public static SpeechPlan Plan(IReadOnlyList<TrafficCallout> candidates, DateTime nowUtc,
        DateTime lastAlertLineUtc, bool announcerSuppressed,
        TrafficCalloutKind? lastInterruptKind = null, DateTime lastInterruptUtc = default)
    {
        if (candidates.Count == 0) return SpeechPlan.Empty;

        var topInterrupt = candidates.Where(c => Interrupts(c.Kind))
            .OrderByDescending(c => Rank(c.Kind)).ThenBy(c => c.DistFt).FirstOrDefault();
        var interrupt = topInterrupt;
        // Equal or lesser urgency within the protect window does not interrupt (R3: was "lesser" only) —
        // it falls through to the alert slot below instead of being dropped outright.
        bool interruptWithheld = interrupt != null && lastInterruptKind is { } last
            && (nowUtc - lastInterruptUtc).TotalMilliseconds < InterruptProtectMs
            && Rank(interrupt.Kind) <= Rank(last);
        if (interruptWithheld) interrupt = null;
        if (announcerSuppressed)
            return new SpeechPlan(interrupt, null, Array.Empty<TrafficCallout>());

        TrafficCallout? alert = null;
        if ((nowUtc - lastAlertLineUtc).TotalMilliseconds >= AlertLineSpacingMs)
        {
            var alertCandidates = candidates.Where(c => !Interrupts(c.Kind) && !IsInfo(c.Kind));
            if (interruptWithheld) alertCandidates = alertCandidates.Append(topInterrupt!);
            alert = alertCandidates.OrderByDescending(c => Rank(c.Kind)).ThenBy(c => c.DistFt).FirstOrDefault();
        }

        var info = candidates.Where(c => IsInfo(c.Kind)).ToList();
        return new SpeechPlan(interrupt, alert, info);
    }

    private static int Rank(TrafficCalloutKind kind) => kind switch
    {
        TrafficCalloutKind.Warning => 100,
        TrafficCalloutKind.RunwayCritical => 90,
        TrafficCalloutKind.Caution => 80,
        TrafficCalloutKind.Converging => 40,
        TrafficCalloutKind.OnRoute => 30,
        TrafficCalloutKind.Awareness => 20,
        TrafficCalloutKind.QueueMoving => 10,
        TrafficCalloutKind.MoveUp => 5,
        _ => 0,
    };
}
