using System.Collections.Generic;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>How one gear-off attempt reaches the sim.</summary>
public enum GearOffTransport
{
    /// <summary>TransmitClientEvent + MOUSE_FLAG_LEFTSINGLE on EVT_GEAR_LEVER. Ground
    /// testing (2026-08-26) confirmed this click is AUDIBLE to the pilot — the transmit
    /// path is reaching the aircraft — but a click that sounds like actuation is not
    /// proof of it (see the trap note on <see cref="GearOffLadder"/>); this rung is what
    /// finds out whether it also moves the lever off its UP detent.</summary>
    TransmitClick,
    /// <summary>Transmit EVT_GEAR_LEVER_UNLOCK LEFTSINGLE, hold, transmit EVT_GEAR_LEVER
    /// LEFTSINGLE, hold, transmit EVT_GEAR_LEVER_UNLOCK LEFTRELEASE — holds the unlock
    /// across the click. The lever is detented at UP and DOWN with OFF between them,
    /// which is what EVT_GEAR_LEVER_UNLOCK exists for; this rung is the hypothesis that
    /// the unlock must be held across the move, not pulsed before it. Ordering follows
    /// the PullFireHandleAsync precedent (PMDG737Definition.cs) — unlock, delay, move —
    /// though that helper uses WalkPMDGSelector rather than a raw transmit click.</summary>
    TransmitUnlockHeldClick,
    /// <summary>K:ROTOR_BRAKE encoded channel: param = (eventId - 69632) * 100 +
    /// mouseCode. The same channel already drives three PMDG 777 soundpack switches
    /// (PMDG777Definition.cs:6188-6209) and was independently re-confirmed live on the
    /// 737 speedbrake (mouse code 01 = left-single). Ground testing reports this one
    /// produces no sound at all for the gear lever — unlike the speedbrake, where it
    /// worked — so it is tried last, but it costs one more cheap attempt and the
    /// mouse-code table is partly inferred, so leaving it in may still pay off.</summary>
    RotorBrakeClick,
}

/// <summary>
/// A real, closed-loop, VERIFIED attempt to move the PMDG 737 gear lever to its OFF
/// detent — replacing a Reminder (acknowledge-only) item that shipped after 21 probing
/// shapes all left <see cref="StateField"/> unchanged, one of which (a fire-and-forget
/// SetSwitch dispatch of EVT_GEAR_LEVER) reported success while the lever never moved —
/// a safety defect for a blind pilot.
///
/// TRAP that fooled both the pilot and a prior investigation: TransmitClientEvent +
/// MOUSE_FLAG_LEFTSINGLE on EVT_GEAR_LEVER produces an AUDIBLE CLICK while the lever
/// does not move. Sound is not actuation on this control — never accept the click alone
/// as proof; this ladder always reads <see cref="StateField"/> back afterward instead.
///
/// New ground information (2026-08-26) is why this is worth attempting again rather than
/// staying acknowledge-only forever: the owner hears the TransmitClick above reach the
/// aircraft, while the identical click sent over the ROTOR_BRAKE encoded channel is
/// silent. So the transmit path is live; whether it can pull the lever out of its detent
/// (weight-on-wheels latches it at DOWN, so ground tests are inconclusive) is genuinely
/// unsettled — and a closed-loop attempt is safe under either outcome: it ticks only on
/// a verified move, and announces honestly when it fails. Shipping it also means
/// ordinary flights tell us which transport (if any) works, via the log, instead of more
/// probing sessions.
///
/// See <see cref="AircraftActionExecutor.SetGearLeverOffAsync"/> for the executor that
/// owns the I/O and the read-back timing; this class is the pure policy, testable
/// without SimConnect.
/// </summary>
public static class GearOffLadder
{
    /// <summary>PMDGNG3DataStruct field: 0 = UP, 1 = OFF, 2 = DOWN.</summary>
    public const string StateField = "MAIN_GearLever";

    /// <summary>Flow-step EventName that AircraftActionExecutor.ExecuteStepAsync
    /// intercepts (same mechanism as SPEEDBRAKE_ARM / FIRE_TEST / GPWS_TEST). Not a
    /// real PMDG event name — it must never appear in PMDG737Definition.EventIds.</summary>
    public const string PseudoKey = "GEAR_LEVER_OFF";

    /// <summary>Tried most-likely-first: the audibly-live transmit click, then the
    /// held-unlock variant of the same click, then the encoded channel the owner
    /// reports is silent for this control.</summary>
    public static IReadOnlyList<GearOffTransport> Attempts { get; } = new[]
    {
        GearOffTransport.TransmitClick,
        GearOffTransport.TransmitUnlockHeldClick,
        GearOffTransport.RotorBrakeClick,
    };

    /// <summary>Should another attempt be made after the one at <paramref name="attemptIndex"/>?</summary>
    /// <param name="attemptIndex">Zero-based index of the attempt just made.</param>
    /// <param name="reachedOff"><see cref="StateField"/> read back after that attempt,
    /// within 0.5 of 1.</param>
    public static bool ShouldContinue(int attemptIndex, bool reachedOff)
        => !reachedOff && attemptIndex < Attempts.Count - 1;
}
