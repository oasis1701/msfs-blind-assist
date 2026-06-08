namespace MSFSBlindAssist.Services;

/// <summary>A countdown milestone: trigger distance (metres, internal) + spoken label (active unit).</summary>
public readonly record struct DistanceMilestone(double TriggerMetres, string Label);

/// <summary>
/// Unit-native countdown tables. Each returns milestones ordered FAR -> NEAR for
/// the active unit, so callouts are round numbers in that unit (not literal
/// conversions). Triggers are metres so the surrounding guidance logic stays metric.
/// </summary>
public static class DistanceMilestones
{
    // Landing-exit approach: feet 1500/900/500 -> metres 500/300/150
    public static IReadOnlyList<DistanceMilestone> ExitApproach()
        => Build(feet: new[] { 1500, 900, 500 }, metres: new[] { 500, 300, 150 });

    // Runway-end (missed last exit): feet 1500/500/100 -> metres 500/150/30
    public static IReadOnlyList<DistanceMilestone> RunwayEnd()
        => Build(feet: new[] { 1500, 500, 100 }, metres: new[] { 500, 150, 30 });

    // Parking / gate arrival: feet 50/20/10 -> metres 15/10/5
    public static IReadOnlyList<DistanceMilestone> ParkingArrival()
        => Build(feet: new[] { 50, 20, 10 }, metres: new[] { 15, 10, 5 });

    private static IReadOnlyList<DistanceMilestone> Build(int[] feet, int[] metres)
    {
        if (feet.Length != metres.Length)
            throw new ArgumentException("feet and metres arrays must have the same length.");
        bool isMetres = DistanceFormatter.IsMetres;
        int[] vals = isMetres ? metres : feet;
        string unit = isMetres ? "metres" : "feet";
        double toMetres = isMetres ? 1.0 : DistanceFormatter.MetresPerFoot;
        var list = new List<DistanceMilestone>(vals.Length);
        foreach (int v in vals)
            list.Add(new DistanceMilestone(v * toMetres, $"{v} {unit}"));
        return list;
    }
}
