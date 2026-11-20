namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The XLS's "red box": the mixture region in which a cylinder is being damaged by the
/// mixture setting, transcribed from the model (COWS_DA40_Logic.xml, the "Whos there?"
/// block). Per cylinder, and only while that cylinder's heat output is at or above
/// <see cref="OpenAtKw"/> AND its air/fuel ratio is inside 12.9-15.5:
///
///   FAC = (kW - 42) / 8;  rich edge = 14.7 - 1.8 FAC;  lean edge = 14.7 + 0.8 FAC;
///   depth = rich of 14.7 ? (AFR - rich edge) / 1.8 : (lean edge - AFR) / 0.8, floored at 0
///
/// and DAMAGE_CYL:n grows by 0.05 x depth per tick. So at 42 kW the box is a single point
/// at stoichiometric and it widens with power; at 50 kW it spans 12.9-15.5, the whole
/// window the model gates on.
///
/// ⚠️ <c>DAMAGE_REDBOX_ITS:n</c> HOLDS ITS LAST VALUE when the box closes - measured 19.7
/// with the engine STOPPED - so the state is recomputed from the inputs here and that
/// variable is never read for it. Measured at run-up power (2211 rpm) the heat was 30.8
/// kW per cylinder: the box cannot open on the ground at run-up power, only at high power.
/// </summary>
public static class DA40RedBox
{
    public const double OpenAtKw = 42;
    public const double WindowRich = 12.9;
    public const double WindowLean = 15.5;
    public const double Stoichiometric = 14.7;

    private static double Factor(double heatKw) => (heatKw - OpenAtKw) / 8.0;

    public static double RichEdge(double heatKw) => Stoichiometric - 1.8 * Factor(heatKw);
    public static double LeanEdge(double heatKw) => Stoichiometric + 0.8 * Factor(heatKw);

    /// <summary>How far inside the box, on the model's own scale; 0 when outside.</summary>
    public static double Depth(double heatKw, double airFuel)
    {
        if (heatKw < OpenAtKw) return 0;
        if (airFuel < WindowRich || airFuel > WindowLean) return 0;
        return airFuel < Stoichiometric
            ? Math.Max(0, (airFuel - RichEdge(heatKw)) / 1.8)
            : Math.Max(0, (LeanEdge(heatKw) - airFuel) / 0.8);
    }

    public static bool IsInside(double heatKw, double airFuel) => Depth(heatKw, airFuel) > 0;

    public static string Describe(double[] heatKw, double[] airFuel)
    {
        var inside = new List<int>();
        for (int i = 0; i < Math.Min(heatKw.Length, airFuel.Length); i++)
        {
            if (IsInside(heatKw[i], airFuel[i])) inside.Add(i + 1);
        }
        if (inside.Count == 0) return "Clear";
        return $"In the red box, {DA40CylinderState.Cylinders(inside)} - the mixture is damaging the engine";
    }
}
