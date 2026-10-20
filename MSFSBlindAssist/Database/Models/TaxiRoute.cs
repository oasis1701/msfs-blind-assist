namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Represents a calculated taxi route with segments and hold-short points.
/// </summary>
public class TaxiRoute
{
    public List<TaxiRouteSegment> Segments { get; set; } = new();
    public double TotalDistanceMeters { get; set; }
    public List<string> TaxiwaySequence { get; set; } = new();
    public List<TaxiHoldShort> HoldShortPoints { get; set; } = new();
    public string DestinationName { get; set; } = "";
    /// <summary>If the constrained route fell back to shortest path, explains why.</summary>
    public string? ConstrainedFallbackReason { get; set; }

    /// <summary>
    /// Every runway this route enters or crosses, in route order, held or not — recorded by the
    /// automatic hold-short pass when the route is adopted. The route's own arrival at a
    /// destination runway is not one. The summary and the "Route changed" clause are built from it.
    /// </summary>
    public List<TaxiRouteRunwayEvent> RunwayEvents { get; set; } = new();

    /// <summary>
    /// The hold label ("runway 12R") when the route's first stop point is its START node — it
    /// begins at a runway hold line with nowhere earlier to stop — so guidance starts held and
    /// waits for Continue. Null otherwise, and never set on a recalculated route.
    /// </summary>
    public string? StartHoldRunway { get; set; }

    /// <summary>
    /// A runway route's tail that the hold-short truncation cut off: the hold node, then every
    /// node on to the runway entry. After Continue the lineup tone follows it onto the pavement
    /// before the centreline intercept takes over. Null when nothing was cut.
    /// </summary>
    public List<(double Lat, double Lon)>? RunwayEntryStub { get; set; }

    /// <summary>
    /// Other runways whose pavement that entry stub rolls onto before (or while) it reaches the
    /// destination runway — intersecting runways at a threshold (KSLC 32 / 35). Named at the
    /// destination hold ("Hold short of Runway 32 and runway 35"), because nothing after it would.
    /// </summary>
    public List<string> RunwayEntryStubAlsoOn { get; set; } = new();
}

/// <summary>
/// A single segment in the taxi route (edge between two nodes).
/// </summary>
public class TaxiRouteSegment
{
    public TaxiNode FromNode { get; set; } = null!;
    public TaxiNode ToNode { get; set; } = null!;
    public double DistanceMeters { get; set; }
    public double CumulativeDistanceMeters { get; set; }
    public double RemainingDistanceMeters { get; set; }
    public string TaxiwayName { get; set; } = "";
    public double BearingDegrees { get; set; }
    public double TurnAngleDegrees { get; set; }
    public string TurnDirection { get; set; } = "straight";
    public double PathWidth { get; set; }
    public bool IsHoldShortPoint { get; set; }
    public string? HoldShortRunway { get; set; }
    /// <summary>
    /// True for a SYNTHETIC crossing segment bridging two disconnected components of
    /// the taxi network over pavement that has no taxi paths in the scenery (the
    /// "uncharted apron" leg — see TaxiRouter.FindCrossComponentPath). Guidance
    /// announces entering/leaving it, and auto-recalc is suppressed while one is
    /// still ahead (a rebuild from mid-gap has no charted position to route from).
    /// </summary>
    public bool IsUncharted { get; set; }
}

/// <summary>
/// Represents a hold-short instruction at a specific segment.
/// </summary>
public class TaxiHoldShort
{
    public int SegmentIndex { get; set; }
    public string RunwayOrTaxiway { get; set; } = "";
    public bool IsRunway { get; set; }
}

/// <summary>
/// How a route meets a runway it does not start on: it goes onto the pavement and back off the
/// SAME side (an entry — also a route that ends on the runway), or off the OTHER side (a crossing).
/// </summary>
public enum RunwayEventKind
{
    Entry,
    Crossing,
}

/// <summary>One runway a route enters or crosses, as the pilot is told about it.</summary>
public sealed class TaxiRouteRunwayEvent
{
    public RunwayEventKind Kind { get; init; }
    /// <summary>The designator announced for it (the pilot's own on the destination strip).</summary>
    public string Designator { get; init; } = "";
    /// <summary>Whether a stop was placed for it (false: passed already, no stop point, or cleared).</summary>
    public bool Held { get; set; }
    /// <summary>
    /// Not held because the aircraft is already past the stop, or already on this runway — as opposed to
    /// "there is no safe place to stop". Only the latter is announced: telling a pilot who is already
    /// crossing a runway that it has "no hold short point" invites a stop on the active runway.
    /// </summary>
    public bool Passed { get; init; }
    /// <summary>
    /// WHERE the runway is met, for the summary's "crossing runway 27 at A" — the hold label's own
    /// "at X" when the stop carries one, else the taxiway the route approaches the runway on. Null
    /// when neither is known. Spoken only for a runway met once (VATSIM gap analysis 2026-08-31, P1).
    /// </summary>
    public string? Location { get; init; }
}
