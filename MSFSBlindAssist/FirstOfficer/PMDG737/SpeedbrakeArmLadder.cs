using System.Collections.Generic;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>How one arm attempt reaches the sim.</summary>
public enum SpeedbrakeArmTransport
{
    /// <summary>CDA control write with MOUSE_FLAG_LEFTSINGLE — the executor's normal path.</summary>
    CdaClick,
    /// <summary>TransmitClientEvent under the "#id" alias with MOUSE_FLAG_LEFTSINGLE — the
    /// transport the NG3's CDA-deaf controls (EVT_TCAS_MODE, the position-light selector,
    /// the CDU keys) require.</summary>
    TransmitClick,
    /// <summary>Transmit LEFTSINGLE, hold, then LEFTRELEASE — the shape the warning-test
    /// buttons need (see AircraftActionExecutor.WarningTestAsync).</summary>
    TransmitPressRelease,
}

/// <summary>
/// Pure escalation policy for arming the PMDG 737 speedbrake.
///
/// The event id is correct (THIRD_PARTY_EVENT_ID_MIN + 6792, matching the shipped
/// PMDG_NG3_SDK.h) and the dispatch table already forces MOUSE_FLAG_LEFTSINGLE, but the
/// NG3 has a documented family of CDA-deaf controls that only respond to
/// TransmitClientEvent mouse-clicks, and which family the speedbrake detents belong to
/// could not be settled from the repository. So the arm ESCALATES, reading
/// <see cref="ArmedField"/> back between attempts, and reports honestly if none takes —
/// the previous code dispatched once and reported success unconditionally.
///
/// Split out from the executor so the order and the early exit are testable without
/// SimConnect; the executor owns the I/O and the read-back timing.
///
/// NOTE: <see cref="ArmedField"/> reflects the auto-speedbrake system being ARMED, not raw
/// lever position, so it will not light cold-and-dark. Every consumer lives in the Landing
/// phase, where the aircraft is powered and configured. The NG3 exposes no lever-position
/// field at all — the analog position is only readable through the L-var switch_679_73X
/// (ARM = 100), which the FO state evaluator cannot reach (it reads the CDA struct and
/// synthetics only).
/// </summary>
public static class SpeedbrakeArmLadder
{
    /// <summary>PMDGNG3DataStruct field proving the lever reached ARM.</summary>
    public const string ArmedField = "MAIN_annunSPEEDBRAKE_ARMED";

    /// <summary>PMDGNG3DataStruct field for an auto-speedbrake fault. Lit, no number of
    /// clicks can arm the system, so the ladder stops — and this annunciator is already
    /// announced independently, so the pilot hears the real reason.</summary>
    public const string DoNotArmField = "MAIN_annunSPEEDBRAKE_DO_NOT_ARM";

    /// <summary>PMDGNG3DataStruct field meaning the speedbrake is already DEPLOYED
    /// (auto-deployed on touchdown, or manually raised). Clicking ARM here would retract
    /// the ground spoilers — see the already-armed/already-extended guard at the top of
    /// AircraftActionExecutor.ArmSpeedbrakeAsync.</summary>
    public const string ExtendedField = "MAIN_annunSPEEDBRAKE_EXTENDED";

    /// <summary>Flow-step EventName that AircraftActionExecutor.ExecuteStepAsync
    /// intercepts (same mechanism as FIRE_TEST / GPWS_TEST / TCAS_TEST). Not a real
    /// PMDG event name — it must never appear in PMDG737Definition.EventIds.</summary>
    public const string PseudoKey = "SPEEDBRAKE_ARM";

    /// <summary>Cheapest and most-likely first.</summary>
    public static IReadOnlyList<SpeedbrakeArmTransport> Attempts { get; } = new[]
    {
        SpeedbrakeArmTransport.CdaClick,
        SpeedbrakeArmTransport.TransmitClick,
        SpeedbrakeArmTransport.TransmitPressRelease,
    };

    /// <summary>Should another attempt be made after the one at <paramref name="attemptIndex"/>?</summary>
    /// <param name="attemptIndex">Zero-based index of the attempt just made.</param>
    /// <param name="armed">ArmedField read back after that attempt.</param>
    /// <param name="doNotArmLit">DoNotArmField read back after that attempt.</param>
    public static bool ShouldContinue(int attemptIndex, bool armed, bool doNotArmLit)
        => !armed && !doNotArmLit && attemptIndex < Attempts.Count - 1;
}
