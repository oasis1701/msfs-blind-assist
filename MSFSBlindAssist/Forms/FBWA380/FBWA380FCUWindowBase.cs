using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

// Shared plumbing for the A380 Fenix-style FCU windows: non-modal show, focus
// capture/restore, Escape-to-close. Subclasses build their own controls in their
// constructor and may override SpeakInitialReadout() to announce on open.
public class FBWA380FCUWindowBase : Form
{
    [DllImport("user32.dll")] protected static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] protected static extern bool SetForegroundWindow(IntPtr hWnd);

    protected readonly FlyByWireA380Definition aircraft;
    protected readonly SimConnectManager simConnect;
    protected readonly ScreenReaderAnnouncer announcer;
    private IntPtr previousWindow;

    protected FBWA380FCUWindowBase(
        FlyByWireA380Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
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
