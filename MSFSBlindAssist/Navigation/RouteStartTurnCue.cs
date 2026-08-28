namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The one owner of the route-start turn cue's wording — spoken when guidance begins with
/// the aircraft pointing well away from the route's first leg (the normal post-pushback
/// case, and the normal case after a landing-exit vacate parks the aircraft past the turn
/// it needs).
///
/// <para>Lifted verbatim out of <c>TaxiGuidanceManager</c> so the same text can be
/// delivered two ways — folded into the form's single standstill utterance, or spoken on
/// the first taxiing frame for routes the form did not start — without the two ever
/// disagreeing.</para>
///
/// <para>Live KATL 2026-08-27: the SayIntentions import spoke its summary and 50 ms later
/// this cue fired as an interrupting announcement, cutting it off mid-word; the pilot got a
/// fragment and then a taxiway-less "Make a U-turn to the left". That is the fifth time
/// two announcements at Calculate have stomped each other in this codebase (4837e45d,
/// 6891c0e7, 86744893, b772e845, c2b69455), and the established remedy is one
/// utterance.</para>
/// </summary>
public static class RouteStartTurnCue
{
    /// <summary>At or above this the route's first leg is a "sharp turn".</summary>
    public const double SharpTurnDeg = 100.0;

    /// <summary>At or above this it is a turnaround — "behind you" rather than "sharp".</summary>
    public const double TurnaroundDeg = 135.0;

    /// <summary>
    /// The cue, or null when the first leg is close enough ahead to need no words.
    /// </summary>
    /// <param name="headingErrorDeg">
    /// Signed heading error to the route's first target, normalized to [-180, +180].
    /// NEGATIVE = turn LEFT — the same convention the steering tone uses, so the spoken
    /// direction and the pan can never disagree.
    /// </param>
    /// <param name="firstTaxiwayName">
    /// The route's first taxiway, or null/blank when the route has no named first leg.
    /// </param>
    public static string? Compose(double headingErrorDeg, string? firstTaxiwayName)
    {
        double abs = Math.Abs(headingErrorDeg);
        if (abs < SharpTurnDeg) return null;

        string dir = headingErrorDeg < 0 ? "left" : "right";
        bool named = !string.IsNullOrWhiteSpace(firstTaxiwayName);
        string name = firstTaxiwayName?.Trim() ?? "";

        if (abs >= TurnaroundDeg)
            return named
                ? $"Taxiway {name} is behind you. Turn {dir} to come around."
                : $"Make a U-turn to the {dir}.";

        return named
            ? $"Sharp turn {dir} onto taxiway {name}."
            : $"Sharp turn {dir}.";
    }
}
