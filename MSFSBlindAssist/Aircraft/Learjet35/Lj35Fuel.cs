namespace MSFSBlindAssist.Aircraft.Learjet35;

public static class Lj35Fuel
{
    public const double PoundsPerGallonJetA = 6.7;

    public static string Describe(double pounds) => $"{pounds:0} pounds, {pounds / PoundsPerGallonJetA:0} gallons";

    /// <summary>The manual (page 67): transfer when either tip is below 760 lb.</summary>
    public static string TransferAdvice(double tipL, double tipR, double fuselage)
        => fuselage <= 0 ? "Fuselage empty"
         : (tipL < 760 || tipR < 760) ? "Tips below 760: fuselage transfer"
         : "No transfer needed";
}
