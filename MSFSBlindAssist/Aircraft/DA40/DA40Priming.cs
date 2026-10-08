namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>How much fuel the XLS's cylinders hold, against what a start needs.</summary>
public enum PrimeState
{
    /// <summary>Less than the required charge — priming needed.</summary>
    NotPrimed,
    /// <summary>At or above the required charge and no cylinder over the flooded line.</summary>
    Primed,
    /// <summary>At least one cylinder over the flooded line — crank it out at cut-off.</summary>
    Flooded
}

/// <summary>
/// The XLS priming arithmetic, transcribed from the aircraft's own model
/// (COWS_DA40_Logic.xml: the priming-assist block and the autostart script's "skip if
/// flooded" test) — never invented, and pinned against numbers read off the live aircraft.
///
/// The injected Lycoming has no primer: the idle/starting jet puts fuel into the induction
/// with the mixture forward and the pump on, and that fuel sits OUTSIDE the cylinders until
/// the crank draws it in. The model tracks it in grams per cylinder
/// (<c>ENG_FUEL_OUTSIDE_CYL_GRAM:n</c>), and:
///
///   required per cylinder = 1.5 / atomisation(n) / evaporation
///   flooded  per cylinder = 1.6 / evaporation / atomisation(n)      (any cylinder over → flooded)
///
/// so the window between primed and flooded is about SIX PERCENT. Measured at EGNX with the
/// engine hot: about 0.39 g per cylinder required, 0.41 flooded — and the idle jet loaded
/// 2.5 g per cylinder per second, so a hot engine floods in well under a second of rich.
/// After a mixture-cut shutdown the cylinders held 1.2 g each (flooded, three times over)
/// while the vendor's percentage gauge said 78 %, because that gauge counts the LINES as
/// well as the cylinders against a fixed 10.5 g of line fill. That is why cylinders and
/// required are said separately here and a bare percentage never is — the plan's own rule.
///
/// FLOODED IS NOT "WILL NOT START". It is the AFM's 4.5 (c) case: pump off, mixture fully
/// aft, throttle mid, crank — the charge burns down and the engine coughs (4.8 g in one
/// second, 32 g in 4.7, measured), and the mixture goes forward at the cough. It is
/// recoverable in the aeroplane and the panel says so.
/// </summary>
public static class DA40Priming
{
    public const double RequiredNumerator = 1.5;
    public const double FloodedNumerator = 1.6;

    /// <summary>The fixed line-fill the vendor's percentage gauge adds to the requirement.</summary>
    public const double GaugeLineFillGrams = 10.5;
    public const double GaugeMaxPercent = 150;

    public const int Cylinders = 4;

    public static double RequiredPerCylinder(double atomisation, double evaporation)
        => atomisation <= 0 || evaporation <= 0 ? 0 : RequiredNumerator / atomisation / evaporation;

    public static double FloodedPerCylinder(double atomisation, double evaporation)
        => atomisation <= 0 || evaporation <= 0 ? double.PositiveInfinity : FloodedNumerator / evaporation / atomisation;

    /// <summary>
    /// The model's total requirement, exactly as it computes it: cylinder 1's atomisation,
    /// times four.
    /// </summary>
    public static double RequiredTotal(double atomisation1, double evaporation)
        => RequiredPerCylinder(atomisation1, evaporation) * Cylinders;

    /// <summary>The vendor's percentage, reproduced so the panel can show it when the option is on.</summary>
    public static double GaugePercent(double systemGrams, double cylinderGrams, double requiredTotal)
        => Math.Clamp((systemGrams + cylinderGrams) / (requiredTotal + GaugeLineFillGrams) * 100, 0, GaugeMaxPercent);

    /// <summary>
    /// Flooded if ANY cylinder is over its line (the autostart's OR); primed if the total
    /// meets the requirement; otherwise not primed.
    /// </summary>
    public static PrimeState Classify(double[] cylinderGrams, double[] atomisation, double evaporation)
    {
        double total = 0;
        for (int i = 0; i < cylinderGrams.Length; i++)
        {
            total += cylinderGrams[i];
            double atom = i < atomisation.Length ? atomisation[i] : atomisation[0];
            if (cylinderGrams[i] >= FloodedPerCylinder(atom, evaporation)) return PrimeState.Flooded;
        }

        return total >= RequiredTotal(atomisation[0], evaporation) ? PrimeState.Primed : PrimeState.NotPrimed;
    }

    /// <summary>Both numbers, always; the state first because it is the answer.</summary>
    public static string Describe(PrimeState state, double cylinderGrams, double requiredTotal)
    {
        string label = state switch
        {
            PrimeState.Flooded => "Flooded",
            PrimeState.Primed => "Primed",
            _ => "Not primed"
        };
        return $"{label}, {cylinderGrams:0.0} grams in the cylinders, {requiredTotal:0.0} needed";
    }
}
