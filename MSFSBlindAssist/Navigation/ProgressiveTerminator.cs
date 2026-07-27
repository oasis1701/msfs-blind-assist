namespace MSFSBlindAssist.Navigation;

/// <summary>
/// How a Progressive Taxi leg ends. The pilot picks this on the last taxiway
/// row of the planner; <see cref="Target"/> names the runway or taxiway it
/// refers to (empty for <see cref="ProgressiveTerminatorType.EndOfTaxiway"/>).
/// </summary>
public enum ProgressiveTerminatorType
{
    HoldShortRunway,
    HoldShortTaxiway,
    AfterCrossingRunway,
    EndOfTaxiway,
    /// <summary>
    /// Hold at a published NAMED holding point (VIKAS, N2E…) sourced from online
    /// data and resolved onto a navdata node by NamedHoldingPointResolver;
    /// <see cref="ProgressiveTerminator.Target"/> is the point's designator.
    /// </summary>
    HoldAtNamedPoint
}

/// <summary>
/// Immutable descriptor of a Progressive Taxi leg's endpoint. The form resolves
/// it to a destination graph node before calling LoadRoute; the manager keeps it
/// to drive the terminal "progressive hold" announcement and to suppress the
/// auto hold-short on a cleared crossing (AfterCrossingRunway only).
/// </summary>
public sealed class ProgressiveTerminator
{
    public ProgressiveTerminatorType Type { get; }
    public string Target { get; }   // runway ("09"), taxiway ("C"), or named holding point ("VIKAS") designator; "" for EndOfTaxiway

    public ProgressiveTerminator(ProgressiveTerminatorType type, string target)
    {
        Type = type;
        Target = target ?? "";
    }

    /// <summary>The spoken end-of-leg callout.</summary>
    public string EndAnnouncement() => Type switch
    {
        ProgressiveTerminatorType.HoldShortRunway =>
            $"Hold short of runway {Target}. Set a new route when cleared.",
        ProgressiveTerminatorType.HoldShortTaxiway =>
            $"Hold short of taxiway {Target}. Set a new route when cleared.",
        ProgressiveTerminatorType.AfterCrossingRunway =>
            $"Across runway {Target}. Hold position. Set a new route when cleared.",
        ProgressiveTerminatorType.HoldAtNamedPoint =>
            $"Hold at {Target}. Set a new route when cleared.",
        _ =>
            "Route ended. Hold position. Set a new route when cleared.",
    };

    /// <summary>Runway designator whose auto hold-short must be suppressed (cleared crossing), or null.</summary>
    public string? ClearedCrossingRunway =>
        Type == ProgressiveTerminatorType.AfterCrossingRunway ? Target : null;
}
