namespace MSFSBlindAssist.Aircraft;

// The DCDU's side of the Fenix display channel: the calls its window makes (Forms/Fenix/FenixChannelDcduForm.cs, whose rows
// are Services/FenixChannelDcduRows.cs and whose keys are Services/FenixChannelDcduKeys.cs). Nothing here reads the
// channel, and no press is written here: a key or a brightness pick goes through HandleUIVariableSet, the panel's own
// S_DCDU1_* branch, which presses and releases ([A320-3]) and carries the variant's refusal in front of it.
public partial class FenixA320Definition
{
    /// <summary>The captain's DCDU brightness switch, the variable the window's combo steps.</summary>
    internal const string DcduBrightnessKey = "S_DCDU1_DIM_BRT";

    /// <summary>
    /// How long after a brightness pick the switch is back in the middle and read again: the hold before the return write
    /// and the gap before the forced read (<see cref="PulseSpringSwitch"/>). The window shows Mid again then.
    /// </summary>
    internal const int DcduSpringBackMs = ButtonPressHoldMs + ButtonReleaseGapMs;

    // The tracker of the pilot's own page and message keys, made by the first press of one (UI thread only).
    private Services.FenixChannelDcduAnnouncer? _dcduAnnouncer;

    /// <summary>
    /// Opens the DCDU window on this instance's channel monitor, whatever the DCDU's verdict: the window shows one status
    /// row while the display is not trusted. With no monitor (before Start, after Stop) the pilot is told "DCDU display
    /// not available" at once and nothing opens (<see cref="ShowChannelWindow{TForm}"/>).
    /// </summary>
    internal void OpenDcduWindow(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        ShowChannelWindow(announcer, Services.FenixChannelDcdu.DisplayName,
            monitor => new Forms.Fenix.FenixChannelDcduForm(monitor, this, simConnect, announcer));
    }

    /// <summary>
    /// Presses the DCDU key <paramref name="key"/> through the panel's button for it. A page or message key tells the
    /// announcer first, so that what the key brings onto the screen is read in the window and never called as a new
    /// message; the announcer is made by the first such key.
    /// </summary>
    internal void PressDcdu(
        Services.FenixDcduKey key, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (Services.FenixChannelDcduKeys.IsNavigation(key)) (_dcduAnnouncer ??= new Services.FenixChannelDcduAnnouncer()).NoteNavigation();

        string varKey = Services.FenixChannelDcduKeys.VariableOf(key);
        HandleUIVariableSet(varKey, 1, GetVariables()[varKey], simConnect, announcer);
    }

    /// <summary>
    /// Moves the brightness switch to <paramref name="position"/> (0 Dim, 2 Bright) through the panel's branch, which
    /// steps it once and lets it spring back to the middle.
    /// </summary>
    internal void SetDcduBrightness(
        double position, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        HandleUIVariableSet(DcduBrightnessKey, position, GetVariables()[DcduBrightnessKey], simConnect, announcer);
    }
}
