namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// A GSX per-aircraft parking stop offset in metres.
/// <c>LongitudinalMetres</c> is forward-positive along the gate lead-in;
/// <c>LateralMetres</c> is the perpendicular shift (negative = left, per GSX's
/// <c>(longitudinal, lateral)</c> tuple convention). Zero = stop at the navdata base.
/// </summary>
public readonly record struct GsxOffset(double LongitudinalMetres, double LateralMetres)
{
    /// <summary>No offset — stop at the base position (the safe default on any miss).</summary>
    public static GsxOffset Zero => new(0.0, 0.0);
}
