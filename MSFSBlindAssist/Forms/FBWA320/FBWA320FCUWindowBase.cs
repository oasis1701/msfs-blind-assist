using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// Shared plumbing for the A320 Fenix-style FCU windows: non-modal show, focus
// capture/restore, Escape-to-close. Mirrors Forms/FBWA380/FBWA380FCUWindowBase so
// the A32NX gets the same value-entry windows as the A380 (the FCU event/var
// namespace is shared, so the windows are near-identical — only the typed aircraft
// reference and a couple of A320-vs-A380 hardware differences differ). Subclasses
// build their controls in the constructor and may override SpeakInitialReadout().
public class FBWA320FCUWindowBase : Form
{
    [DllImport("user32.dll")] protected static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] protected static extern bool SetForegroundWindow(IntPtr hWnd);

    protected readonly FlyByWireA320Definition aircraft;
    protected readonly SimConnectManager simConnect;
    protected readonly ScreenReaderAnnouncer announcer;
    private IntPtr previousWindow;

    protected FBWA320FCUWindowBase(
        FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        this.aircraft = aircraft;
        this.simConnect = simConnect;
        this.announcer = announcer;

        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
        FormClosing += (s, e) => { if (previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow); };
    }

    // Non-modal show (Show(), not ShowDialog()) so other windows stay accessible,
    // matching the Fenix FCU windows. Speaks the current value on open.
    public void ShowForm()
    {
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        SpeakInitialReadout();
    }

    protected virtual void SpeakInitialReadout() { }
}
