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
/// sentence supersedes first (<see cref="TouchdownCallout.RetireExitCallouts"/> with
/// <see cref="LeadSeconds"/>), so no milestone can cut it off — KMEM 36L 2026-09-26: "Missed taxiway M6.
/// Retargeting taxiway M7, 650 feet ahead." was cut off 65 ms later by a stale "Taxiway M7, 900 feet."
/// at 631 ft. Pure — <c>RetargetCalloutTests</c>.
/// </summary>
public static class RetargetCallout
{
    /// <summary>
    /// How long the longest realistic retarget sentence takes to speak, MEASURED through System.Speech at
    /// Rate 0 (what ScreenReaderAnnouncer uses) with trailing silence trimmed, plus about a fifth:
    /// "Too fast for taxiway N12. Straighten. Continue to taxiway N14, 1250 feet. Slow down." = 10.76 s
    /// → 13 s (2026-09-26). Without its "Slow down." the same sentence is 9.15 s, which sized the earlier
    /// 11 s and was too short for the folded "Slow down."; the common "Missed taxiway M6. Straighten.
    /// Retargeting taxiway M7, 650 feet ahead." is 8.25 s.
    /// Never size this by estimate — re-measure when the wording changes.
    /// </summary>
    public const double LeadSeconds = 13.0;

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

    /// <summary>Said at the turn-now point instead of "turn now" when too fast and no exit is left ahead.</summary>
    public static string ComposeTooFastNoExit(string? taxiwayName)
    {
        string name = TouchdownCallout.ExitNamePhrase(taxiwayName);
        return $"{char.ToUpperInvariant(name[0])}{name[1..]}, too fast to turn. Slow down.";
    }
}
