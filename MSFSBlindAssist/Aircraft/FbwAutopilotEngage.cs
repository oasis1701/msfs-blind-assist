namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The FlyByWire family's universal auto-engage decisions (FBW A32NX, FBW A380X, and the Headwind
/// A330, which inherits the A32NX definition) — FO-21. FlyByWire captures the stock
/// <c>AUTOPILOT_ON</c> and sets an <c>AP_engage</c> flag nothing has read since FBW #7587 (A32NX)
/// and #10855 (A380X), so the engage is the cockpit's AP 1 pushbutton, <c>A32NX.FCU_AP_1_PUSH</c>
/// (the event the AP 1 button and the ToggleAutopilot1 hotkey already send). The push is a TOGGLE:
/// it is pressed only once both autopilots read definitively off, never on an unknown state (the
/// service waits for a reading instead, <c>IAircraftDefinition.AutopilotEngageWaitsForState</c>).
/// Pure, so it is testable without a sim.
/// </summary>
internal static class FbwAutopilotEngage
{
    public const string Ap1PushEvent = "A32NX.FCU_AP_1_PUSH";
    public const string Ap1ActiveVar = "A32NX_AUTOPILOT_1_ACTIVE";
    public const string Ap2ActiveVar = "A32NX_AUTOPILOT_2_ACTIVE";

    /// <summary>Time between attempts (a press, or a wait for the first reading), and before giving
    /// up after the last one: the AP state reaches the cache with the 1 Hz batch, so a press sooner
    /// than this could land on an autopilot the previous press already engaged — and switch it
    /// off.</summary>
    public const int RetryIntervalMs = 3000;

    /// <summary>True when AP 1 or AP 2 reads engaged; false only when both read definitively off;
    /// null while either is unknown or NaN (FO-18: never a guessed false — the push is a toggle).</summary>
    public static bool? IsEngaged(Func<string, double?> read)
    {
        double? ap1 = read(Ap1ActiveVar);
        double? ap2 = read(Ap2ActiveVar);
        if (ap1 > 0.5 || ap2 > 0.5) return true;
        if (ap1 is not double a || ap2 is not double b || double.IsNaN(a) || double.IsNaN(b)) return null;
        return false;
    }

    /// <summary>True while <c>SimConnectManager.SendEvent</c> would hold a dotted event in its calc-path
    /// probe queue instead of sending it (SIM-13; the dotted branch of <c>SendEvent</c>): the MobiFlight
    /// bridge object exists and the probe has neither verified nor concluded. The bridge object exists
    /// on every connection, whether or not the WASM module is installed, so
    /// <c>CanExecuteCalculatorCode</c> is true either way; with no module the probe can never verify
    /// and runs its full ~60 s (40 attempts, 1.5 s apart) before it concludes. That queue is sent all
    /// at once when the probe ends, so several pushes queued by the service's retries would arrive back
    /// to back and toggle AP 1 on and off again. After the service has already told the Captain to
    /// engage the autopilot, those late pushes could also switch off the one the Captain engaged
    /// since. This only reports the condition; <see cref="PressFor"/> decides what to do about it. Not
    /// for the A380, whose <c>A32NX.FCU_*</c> events never wait (SIM-10). The arguments are
    /// <c>SimConnectManager</c>'s <c>CanExecuteCalculatorCode</c>, <c>CalcPathVerified</c> and
    /// <c>CalcPathProbeConcluded</c>.</summary>
    public static bool PressWouldWait(bool canExecuteCalculatorCode, bool calcPathVerified, bool calcPathProbeConcluded)
        => canExecuteCalculatorCode && !calcPathVerified && !calcPathProbeConcluded;

    /// <summary>The event the engage sends: AP 1's push, or null when nothing is sent. The push is a
    /// TOGGLE, so it goes out only when both autopilots read definitively off and the press would not
    /// wait behind the calc-path probe (<see cref="PressWouldWait"/>). Null when an autopilot already
    /// reads engaged: pressing would disconnect it. Null when the state is unknown, an AP var not read
    /// yet or NaN: a blind press could disconnect an autopilot that is already on, so the service waits
    /// for a definite reading instead (<c>IAircraftDefinition.AutopilotEngageWaitsForState</c>). The
    /// service does not ask to press from there, because MainForm pushes the aircraft's limits,
    /// <c>WaitForApState</c> among them, on every aircraft switch as well as every second; this
    /// guard stays as defence in depth. Null when both read off but the press would wait: the
    /// service retries from that state, so the waiting pushes could pile up and arrive back to back.
    /// That press is tried again after <see cref="RetryIntervalMs"/>, and if none ever goes out the
    /// service's Captain-action call is then true.</summary>
    public static string? PressFor(Func<string, double?> read, bool pressWouldWait = false)
        => (IsEngaged(read) == false && !pressWouldWait) ? Ap1PushEvent : null;
}
