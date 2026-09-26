namespace MSFSBlindAssist.Navigation;

/// <summary>The runway correction a touchdown sentence leads with: the aircraft is on
/// <paramref name="ActualRunwayId"/>, not the planned <paramref name="PlannedRunwayId"/>.</summary>
public readonly record struct TouchdownRunwayCorrection(string ActualRunwayId, string PlannedRunwayId);

/// <summary>Exit-approach callouts a correction sentence retires, and what it folds in.</summary>
public readonly record struct ExitCalloutRetirement(
    bool Retire1500, bool Retire900, bool Retire500, bool RetireTurnNow, bool SlowDown);

/// <summary>Runway-end countdown callouts a correction sentence retires, and what it folds in.</summary>
public readonly record struct RunwayEndCalloutRetirement(
    bool Retire1500, bool Retire500, bool Retire100, bool SlowDown);

/// <summary>
/// The touchdown sentence of a landing-exit rollout, and — when it carries a runway correction —
/// which milestones it retires so their <c>AnnounceImmediate</c> cannot cut it off (PR #236 review:
/// the correction is the only sentence that tells the pilot the runways differ, so it must be heard
/// whole). The wording is short on purpose: measured through System.Speech at Rate 0, "Touchdown on
/// runway 30L, not 12L. High-speed exit taxiway M12A in 4,000 feet." is 7.5 s, against 10.5 s for
/// the PR's original "Landing exit plan was for runway 12L; you are on runway 30L. Touchdown. …".
///
/// <para>Without a correction the sentence is exactly the pre-existing one, so a landing on the
/// planned runway is unchanged. Pure — <c>TouchdownCalloutTests</c>.</para>
/// </summary>
public static class TouchdownCallout
{
    public static string ExitClassPhrase(string? exitType) => exitType switch
    {
        "High-speed" => "high-speed exit",
        "End"        => "runway-end exit",
        _            => "exit"
    };

    public static string ExitNamePhrase(string? taxiwayName)
        => string.IsNullOrEmpty(taxiwayName) ? "exit" : $"taxiway {taxiwayName}";

    public static string CorrectionLead(TouchdownRunwayCorrection correction)
        => $"Touchdown on runway {correction.ActualRunwayId}, not {correction.PlannedRunwayId}.";

    public static string ComposeExit(
        TouchdownRunwayCorrection? correction, string? exitType, string? taxiwayName,
        int distanceFeet, ExitCalloutRetirement retired, string? turnPhrase)
    {
        string exitClass = ExitClassPhrase(exitType);
        string name = ExitNamePhrase(taxiwayName);
        string where = distanceFeet > 0 ? $" in {Services.DistanceFormatter.FromFeet(distanceFeet)}" : "";

        if (correction is not TouchdownRunwayCorrection c)
            return $"Touchdown. {exitClass} {name}{where}.";

        string s = $"{CorrectionLead(c)} {char.ToUpperInvariant(exitClass[0])}{exitClass[1..]} {name}{where}.";
        if (retired.SlowDown) s += " Slow down.";
        if (retired.RetireTurnNow && !string.IsNullOrWhiteSpace(turnPhrase)) s += $" {turnPhrase.Trim()} now.";
        return s;
    }

    public static string ComposeNoUsableExit(
        TouchdownRunwayCorrection correction, int distanceToEndFeet, RunwayEndCalloutRetirement retired)
    {
        string s = $"{CorrectionLead(correction)} No usable exit.";
        if (!(retired.Retire1500 || retired.Retire500 || retired.Retire100)) return s;
        if (distanceToEndFeet > 0) s += $" Runway end in {Services.DistanceFormatter.FromFeet(distanceToEndFeet)}.";
        if (retired.Retire100) s += " Stop.";
        else if (retired.SlowDown) s += " Slow down.";
        return s;
    }

    /// <summary>The 1,500 / 900 / 500 ft milestones retire on <see cref="RolloutCalloutSupersession.Supersedes"/>
    /// (900 exists only for high-speed exits). Turn-now uses the strict inside test: "now" is
    /// time-critical and must never be spoken a lead window early. "Slow down." folds exactly when the
    /// rollout's own 500 ft callout would say it — above the exit's slow-down line
    /// (<see cref="RolloutExitGate.SlowDownAboveKts"/>), which the caller passes.</summary>
    public static ExitCalloutRetirement RetireExitCallouts(
        double distanceToExitFeet, double groundSpeedKts, string? exitType, double leadSeconds,
        double trigger1500Feet, double trigger900Feet, double trigger500Feet,
        double turnNowFeet, double slowDownAboveKts)
    {
        bool highSpeed = exitType == "High-speed";
        bool retire500 = RolloutCalloutSupersession.Supersedes(distanceToExitFeet, trigger500Feet, groundSpeedKts, leadSeconds);
        return new ExitCalloutRetirement(
            Retire1500: RolloutCalloutSupersession.Supersedes(distanceToExitFeet, trigger1500Feet, groundSpeedKts, leadSeconds),
            Retire900: highSpeed && RolloutCalloutSupersession.Supersedes(distanceToExitFeet, trigger900Feet, groundSpeedKts, leadSeconds),
            Retire500: retire500,
            RetireTurnNow: distanceToExitFeet <= turnNowFeet,
            SlowDown: retire500 && groundSpeedKts > slowDownAboveKts);
    }

    /// <summary>Same rules for the runway-end countdown; the 100 ft "Stop." uses the strict inside test.</summary>
    public static RunwayEndCalloutRetirement RetireRunwayEndCallouts(
        double distanceToEndFeet, double groundSpeedKts, double leadSeconds,
        double trigger1500Feet, double trigger500Feet, double trigger100Feet, double taxiSpeedKts)
    {
        bool retire500 = RolloutCalloutSupersession.Supersedes(distanceToEndFeet, trigger500Feet, groundSpeedKts, leadSeconds);
        return new RunwayEndCalloutRetirement(
            Retire1500: RolloutCalloutSupersession.Supersedes(distanceToEndFeet, trigger1500Feet, groundSpeedKts, leadSeconds),
            Retire500: retire500,
            Retire100: distanceToEndFeet <= trigger100Feet,
            SlowDown: retire500 && groundSpeedKts > taxiSpeedKts);
    }
}
