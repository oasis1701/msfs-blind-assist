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

    public TaxiGraph.RunwayCenterline Centerline { get; }
    public double Lat1 { get; }
    public double Lon1 { get; }
    public double Lat2 { get; }
    public double Lon2 { get; }
    public string Name1 => Centerline.Name1 ?? "";
    public string Name2 => Centerline.Name2 ?? "";
    public double HalfWidthMeters { get; }
    /// <summary>True when the ends are the runway-table pavement, false when they are the start rows.</summary>
    public bool UsesPavement { get; }
    public double LengthMeters { get; }
    public double ExtentMinMeters { get; }
    public double ExtentMaxMeters { get; }
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
    }

    public static RunwayShape For(TaxiGraph.RunwayCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(centerline);

        double pavementHalf = centerline.PavementHalfWidthMeters > 0.0
            ? Math.Min(centerline.PavementHalfWidthMeters, MaxPlausibleHalfWidthMeters)
            : DefaultHalfWidthMeters;
        if (PavementIsUsable(centerline, pavementHalf))
            return new RunwayShape(centerline,
                centerline.PavementLat1, centerline.PavementLon1,
                centerline.PavementLat2, centerline.PavementLon2,
                pavementHalf, usesPavement: true);

        double startHalf = centerline.HalfWidthMeters > 0.0
            ? centerline.HalfWidthMeters : DefaultHalfWidthMeters;
        return new RunwayShape(centerline,
            centerline.Lat1, centerline.Lon1, centerline.Lat2, centerline.Lon2,
            startHalf, usesPavement: false);
    }

    private static bool PavementIsUsable(TaxiGraph.RunwayCenterline cl, double pavementHalf)
    {
        if (!double.IsFinite(cl.PavementLat1) || !double.IsFinite(cl.PavementLon1) ||
            !double.IsFinite(cl.PavementLat2) || !double.IsFinite(cl.PavementLon2))
            return false;
        if ((cl.PavementLat1 == 0.0 && cl.PavementLon1 == 0.0) ||
            (cl.PavementLat2 == 0.0 && cl.PavementLon2 == 0.0))
            return false;

        var pavement = new RunwayShape(cl,
            cl.PavementLat1, cl.PavementLon1, cl.PavementLat2, cl.PavementLon2,
            pavementHalf, usesPavement: false);
        if (pavement.IsDegenerate) return false;

        // The centerline's own start rows must lie on this pavement's axis, or the runway table
        // row belongs to a different runway (EDVQ heading-pass mis-pair).
        double limit = pavementHalf + RolloutExitGate.RunwayClearMarginM;
        return Math.Abs(pavement.Project(cl.Lat1, cl.Lon1).Lateral) <= limit
            && Math.Abs(pavement.Project(cl.Lat2, cl.Lon2).Lateral) <= limit;
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
    /// </summary>
    public bool IsClearOf(double lateral)
        => Math.Abs(lateral) > HalfWidthMeters + RolloutExitGate.RunwayClearMarginM;

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
