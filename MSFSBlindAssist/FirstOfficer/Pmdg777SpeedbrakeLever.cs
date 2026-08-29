namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The PMDG 777 speed-brake lever's value scale, in one place.
///
/// <c>FCTL_Speedbrake_Lever</c> is an ANALOG 0–100 position, not a detent index. The
/// detents were MEASURED on a live 777 at the gate (2026-08-29) by clicking each one
/// through the stock <c>K:ROTOR_BRAKE</c> transport and reading the field back, with the
/// owner also moving the lever by hand to confirm the field was live:
/// <code>
///   DOWN 0   |   ARM 50   |   half-deployed 75   |   UP 100
/// </code>
///
/// ⚠️ <b>The vendor SDK header is WRONG about this field.</b> PMDG_777X_SDK.h:454 — the
/// same line in the 77W, 77ER and 77F packages — says
/// <c>// Position 0...100  0: DOWN, 25: ARMED, 26...100: DEPLOYED</c>. The lever never
/// rests at 25. Do not "correct" the constants back to the comment; the 75 measurement is
/// what settles it rather than merely asserting it, because PMDG's own event for that
/// detent is named <c>EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50</c> and (75−50)/50 = 50 %,
/// which only works if deployment is measured from an ARM detent at 50.
///
/// The bug this class exists to fix is separate from that correction: every 777 First
/// Officer consumer tested <c>v &gt; 0.5 &amp;&amp; v &lt; 1.5</c> for ARMED — a detent
/// INDEX the lever cannot produce on any reading of the scale. Two visible consequences,
/// both reported from a live flight (2026-08-29): the Landing flow armed the lever and
/// then announced <em>"Skipping: Speedbrake: ARM"</em>, and a hand-tick of
/// "Speedbrake: ARMED" on the Landing checklist reverted and spoke
/// <em>"Unable to complete"</em>. The wrong scale lived only in a comment on
/// <c>AircraftStateEvaluator.SpeeedbrakeLeverPos</c> ("0=Down, 1=Armed, 2–7 = deployed
/// positions"). It is a class now so the consumers cannot drift again, and so the values
/// are testable without SimConnect — the project's idiom for FO decisions
/// (see <c>CenterPumpGate</c>, <c>GroundPowerGate</c>, <c>PMDG737.SpeedbrakeArmLadder</c>).
/// </summary>
public static class Pmdg777SpeedbrakeLever
{
    /// <summary>Lever fully down (auto-speedbrake not armed). Measured.</summary>
    public const int DownValue = 0;

    /// <summary>The ARM detent. Measured — one value, not a range, and not the header's 25.</summary>
    public const int ArmedValue = 50;

    /// <summary>The detent PMDG's own event calls "_50", i.e. 50 % deployed. Measured.</summary>
    public const int HalfDeployedValue = 75;

    /// <summary>Lever at the UP stop (full manual deployment). Measured.</summary>
    public const int UpValue = 100;

    // Half a count either side: the field is an unsigned byte, so this only absorbs the
    // double round-trip through GetFieldValue, never a neighbouring detent (the closest
    // pair is ARM 50 and half-deployed 75).
    private const double Tolerance = 0.5;

    public static bool IsDown(double lever) => lever < DownValue + Tolerance;

    public static bool IsArmed(double lever) =>
        lever > ArmedValue - Tolerance && lever < ArmedValue + Tolerance;

    /// <summary>
    /// Deployed is everything ABOVE the arm detent. Deliberately strict: a lever still
    /// travelling below ARM reads neither down nor deployed, so a verification fails
    /// toward "not armed" rather than claiming an arm that has not happened.
    /// </summary>
    public static bool IsDeployed(double lever) => lever >= ArmedValue + Tolerance;

    /// <summary>
    /// Deployment as a percentage of the travel a pilot commands, measured from the ARM
    /// detent (50) to the UP stop (100) — so the half-deployed detent at 75 reads 50 %,
    /// matching the name of the event that selects it.
    /// </summary>
    public static int DeployedPercent(double lever)
    {
        double span = UpValue - ArmedValue;
        double pct = (lever - ArmedValue) / span * 100.0;
        return (int)System.Math.Round(System.Math.Clamp(pct, 0, 100));
    }
}
