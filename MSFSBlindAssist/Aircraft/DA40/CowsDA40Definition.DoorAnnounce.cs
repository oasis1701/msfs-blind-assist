using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// A door is OPEN or CLOSED. It is never "65.4".
///
/// ⚠️ THE DOORS WERE ANNOUNCING THEIR ANIMATION. `EXIT OPEN:n` is a percentage and the model
/// sweeps it over a second or so as the canopy swings, so a pilot who opened it heard
/// "Front Canopy: 15.0", "Front Canopy: 65.4" and then a state - three announcements for one
/// action, two of them meaningless. Reported from the cockpit as exactly that, with the
/// entirely reasonable note: "I don't need percentages."
///
/// So the door speaks the state it comes to REST in, once. The same settle the barometric
/// subscales and the radios use, for the same reason: what a pilot wants is where it ended
/// up, not a commentary on the way there. The percentage is still in the panel scan for
/// anyone who wants to know a door is halfway.
///
/// ⚠️ AND THE PERCENTAGE IS NOT THE STATE. A door on this aeroplane comes to rest at 100 or
/// at 0, but a storm window nudged by the airflow can sit anywhere - so "open" means anything
/// off the stop, which is also what the aeroplane's own DOOR OPEN warning means.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>
    /// How long the door must be still before its state is spoken. Longer than the radio
    /// settle because a canopy swings for well over a second.
    /// </summary>
    private const int DoorSettleMs = 1200;

    private static readonly Dictionary<string, string> DoorNames = new(StringComparer.Ordinal)
    {
        // The cockpit's own names (the model's tooltips: Canopy, Rear Canopy, Window).
        ["DA40_DOOR_CANOPY"] = "Canopy",
        ["DA40_DOOR_REAR"] = "Rear canopy",
        ["DA40_DOOR_STORM_L"] = "Left window",
        ["DA40_DOOR_STORM_R"] = "Right window"
    };

    /// <summary>Exposed for the tests, which check every name matches a real control.</summary>
    public static IReadOnlyCollection<string> DoorAnnounceKeys => DoorNames.Keys;

    private System.Windows.Forms.Timer? _doorTimer;
    private ScreenReaderAnnouncer? _doorAnnouncer;
    private readonly Dictionary<string, double> _doorPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _doorSpokenOpen = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns true for a door, which keeps the percentage off the generic announcer. The
    /// state is spoken by the settle below instead.
    /// </summary>
    private bool NoteDoorChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!DoorNames.ContainsKey(varKey)) return false;

        _doorAnnouncer = announcer;
        _doorPending[varKey] = value;

        if (_doorTimer == null)
        {
            _doorTimer = new System.Windows.Forms.Timer { Interval = DoorSettleMs };
            _doorTimer.Tick += (_, _) => FlushDoorSettle();
        }

        // Stop-then-start is what makes it a settle rather than a running commentary.
        _doorTimer.Stop();
        _doorTimer.Start();
        return true;
    }

    private void FlushDoorSettle()
    {
        _doorTimer?.Stop();

        var pending = new Dictionary<string, double>(_doorPending);
        _doorPending.Clear();
        if (pending.Count == 0 || _doorAnnouncer == null) return;

        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;
        var said = new List<string>();

        foreach (var kv in pending)
        {
            if (muted.Contains(kv.Key)) continue;

            // Off the stop is open - which is what the aeroplane's own DOOR OPEN warning
            // means, and what matters to a pilot about to take off.
            bool open = kv.Value > 0.5;

            // A first reading is not a change: connecting to an aeroplane with a door
            // already open must not announce it as though it had just swung.
            if (!_doorSpokenOpen.ContainsKey(kv.Key))
            {
                _doorSpokenOpen[kv.Key] = open;
                continue;
            }

            if (_doorSpokenOpen[kv.Key] == open) continue;
            _doorSpokenOpen[kv.Key] = open;

            said.Add(DoorNames[kv.Key] + " " + (open ? "open" : "closed"));
        }

        if (said.Count > 0) _doorAnnouncer.AnnounceImmediate(string.Join(". ", said));
    }

    private void StopDoorAnnounce()
    {
        try { _doorTimer?.Stop(); _doorTimer?.Dispose(); } catch { }
        _doorTimer = null;
        _doorPending.Clear();
    }
}
