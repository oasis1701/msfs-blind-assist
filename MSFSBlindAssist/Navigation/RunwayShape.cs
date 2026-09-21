using System.Runtime.CompilerServices;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Where a runway is, as every "is this on the runway / which end is this nearer" question in
/// taxi guidance must see it: the route classifier, hold placement, Where-Am-I, takeoff-assist
/// detection, the vacate resolver and the reach walk. Nothing else reads a centerline's
/// <c>Pavement*</c> fields directly.
///
/// <para>The PAVEMENT ends (runway table) are used when they form a sound line that belongs to this
/// centerline; otherwise the start rows. The start rows are repaired only laterally, so at a
/// displaced threshold they sit far inside the pavement — but a hand-built centerline leaves the
/// pavement unset, and a heading-pass mis-pair can hand a centerline another runway's pavement
/// (EDVQ). The extent is the envelope of both, so an outboard start row (LIMC 17L) stays inside.
/// Full rationale: docs/taxi-guidance.md, "Runway crossings and entries".</para>
///
/// <para>Projection matches <c>TaxiGraph.ProjectOntoCenterline</c>: equirectangular, 111,132 m per
/// degree of latitude, longitude scaled by cos(mid-latitude), anchored at end 1.</para>
/// </summary>
public sealed class RunwayShape
{
    /// <summary>TaxiGraph.Build's 75 ft default half-width, for a centerline that carries none.</summary>
    public const double DefaultHalfWidthMeters = 75.0 * 0.3048;

    /// <summary>
    /// Sanity cap on a pavement half-width derived from navdata's <c>runway.width</c>: measured
    /// over the shipped fs2024 database, that field reaches 2001 ft (105 rows over 400 ft) — at
    /// ZBAT a 546 ft width gives an 83.2 m half-width that alone swallows all 28 nodes of the
    /// airport's entire main taxi component (19 fs2024 airports have >= 50% of their main network
    /// swallowed this way; KMSP loses 213 of 3891 nodes). 400 ft is above any real runway and
    /// below every malformed row measured, so capping a pavement half-width to it here — where
    /// <see cref="For"/> derives <c>pavementHalf</c> — bounds the value for every consumer of
    /// <see cref="RunwayShape"/> at once, which is what rescues an unbounded case like KMSP's. A
    /// sound runway's width never approaches this, so the cap never changes the pavement-usable
    /// decision below for one.
    ///
    /// <para>⚠ ZBAT is cited above only as evidence the malformed-width problem is real — it is
    /// NOT a worked "the cap fixes this airport" example, and re-measurement
    /// (<c>tools/StandBridgeSweep</c>, 2026-09-17) disproves reading it as one: the cap IS active
    /// there (capped half-width 60.96 m, down from the uncapped 83.2 m) but all 28 main-component
    /// nodes STILL read as on-pavement afterward, because ZBAT's own taxi network sits at lateral
    /// offset 6.6-52.1 m from the centreline — genuinely inside even the narrower, capped band.
    /// ZBAT was also never a bridging case either way: its graph is a single connected component
    /// (28 of 28 nodes), so there was no orphan stand-stub island there to bridge before or after
    /// this cap existed. The cap bounds the unbounded, pathological rows (KMSP's shape) — it
    /// cannot and does not rescue every tight-clearance apron, and ZBAT's is one it can't.</para>
    /// </summary>
    public const double MaxPlausibleHalfWidthMeters = 400.0 * 0.3048 / 2.0;

    private const double MetersPerDegLat = 111132.0;

    private readonly double _metersPerDegLon;
    private readonly double _ux;
    private readonly double _uy;
    // The start row paired with each END BY POSITION — never by name index. See DepartureEndFor.
    private readonly (double Lat, double Lon) _startRowAtEnd1;
    private readonly (double Lat, double Lon) _startRowAtEnd2;

    public TaxiGraph.RunwayCenterline Centerline { get; }
    public double Lat1 { get; }
    public double Lon1 { get; }
    public double Lat2 { get; }
    public double Lon2 { get; }
    public string Name1 => Centerline.Name1 ?? "";
    public string Name2 => Centerline.Name2 ?? "";
    public double HalfWidthMeters { get; }
    /// <summary>True when the ends are the runway-table pavement, false when they are the start rows.</summary>
    public bool UsesPavement { get; private set; }
    public double LengthMeters { get; }
    public double ExtentMinMeters { get; private set; }
    public double ExtentMaxMeters { get; private set; }
    /// <summary>Ends less than a metre apart: no axis, so nothing is on this runway.</summary>
    public bool IsDegenerate => LengthMeters < 1.0;

    private RunwayShape(
        TaxiGraph.RunwayCenterline centerline,
        double lat1, double lon1, double lat2, double lon2,
        double halfWidthMeters, bool usesPavement)
    {
        Centerline = centerline;
        Lat1 = lat1; Lon1 = lon1; Lat2 = lat2; Lon2 = lon2;
        HalfWidthMeters = halfWidthMeters;
        UsesPavement = usesPavement;

        _metersPerDegLon = MetersPerDegLat * Math.Cos((lat1 + lat2) * 0.5 * (Math.PI / 180.0));
        double bx = (lon2 - lon1) * _metersPerDegLon;
        double by = (lat2 - lat1) * MetersPerDegLat;
        LengthMeters = Math.Sqrt(bx * bx + by * by);
        if (LengthMeters >= 1.0)
        {
            _ux = bx / LengthMeters;
            _uy = by / LengthMeters;
        }

        double min = 0.0, max = LengthMeters;
        if (usesPavement)
        {
            double a1 = Project(centerline.Lat1, centerline.Lon1).Along;
            double a2 = Project(centerline.Lat2, centerline.Lon2).Along;
            min = Math.Min(min, Math.Min(a1, a2));
            max = Math.Max(max, Math.Max(a1, a2));
        }
        ExtentMinMeters = min;
        ExtentMaxMeters = max;

        // Pair each end with the start row NEAREST IT ALONG THE AXIS. On sound data that is the row
        // whose name the end carries; on a name-swapped centreline it is the other one, which is the
        // whole of PR #238 deferred finding §5. The pairing carries the THRESHOLD only — the NAME
        // stays the shape's own (Name1 belongs to end 1, as the runway table orders the pavement).
        double rowA = Project(centerline.Lat1, centerline.Lon1).Along;
        double rowB = Project(centerline.Lat2, centerline.Lon2).Along;
        bool rowAIsNearerEnd1 = Math.Abs(rowA) <= Math.Abs(rowB);
        _startRowAtEnd1 = rowAIsNearerEnd1
            ? (centerline.Lat1, centerline.Lon1) : (centerline.Lat2, centerline.Lon2);
        _startRowAtEnd2 = rowAIsNearerEnd1
            ? (centerline.Lat2, centerline.Lon2) : (centerline.Lat1, centerline.Lon1);
    }

    /// <summary>
    /// One end of the runway, with its name, its lineup anchor and its takeoff heading — the three
    /// things a caller must never mix frames on.
    /// </summary>
    /// <param name="Designator">The end's name, from the shape (the PAVEMENT frame, as <see cref="NameAt"/> reads it).</param>
    /// <param name="ThresholdLat">The <c>start</c> row nearest this end BY POSITION: runway-destination lineup anchors on the start table, which is what accounts for displaced thresholds and starter extensions.</param>
    /// <param name="HeadingTrue">The true bearing of a departure from this end, measured along the shape's own axis.</param>
    public readonly record struct RunwayEndAnchor(
        string Designator, double ThresholdLat, double ThresholdLon, double HeadingTrue);

    /// <summary>The true bearing from end 1 to end 2, measured on the shape's own axis.</summary>
    public double HeadingFromEnd1Deg
    {
        get
        {
            if (IsDegenerate) return 0.0;
            double deg = Math.Atan2(_ux, _uy) * (180.0 / Math.PI);
            return deg < 0.0 ? deg + 360.0 : deg;
        }
    }

    /// <summary>
    /// The end whose takeoff heading is nearer <paramref name="aircraftHeadingTrue"/> — the end the
    /// aircraft is departing FROM — with its name, lineup anchor and heading taken together.
    ///
    /// <para>PR #238 deferred finding §5. <c>TryGetRunwayAtPosition</c> migrated its MEMBERSHIP test
    /// to this class but still picked the END from the centreline's <c>HeadingDeg1</c> and
    /// <c>Lat1/Lat2</c> — the START-ROW frame — while Where-Am-I named it through
    /// <see cref="NameAt"/>, the pavement frame. On a name-swapped centreline the two are reversed,
    /// and measured over 405 centrelines at 300 fs2024 airports, four disagreed outright (AYCH,
    /// OIII, URWW, EDVQ): a blind pilot asking Where-Am-I was told one runway while the
    /// takeoff-assist reference seeded at the same spot carried the other. Everything here comes
    /// from ONE frame, so name, threshold and heading can no longer disagree.</para>
    ///
    /// <para>The threshold is still a <c>start</c> row, never the pavement end — the lineup
    /// invariant — but the row PAIRED WITH THIS END BY POSITION rather than by name index.</para>
    /// </summary>
    public RunwayEndAnchor DepartureEndFor(double aircraftHeadingTrue)
    {
        double heading1 = HeadingFromEnd1Deg;
        double heading2 = (heading1 + 180.0) % 360.0;
        bool fromEnd1 = AngleBetween(aircraftHeadingTrue, heading1) <= AngleBetween(aircraftHeadingTrue, heading2);

        // An end with no designator falls back to the other's name, and takes that end's geometry
        // with it so the answer stays self-consistent (the pre-existing malformed-navdata rule).
        if (fromEnd1 && string.IsNullOrEmpty(Name1) && !string.IsNullOrEmpty(Name2)) fromEnd1 = false;
        else if (!fromEnd1 && string.IsNullOrEmpty(Name2) && !string.IsNullOrEmpty(Name1)) fromEnd1 = true;

        return fromEnd1
            ? new RunwayEndAnchor(Name1, _startRowAtEnd1.Lat, _startRowAtEnd1.Lon, heading1)
            : new RunwayEndAnchor(Name2, _startRowAtEnd2.Lat, _startRowAtEnd2.Lon, heading2);
    }

    private static double AngleBetween(double a, double b)
    {
        double d = Math.Abs((a - b) % 360.0);
        return d > 180.0 ? 360.0 - d : d;
    }

    /// <summary>
    /// ONE shape per centerline, memoised. A centerline is immutable once <c>TaxiGraph.Build</c> has
    /// applied its pavement — nothing writes those fields outside <c>ApplyPavement</c>, which runs
    /// before the graph is published — so the shape derived from it never changes either.
    ///
    /// <para>PR #238 deferred finding §8b. <see cref="For"/> is called per runway per
    /// classification, per passage for <c>otherRunways</c>, per NODE per runway in
    /// <c>IsOnAnyRunway</c>, per runway per Where-Am-I keypress, and once per hold node per
    /// candidate runway inside <c>TaxiGraph.Build</c>'s naming pass — hundreds of nodes at a large
    /// airport, on a path that runs synchronously on the UI thread for a Where-Am-I cache miss. It
    /// also ALLOCATED TWICE per call: the pavement-usable test built a throwaway shape purely to
    /// reuse <see cref="Project"/>, and the verdict then threw it away and built a second. Now the
    /// pavement candidate is built ONCE and widened after the verdict, and every later call for the
    /// same centerline is O(1).</para>
    ///
    /// <para>A weak-keyed table, so a graph that goes away takes its shapes with it — no per-airport
    /// cache to invalidate on a database switch — and so concurrent callers (the UI thread and the
    /// position thread both ask) need no lock. Do NOT inline a second copy of the projection math
    /// instead; this area already has four copies and that is its own finding.</para>
    /// </summary>
    private static readonly ConditionalWeakTable<TaxiGraph.RunwayCenterline, RunwayShape> Shapes = new();

    public static RunwayShape For(TaxiGraph.RunwayCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(centerline);
        return Shapes.GetValue(centerline, Create);
    }

    private static RunwayShape Create(TaxiGraph.RunwayCenterline centerline)
    {
        double pavementHalf = centerline.PavementHalfWidthMeters > 0.0
            ? Math.Min(centerline.PavementHalfWidthMeters, MaxPlausibleHalfWidthMeters)
            : DefaultHalfWidthMeters;

        var pavement = PavementCandidate(centerline, pavementHalf);
        if (pavement != null)
        {
            // The verdict is in, so widen the SAME instance's extent to envelope the start rows
            // rather than constructing a second shape over identical geometry.
            pavement.AdoptAsPavement();
            return pavement;
        }

        double startHalf = centerline.HalfWidthMeters > 0.0
            ? centerline.HalfWidthMeters : DefaultHalfWidthMeters;
        return new RunwayShape(centerline,
            centerline.Lat1, centerline.Lon1, centerline.Lat2, centerline.Lon2,
            startHalf, usesPavement: false);
    }

    /// <summary>
    /// The pavement line as a shape when it is a sound line for this centerline, else null. Returned
    /// rather than discarded so <see cref="Create"/> can keep it — see the allocation note above.
    /// </summary>
    private static RunwayShape? PavementCandidate(TaxiGraph.RunwayCenterline cl, double pavementHalf)
    {
        if (!double.IsFinite(cl.PavementLat1) || !double.IsFinite(cl.PavementLon1) ||
            !double.IsFinite(cl.PavementLat2) || !double.IsFinite(cl.PavementLon2))
            return null;
        if ((cl.PavementLat1 == 0.0 && cl.PavementLon1 == 0.0) ||
            (cl.PavementLat2 == 0.0 && cl.PavementLon2 == 0.0))
            return null;

        var pavement = new RunwayShape(cl,
            cl.PavementLat1, cl.PavementLon1, cl.PavementLat2, cl.PavementLon2,
            pavementHalf, usesPavement: false);
        if (pavement.IsDegenerate) return null;

        // The centerline's own start rows must lie on this pavement's axis, or the runway table
        // row belongs to a different runway (EDVQ heading-pass mis-pair).
        double limit = pavementHalf + RolloutExitGate.RunwayClearMarginM;
        return Math.Abs(pavement.Project(cl.Lat1, cl.Lon1).Lateral) <= limit
            && Math.Abs(pavement.Project(cl.Lat2, cl.Lon2).Lateral) <= limit
            ? pavement
            : null;
    }

    /// <summary>
    /// Marks this candidate as THE pavement and widens its extent to envelope the centerline's start
    /// rows — the one thing the constructor's <c>usesPavement</c> branch does. Private and called
    /// exactly once, by <see cref="Create"/>, before the shape is visible to anyone.
    /// </summary>
    private void AdoptAsPavement()
    {
        UsesPavement = true;
        double a1 = Project(Centerline.Lat1, Centerline.Lon1).Along;
        double a2 = Project(Centerline.Lat2, Centerline.Lon2).Along;
        ExtentMinMeters = Math.Min(ExtentMinMeters, Math.Min(a1, a2));
        ExtentMaxMeters = Math.Max(ExtentMaxMeters, Math.Max(a1, a2));
    }

    /// <summary>
    /// Along = metres from end 1 toward end 2, unclamped. Lateral = signed metres from the axis
    /// (positive left of end 1 → end 2); only its sign consistency within one runway matters.
    /// </summary>
    public (double Along, double Lateral) Project(double lat, double lon)
    {
        double px = (lon - Lon1) * _metersPerDegLon;
        double py = (lat - Lat1) * MetersPerDegLat;
        return (px * _ux + py * _uy, _ux * py - _uy * px);
    }

    /// <summary>
    /// On the runway: within half-width + <paramref name="lateralMarginMeters"/> and inside the
    /// extent. Named distinctly from the <c>(lat, lon, margin)</c> overload below because C#
    /// cannot overload on parameter names alone — both take <c>(double, double, double)</c> — so
    /// a caller that has already projected (e.g. to also read the lateral sign) uses this one
    /// instead of re-projecting.
    /// </summary>
    public bool ContainsAlongLateral(double along, double lateral, double lateralMarginMeters)
        => !IsDegenerate
           && Math.Abs(lateral) <= HalfWidthMeters + lateralMarginMeters
           && along >= ExtentMinMeters && along <= ExtentMaxMeters;

    /// <summary>On the runway: within half-width + <paramref name="lateralMarginMeters"/> and inside the extent.</summary>
    public bool Contains(double lat, double lon, double lateralMarginMeters)
    {
        var (along, lateral) = Project(lat, lon);
        return ContainsAlongLateral(along, lateral, lateralMarginMeters);
    }

    /// <summary>
    /// Far enough off the pavement for a stop point: the codebase's one definition of "off the
    /// runway" (half-width + <see cref="RolloutExitGate.RunwayClearMarginM"/>).
    ///
    /// <para>LATERAL ONLY, so a point beyond the runway's along-track extent is not clear of it by
    /// this test however far off the end it sits. Hold placement asks
    /// <see cref="IsClearOfAt"/> instead; this overload is kept for callers that have no
    /// along-track value and mean the lateral question alone.</para>
    /// </summary>
    public bool IsClearOf(double lateral)
        => Math.Abs(lateral) > HalfWidthMeters + RolloutExitGate.RunwayClearMarginM;

    /// <summary>
    /// Off the runway at a point: OUTSIDE the extent, or beyond half-width +
    /// <paramref name="lateralMarginMeters"/>. The exact complement of
    /// <see cref="ContainsAlongLateral"/>, which is what <see cref="IsClearOf"/> was not.
    ///
    /// <para>PR #238 deferred finding §3. <see cref="Contains"/> requires <c>along</c> inside the
    /// extent while <see cref="IsClearOf"/> tested <c>|lateral|</c> only, so a node BEYOND the
    /// runway's along-track extent but near its axis was neither "on the runway" nor "clear of" it.
    /// Hold placement's second walk stepped over it — and over every node behind it — and fell
    /// through to a START hold, telling the pilot to stop before moving, hundreds of metres from the
    /// real hold line, while no hold was placed where the route actually meets the pavement. The
    /// trigger shape is a taxiway running off the end of a runway on or near its extended
    /// centreline: a turnpad lead-in, or any approach to a crossing from beyond the end.</para>
    ///
    /// <para>⚠ <paramref name="lateralMarginMeters"/> is a parameter because the two walks
    /// deliberately differ and that is an owner ruling, not an oversight. The scenery-hold-line walk
    /// passes 0 — the bare half-width — so a painted line hugging the pavement edge is still usable
    /// (measured: SC99's line is 7.2 m out on a 4.0 m half-width, and tightening it to the clear
    /// margin would reject real hold lines). The fallback clear-node walk passes
    /// <see cref="RolloutExitGate.RunwayClearMarginM"/>, the codebase's definition of "off the
    /// runway" for a stop it invents itself. Do not collapse them.</para>
    /// </summary>
    public bool IsClearOfAt(double along, double lateral, double lateralMarginMeters)
        => along < ExtentMinMeters || along > ExtentMaxMeters
           || Math.Abs(lateral) > HalfWidthMeters + lateralMarginMeters;

    /// <summary>
    /// The designator of the end nearer <paramref name="along"/> (in a plane, exactly the
    /// closer-end rule), falling back to the other name when that end has none.
    /// </summary>
    public string NameAt(double along)
    {
        string near = along <= LengthMeters / 2.0 ? Name1 : Name2;
        string far = along <= LengthMeters / 2.0 ? Name2 : Name1;
        return string.IsNullOrEmpty(near) ? far : near;
    }
}
