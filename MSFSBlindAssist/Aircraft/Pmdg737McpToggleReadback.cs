using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The read-back of the PMDG 737's MCP toggles in the heading, speed, altitude and V/S windows
/// (Input Ctrl+H, Ctrl+S, Ctrl+A, Ctrl+V; owner, 2026-10-23, option A). Not the altimeter window, and no
/// other aircraft.
/// <para>
/// ValueInputForm reads a pressed toggle back 1.2 s after the press with an INTERRUPTING announce. On the
/// 737 a press that works is already confirmed three ways: the screen reader reads the button's new
/// label, the MCP light speaks ("MCP: Level Change: engaged") and the FMA speaks ("FMA: Pitch: MCP SPD").
/// The window's read-back repeated the MCP light and cut the FMA line off. So it is switched off for
/// these toggles, and 3 s after the press the state is read again: a state that changed says nothing
/// more; a state that did not change (the press did nothing) is read back, queued, in the window's
/// words ("Level Change Off"). A press the action refused (it spoke the refusal) is not read back either.
/// A toggle that shows no state (Altitude Intervene) has nothing to wait for: its press says
/// "Altitude Intervene pressed" at once, queued (owner, 2026-10-23).
/// </para>
/// </summary>
internal static class Pmdg737McpToggleReadback
{
    /// <summary>How long after a press the state is read again: past the SDK's next update and the FMA's
    /// settle, so a press that worked has changed it by then.</summary>
    public const int DelayMs = 3000;

    /// <summary>What the check says: the window's words for an unchanged state ("Level Change Off"), or
    /// null when the state changed, the press was refused, or the toggle shows no state (its press was
    /// confirmed at once, see <see cref="PressedLine"/>).</summary>
    public static string? Decide(string label, string stateAtPress, string stateNow, bool refused)
    {
        if (refused || string.IsNullOrEmpty(stateAtPress) || stateNow != stateAtPress) return null;
        return $"{label.Replace("&", "")} {stateNow}";
    }

    /// <summary>What the press of a toggle that shows no state says at once: "Altitude Intervene pressed",
    /// the app's words for a momentary button with no readable state.</summary>
    public static string PressedLine(string label) => $"{label.Replace("&", "")} pressed";

    /// <summary>Every toggle of one of the four MCP windows, wrapped (see <see cref="Wrap(ToggleButtonDef, ScreenReaderAnnouncer)"/>).</summary>
    public static List<ToggleButtonDef> WrapAll(IEnumerable<ToggleButtonDef> toggles, ScreenReaderAnnouncer announcer) =>
        toggles.Select(t => Wrap(t, announcer)).ToList();

    /// <summary>The toggle with the window's own read-back switched off and the 3 s check on its press.
    /// Its label, state and enable gate are its own; its refusal flag (its own
    /// <see cref="ToggleButtonDef.SuppressStateAnnounce"/>, if any) now only skips the check.</summary>
    public static ToggleButtonDef Wrap(ToggleButtonDef toggle, ScreenReaderAnnouncer announcer) =>
        Wrap(toggle, announcer, RunOnceOnUiThread);

    /// <summary><paramref name="schedule"/> runs the check after the delay; the tests capture it.</summary>
    internal static ToggleButtonDef Wrap(ToggleButtonDef toggle, ScreenReaderAnnouncer announcer,
        Action<int, Action> schedule)
    {
        var refused = toggle.SuppressStateAnnounce;
        return toggle with
        {
            OnPressed = () =>
            {
                string atPress = toggle.GetCurrentState();
                toggle.OnPressed();
                if (string.IsNullOrEmpty(atPress))
                {
                    // No state to read again: confirm the press now, queued, unless the action refused it.
                    if (refused?.Invoke() != true)
                        announcer.Announce(PressedLine(toggle.Label));
                    return;
                }
                // The check reads the data manager and speaks: it touches no control, so it is safe,
                // and still says its line, after the window has closed (the press happened).
                schedule(DelayMs, () =>
                {
                    if (Decide(toggle.Label, atPress, toggle.GetCurrentState(), refused?.Invoke() == true) is { } line)
                        announcer.Announce(line);
                });
            },
            SuppressStateAnnounce = () => true,
        };
    }

    /// <summary>A one-shot WinForms timer, started on the UI thread the button's click runs on, and
    /// disposed before the action runs.</summary>
    private static void RunOnceOnUiThread(int delayMs, Action action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = delayMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            action();
        };
        timer.Start();
    }
}
