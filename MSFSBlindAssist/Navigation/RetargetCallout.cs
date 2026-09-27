namespace MSFSBlindAssist.Navigation;

/// <summary>Why the rollout moved to another exit.</summary>
public enum RetargetReason
{
    /// <summary>Rolled past the planned exit, or a fall-forward after a route failure.</summary>
    Missed,
    /// <summary>Too fast to make the planned exit's turn (<see cref="RolloutExitGate.IsTooFastToTurn"/>).</summary>
    TooFast,
    /// <summary>The undershoot retarget to an earlier exit.</summary>
    Earlier,
}

/// <summary>
/// The ONE utterance a landing-exit retarget speaks. The caller retires every approach milestone the
/// sentence supersedes first (<see cref="Retire"/>), so no milestone can cut it off — KMEM 36L 2026-09-26:
/// "Missed taxiway M6. Retargeting taxiway M7, 650 feet ahead." was cut off 65 ms later by a stale
/// "Taxiway M7, 900 feet." at 631 ft. Pure — <c>RetargetCalloutTests</c>.
/// </summary>
public static class RetargetCallout
{
    /// <summary>
    /// How long THIS retarget sentence takes to speak, per variant: MEASURED through System.Speech at Rate 0
    /// (what ScreenReaderAnnouncer uses) with trailing silence trimmed, the worst over every spoken distance up
    /// to 3,550 ft (taxiways N12/N14; beyond that no milestone can come due inside the sentence), plus about a
    /// fifth, rounded up to the half second (2026-09-26):
    /// <list type="bullet">
    /// <item>too fast: 10.96 s with "Straighten." and "Slow down." → 13.5; 9.37 / 9.50 with one of them → 11.5;
    /// 7.90 bare → 9.5</item>
    /// <item>missed: 10.77 → 13; 9.18 / 9.31 → 11.5; 7.72 → 9.5</item>
    /// <item>earlier (never "Straighten."): 7.89 with "Slow down." → 9.5; 6.32 bare → 8</item>
    /// </list>
    /// One lead for every sentence - the longest's 13 s - retired milestones a short sentence was never going
    /// to collide with: "Taking earlier exit, taxiway A5, 1000 feet ahead." at 30 kt lost A5's own 500 ft call,
    /// which falls due 11 s later, and the pilot heard nothing more until the turn. Never size these by
    /// estimate — re-measure when the wording changes.
    /// </summary>
    public static double LeadSecondsFor(RetargetReason reason, bool straighten, bool slowDown)
    {
        if (reason == RetargetReason.Earlier) return slowDown ? 9.5 : 8.0;
        if (straighten && slowDown) return reason == RetargetReason.TooFast ? 13.5 : 13.0;
        if (straighten || slowDown) return 11.5;
        return 9.5;
    }

    /// <summary>
    /// The milestones a retarget sentence supersedes (<see cref="TouchdownCallout.RetireExitCallouts"/>), judged
    /// with the lead of the sentence that will actually be spoken. Whether it folds "Slow down." depends on
    /// whether the 500 ft call is retired, which depends on the lead: so the sentence without it is judged
    /// first, and only when that retires the 500 ft call above its slow-down line is it judged again with the
    /// longer lead - which can only retire more, so the 500 ft call stays retired and "Slow down." stays in.
    /// </summary>
    public static ExitCalloutRetirement Retire(
        RetargetReason reason, bool straighten, double distanceAheadFeet, double groundSpeedKts, string? exitType,
        double trigger1500Feet, double trigger900Feet, double trigger500Feet, double turnNowFeet,
        double slowDownAboveKts)
    {
        ExitCalloutRetirement Judge(bool slowDown) => TouchdownCallout.RetireExitCallouts(
            distanceAheadFeet, groundSpeedKts, exitType, LeadSecondsFor(reason, straighten, slowDown),
            trigger1500Feet, trigger900Feet, trigger500Feet, turnNowFeet, slowDownAboveKts);

        var withoutSlowDown = Judge(slowDown: false);
        return withoutSlowDown.SlowDown ? Judge(slowDown: true) : withoutSlowDown;
    }

    public static string Compose(
        RetargetReason reason, string? fromTaxiwayName, string? toTaxiwayName,
        int distanceAheadFeet, bool straighten, bool slowDown)
    {
        string from = TouchdownCallout.ExitNamePhrase(fromTaxiwayName);
        string distance = Services.DistanceFormatter.FromFeet(distanceAheadFeet);
        string slow = slowDown ? " Slow down." : "";

        if (reason == RetargetReason.Earlier)
        {
            string earlier = string.IsNullOrEmpty(toTaxiwayName) ? "earlier exit" : $"taxiway {toTaxiwayName}";
            return $"Taking earlier exit, {earlier}, {distance} ahead.{slow}";
        }

        string to = string.IsNullOrEmpty(toTaxiwayName) ? "next exit" : $"taxiway {toTaxiwayName}";
        string straight = straighten ? " Straighten." : "";
        return reason == RetargetReason.TooFast
            ? $"Too fast for {from}.{straight} Continue to {to}, {distance}.{slow}"
            : $"Missed {from}.{straight} Retargeting {to}, {distance} ahead.{slow}";
    }

    /// <summary>
    /// The one sentence for a retarget whose every candidate failed to route. A too-fast call is made at the
    /// exit's turn point, before the pilot has reached it, so it never says "Missed". An earlier-exit retarget
    /// never gets here: it stays on the planned exit instead (<see cref="StaysOnPlannedExit"/>).
    /// </summary>
    public static string ComposeNoReachableExit(RetargetReason reason, string? fromTaxiwayName)
    {
        string from = TouchdownCallout.ExitNamePhrase(fromTaxiwayName);
        string lead = reason == RetargetReason.TooFast ? "Too fast for" : "Missed";
        return $"{lead} {from}. No reachable exit remaining.";
    }

    /// <summary>
    /// True when a retarget's fall-forward has reached the exit the rollout already targets and should stop
    /// there without a word. Only an EARLIER-exit retarget does: it is a detour on the way to the planned exit,
    /// so when no exit before it can be routed the pilot keeps the exit, route and callouts they had. It used to
    /// fall forward onto that same exit and say "Missed taxiway P. Retargeting taxiway P, 1500 feet ahead." about
    /// an exit still ahead, re-arming its approach calls - and the undershoot scan tried again every 8 s.
    /// </summary>
    public static bool StaysOnPlannedExit(
        RetargetReason reason, double candidateFromThresholdFeet, double plannedFromThresholdFeet)
        => reason == RetargetReason.Earlier && candidateFromThresholdFeet >= plannedFromThresholdFeet;

    /// <summary>Said at the turn-now point instead of "turn now" when too fast and no exit is left ahead.</summary>
    public static string ComposeTooFastNoExit(string? taxiwayName)
    {
        string name = TouchdownCallout.ExitNamePhrase(taxiwayName);
        return $"{char.ToUpperInvariant(name[0])}{name[1..]}, too fast to turn. Slow down.";
    }
}
