namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// COWS's own auto-start script, narrated from its step counter. Transcribed from the XLS
/// Logic's Autostart block: <c>INPUT_START</c> = 1 runs it, <c>AUTOSTART_STEP</c> walks
/// 0 → 11 in tenths, and it ends by writing <c>INPUT_START</c> back to 0 — from step 11
/// when it worked, from step 8 when seven seconds of cranking produced no fire (the step
/// is zeroed in the same tick, so at the stop it already reads 0). A flooded engine skips
/// 3 → 7 (no priming); an engine already turning over 400 rpm skips 0 → 9.
/// </summary>
public static class DA40AutoStart
{
    public const double CrankTimeoutSeconds = 7;
    public const int CrankStep = 8;
    public const int FinalStep = 11;

    public static string Describe(bool active, double step, double starterTimer)
    {
        if (!active) return "Not running";

        return (int)Math.Floor(step) switch
        {
            0 => "Master on, waiting for the PFD",
            1 => "Strobes on",
            2 => "Selecting the lower tank",
            3 => "Throttle to the priming mark",
            4 => "Electric pump on",
            5 => "Priming",
            6 => "Mixture to cut-off",
            7 => "Throttle set for the crank",
            CrankStep => $"Cranking, {starterTimer:F0} of {CrankTimeoutSeconds:F0} seconds",
            9 => "Mixture in",
            10 => "Electric pump off",
            FinalStep => "Alternator on",
            _ => $"Step {step:F0}"
        };
    }

    /// <summary>
    /// Spoken once when the script stops running. Judged on the ENGINE first: measured, a
    /// start that worked left the counter at 0, not 11, so the counter alone cannot say
    /// "complete" - only whether a failure was the seven-second crank timeout.
    /// </summary>
    public static string Outcome(double highestStepWhileActive, double stepAtStop, bool engineRunning)
    {
        if (engineRunning) return "Auto-start complete, engine running";
        if ((int)Math.Floor(highestStepWhileActive) == CrankStep && stepAtStop < 1)
            return $"Auto-start gave up, no fire in {CrankTimeoutSeconds:F0} seconds of cranking";
        return "Auto-start stopped, engine not running";
    }
}
