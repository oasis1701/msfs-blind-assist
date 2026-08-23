namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A380 EFIS-CP ND option filter: ONE selection of Off / Waypoints / VOR-DME / NDB, per
/// side.
///
/// ⚠️ These three are NOT independent switches, and must never be offered as three On/Off
/// controls again. The FCU holds a single `pEfisFilter` enum (A380FcuComputer.cpp:2271-2281):
/// pressing the button that is already active clears it to NONE, and pressing any other button
/// REPLACES the selection. Each light is `efis_filter == &lt;enum&gt;` (:2567-2572), so two can
/// never be lit at once. Offering three switches produced the live report "turn Waypoints on,
/// then turn NDB on, and Waypoints turns off" — the aircraft was correct and the app was
/// showing a shape the aircraft does not have. CSTR, ARPT and V/V ARE independent toggles
/// (T flip-flops in the same function) and stay as separate controls.
///
/// There is no "off" button on the panel, which is why <see cref="PushEvent"/> clears the
/// selection by pressing whatever is currently ACTIVE rather than pressing the wanted one.
/// </summary>
public static class NdFilterSelection
{
    public const int Off = 0, Waypoints = 1, VorDme = 2, Ndb = 3;

    /// <summary>Selection implied by the three `A32NX_FCU_EFIS_{side}_{WPT,VORD,NDB}_LIGHT_ON`
    /// vars. Two lit at once is unreachable on this build, but a partially-delivered batch can
    /// show it for one frame, so the order here is fixed to keep the readout from flapping.</summary>
    public static int FromLights(bool wpt, bool vord, bool ndb) =>
        wpt ? Waypoints : vord ? VorDme : ndb ? Ndb : Off;

    /// <summary>
    /// The single button press that moves <paramref name="current"/> to <paramref name="desired"/>,
    /// or null when there is nothing to do. Always at most ONE press: the FCU replaces the
    /// selection outright, so there is never an "old one off, new one on" pair to send.
    /// </summary>
    public static string? PushEvent(string side, int current, int desired)
    {
        if (desired == current) return null;
        // No "off" button exists — clearing means re-pressing the active one.
        int button = desired == Off ? current : desired;
        string? name = button switch
        {
            Waypoints => "WPT",
            VorDme => "VORD",
            Ndb => "NDB",
            _ => null
        };
        return name == null ? null : $"A32NX.FCU_EFIS_{side}_{name}_PUSH";
    }

    /// <summary>
    /// True when the pilot asked for Off while a filter is actually shown — the one case the
    /// aircraft will not honour (see <see cref="ClearUnsupportedMessage"/>). Selecting Off when
    /// nothing is shown asks for nothing, and a filter-to-filter change is a normal replace.
    /// </summary>
    public static bool IsClearAttempt(int current, int desired) =>
        desired == Off && current != Off;

    /// <summary>
    /// Spoken when a clear is asked for. The press is still sent, but measured live on
    /// a380x 1bbd304 it does not take, and a control that silently does nothing is worse for a
    /// blind pilot than one that explains itself. Phrased as a property of this aircraft build,
    /// because that is what it is — the FCU's own generated code reads as though it should work.
    /// </summary>
    public static string ClearUnsupportedMessage =>
        "This A380 build cannot clear the ND filter. Choose Waypoints, VOR/DME or NDB instead.";

    /// <summary>Spoken/display text for a <see cref="FromLights"/> position.</summary>
    public static string Text(int position) => position switch
    {
        Waypoints => "Waypoints",
        VorDme => "VOR/DME",
        Ndb => "NDB",
        _ => "Off"
    };
}
