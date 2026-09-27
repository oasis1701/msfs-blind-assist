namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// How the MobiFlight calc-path probe reports itself.
///
/// ⚠️ The probe used to report its verdict NOWHERE — no log line on success, none on giving up.
/// That silence is precisely why a broken probe survived ten weeks (11 Jun to 22 Aug 2026),
/// quietly degrading every generic L:var write and every dotted FBW event while each symptom got
/// patched locally instead of traced. Whatever else changes here, KEEP THE VERDICT OBSERVABLE.
/// </summary>
public static class CalcPathVerdict
{
    /// <summary>
    /// Whether an AIRCRAFT SWITCH should re-arm the probe, given the verdict standing on this
    /// connection. True for "no verdict yet" and for "concluded UNVERIFIED"; false once VERIFIED.
    ///
    /// What the probe establishes is that the MobiFlight WASM executes an RPN write and it lands
    /// (<c>L:MSFSBA_BRIDGE_PROBE</c> through <c>MF.SimVars.Set</c>) — a property of the MODULE and
    /// the connection, neither of which an aircraft switch touches. The aircraft supplies only the
    /// data-def registration the read-back uses, which decides whether a verdict can be REACHED,
    /// not whether writes land once one has been.
    ///
    /// So the negative verdict is the one that must not be inherited: a profile registering no
    /// probe target (PMDG 737/777, HS787, iFly, Fenix) concludes UNVERIFIED and silently, and that
    /// standing made every write on the aircraft picked next refuse "unavailable" with a healthy
    /// path. Clearing a POSITIVE verdict as well cost a guaranteed degraded window on every switch
    /// — at least two probe ticks of FBW <c>SetLVar</c> falling back to the data-def write that
    /// reverts silently, and up to ~60 s of queued dotted events on a machine with no WASM module
    /// — and prevented no named failure: a path that died mid-connection fails its writes whether
    /// or not the flag says so, and the probe does not re-run once concluded.
    /// </summary>
    public static bool ShouldRearmOnAircraftSwitch(bool calcPathVerified) => !calcPathVerified;

    /// <summary>One line for debug.log, on success AND on give-up — the good case has to be
    /// confirmable too, or "is the path up?" stays unanswerable from a log.</summary>
    public static string LogLine(bool verified, int attempts) =>
        verified
            ? $"calc path VERIFIED after {attempts} attempt(s) — MobiFlight round-trip succeeded"
            : $"calc path NOT available after {attempts} attempt(s) — falling back to the "
              + "data-def write and the legacy event transport";

    /// <summary>
    /// What to SAY, or null to stay silent. Only one case speaks: an aircraft that needs the
    /// calculator path and hasn't got it. That is a degraded session — overhead switches may
    /// silently revert and the FCU may ignore commands — which the pilot would otherwise
    /// discover one dead control at a time. Everything else is normal operation.
    /// </summary>
    public static string? PilotWarning(bool verified, bool aircraftNeedsCalcPath) =>
        !verified && aircraftNeedsCalcPath
            ? "MobiFlight calculator path unavailable. Some cockpit controls may not respond."
            : null;
}
