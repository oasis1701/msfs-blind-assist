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
