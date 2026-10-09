namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The FlyByWire family's universal auto-engage decisions (FBW A32NX, FBW A380X, and the Headwind
/// A330, which inherits the A32NX definition) — FO-21. FlyByWire captures the stock
/// <c>AUTOPILOT_ON</c> and sets an <c>AP_engage</c> flag nothing has read since FBW #7587 (A32NX)
/// and #10855 (A380X), so the engage is the cockpit's AP 1 pushbutton, <c>A32NX.FCU_AP_1_PUSH</c>
/// (the event the AP 1 button and the ToggleAutopilot1 hotkey already send). The push is a TOGGLE:
/// it is pressed only while no autopilot reads engaged. Pure, so it is testable without a sim.
/// </summary>
internal static class FbwAutopilotEngage
{
    public const string Ap1PushEvent = "A32NX.FCU_AP_1_PUSH";
    public const string Ap1ActiveVar = "A32NX_AUTOPILOT_1_ACTIVE";
    public const string Ap2ActiveVar = "A32NX_AUTOPILOT_2_ACTIVE";

    /// <summary>Time between presses, and before giving up after the last one: the AP state
    /// reaches the cache with the 1 Hz batch, so a press sooner than this could land on an
    /// autopilot the previous press already engaged — and switch it off.</summary>
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
    /// module is there and the probe has neither verified nor concluded. That queue is sent all at once
    /// when the probe ends, so several pushes queued by the service's retries would arrive back to back
    /// and toggle AP 1 on and off again. After the service has already told the Captain to engage the
    /// autopilot, those late pushes could also switch off the one the Captain engaged since. Only a
    /// retried press can pile up like that, so this only reports the condition: <see cref="PressFor"/>
    /// withholds a waiting press just when both autopilots read off, and a single press on an unknown
    /// state is still sent. Not for the A380, whose <c>A32NX.FCU_*</c> events never wait (SIM-10). The
    /// arguments are <c>SimConnectManager</c>'s <c>CanExecuteCalculatorCode</c>, <c>CalcPathVerified</c>
    /// and <c>CalcPathProbeConcluded</c>.</summary>
    public static bool PressWouldWait(bool canExecuteCalculatorCode, bool calcPathVerified, bool calcPathProbeConcluded)
        => canExecuteCalculatorCode && !calcPathVerified && !calcPathProbeConcluded;

    /// <summary>The event the engage sends: AP 1's push, or null when nothing is sent. Null in two
    /// cases. An autopilot already reads engaged: the push toggles, so pressing would disconnect it.
    /// Or both autopilots read definitively off and the press would wait behind the calc-path probe
    /// (<see cref="PressWouldWait"/>): the service retries from that state, so the waiting pushes
    /// could pile up and arrive back to back. That press is tried again after
    /// <see cref="RetryIntervalMs"/>, and if none ever goes out the service's Captain-action call is
    /// then true. With an unknown state (either autopilot unread, or NaN) the push is sent even if
    /// it waits: the service presses once from there and never retries, so one queued push cannot
    /// pile up, and when the probe ends it is sent and engages AP 1.</summary>
    public static string? PressFor(Func<string, double?> read, bool pressWouldWait = false)
    {
        bool? engaged = IsEngaged(read);
        if (engaged == true) return null;                     // the push toggles: it would disconnect it
        if (engaged == false && pressWouldWait) return null;  // a retry would queue behind this push
        return Ap1PushEvent;
    }
}
