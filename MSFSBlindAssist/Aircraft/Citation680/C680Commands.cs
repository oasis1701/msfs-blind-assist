using System.Globalization;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>One calculator write. <paramref name="Unique"/> = send through ExecuteCalculatorCodeUnique (a valueless repeat would be dropped).</summary>
public readonly record struct C680Command(string Code, bool Unique);

/// <summary>
/// The calculator strings a C680 control runs, built from the cockpit's own click code (model
/// XML) and verified live on the Sovereign+ (2026-09-27). Pure, so every write is pinned by a test;
/// the definition only executes what this returns. An unknown key returns an empty list and the
/// definition's older handlers take it.
/// </summary>
public static class C680Commands
{
    /// <summary>
    /// The reverse detents as THROTTLEn_SET positions. The plugin's lever range runs -30 % (full
    /// reverse) to +100 %; a negative THROTTLEn_SET deploys the nozzle (measured: -2000 gave a -20 %
    /// lever and a deploying nozzle). THROTTLEn_DECR is FORWARD thrust — it moved the lever to +22 %.
    /// </summary>
    public const int IdleReverseSet = -1638;   // -10 %
    public const int MaxReverseSet = -4915;    // -30 %, the lever's lower limit

    public static IReadOnlyList<C680Command> For(string key, double value)
    {
        switch (key)
        {
            case "C680_RUN_L": return One(RunStop(1, value > 0.5));
            case "C680_RUN_R": return One(RunStop(2, value > 0.5));
            case "C680_REV_L": return One($"{ReverseSet(value)} (>K:THROTTLE1_SET)");
            case "C680_REV_R": return One($"{ReverseSet(value)} (>K:THROTTLE2_SET)");
            case "C680_THR_L_SET": return One($"{LeverSet(value)} (>K:THROTTLE1_SET)");
            case "C680_THR_R_SET": return One($"{LeverSet(value)} (>K:THROTTLE2_SET)");
            // STBY PWR: 0 ON / 1 OFF / 2 TEST (momentary — the definition holds TEST by repeating this write).
            case "C680_STBY_PWR": return One($"{Position(value)} (>B:ELECTRICAL_Battery_STBY_3_Set)", unique: true);
            // GEN: 0 ON / 1 OFF / 2 RESET. The plugin's ALTERNATOR_SET intercept works but leaves this switch unsynced.
            case "C680_GEN_L": return One($"(>B:ELECTRICAL_Alternator_1_{GenWord(value)})", unique: true);
            case "C680_GEN_R": return One($"(>B:ELECTRICAL_Alternator_2_{GenWord(value)})", unique: true);
            case "C680_APU_GEN": return One($"{Position(value)} (>B:ELECTRICAL_APU_Generator_1_Set)", unique: true);
            case "C680_CABIN_INTERNET":
                return One($"(A:CIRCUIT SWITCH ON:'{CabinInternetCircuit}'_n, Bool) {(value > 0.5 ? 0 : 1)} == if{{ '{CabinInternetCircuit}'_n (>K:ELECTRICAL_CIRCUIT_TOGGLE) }}");
            case "C680_STARTER_DISENG":
                return new[]
                {
                    new C680Command("(>B:ENGINE_Starter_Disengage_Push)", true),
                    new C680Command("1 (>L:SW_SOV_STARTER_DISENGAGE, bool)", true)
                };
        }
        return Array.Empty<C680Command>();
    }

    public const string CabinInternetCircuit = "ATG_4000_BROADBAND_UT580";

    /// <summary>The ON/OFF/RESET switches whose RESET is spring-loaded back to OFF.</summary>
    public static bool SpringsFromResetToOff(string key)
        => key is "C680_GEN_L" or "C680_GEN_R" or "C680_APU_GEN";

    private static int Position(double v) => Math.Clamp((int)Math.Round(v), 0, 2);
    private static string GenWord(double v) => Position(v) switch { 0 => "On", 1 => "Off", _ => "Reset" };

    /// <summary>
    /// The reverser combo's position from the signed lever (GENERAL ENG THROTTLE LEVER POSITION,
    /// percent): at or above -5 % the reverser is stowed, down to -20 % it is at idle reverse,
    /// below that at maximum reverse.
    /// </summary>
    public static double ReverserKey(double leverPercent)
        => leverPercent > -5 ? 0 : leverPercent > -20 ? 1 : 2;

    /// <summary>
    /// RUN/STOP state is the mixture lever (0 Stop / 100 Run), the model's own RunStop state.
    /// GENERAL ENG FUEL VALVE reads 1 at rest, so gating on it made the Run press a no-op.
    /// </summary>
    private static string RunStop(int engine, bool run)
        => $"(A:GENERAL ENG MIXTURE LEVER POSITION:{engine}, percent) 50 {(run ? "<" : ">=")} if{{ (>B:FUEL_RunStop_{engine}_Toggle) }}";

    private static int ReverseSet(double detent) => (int)Math.Round(detent) switch
    {
        <= 0 => 0,
        1 => IdleReverseSet,
        _ => MaxReverseSet
    };

    private static string LeverSet(double percent)
        => Math.Round(Math.Clamp(percent, 0, 100) * 163.84).ToString("0", CultureInfo.InvariantCulture);

    private static IReadOnlyList<C680Command> One(string code, bool unique = false) => new[] { new C680Command(code, unique) };
}
