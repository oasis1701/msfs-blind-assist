using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.DA40;

/// <summary>
/// Alt+S — the engine, at a glance, as a LIVE WINDOW rather than one long sentence.
///
/// ⚠️ FOURTEEN READINGS IN ONE UTTERANCE IS NOT A GLANCE. The key answered with the whole
/// engine picture spoken end to end - load, RPM, oil pressure and temperature, coolant,
/// gearbox, fuel flow, bus volts and amps, each with its arc - which is a sighted pilot's
/// one look turned into a paragraph nobody can re-hear a part of. The pilot's ruling:
/// "output alt plus s should open up a little refreshing window, all that in one go might
/// be hard for some users."
///
/// So it opens the same shape every other live readout here uses: a read-only list the
/// pilot arrows through, refreshing on a tick, reconciled through
/// <see cref="DisplayList.UpdateInPlace"/> so only a CHANGED row re-announces and the
/// cursor never jumps off the row being read.
///
/// The rows themselves are unchanged and still come from the definition's own display
/// overrides, so a value can never disagree with the panel showing it - the arc is part of
/// the reading ("85 degrees celsius, green" is the answer; "85" is a number).
///
/// It opens and closes like the FBW E/WD window (<see cref="FbwEwdWindow"/>): unowned and
/// centred on the screen, brought to the front over the sim, and focus goes back to the
/// window the pilot pressed Alt+S in when it closes. Owned by BA's main window and centred
/// on it, it came up on top of BA instead of over the sim (user report 2026-10-28).
/// </summary>
public sealed class CowsDA40EngineGlanceForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly DisplayListBox _list;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly Func<List<string>> _readRows;
    private IntPtr _previousWindow;

    public CowsDA40EngineGlanceForm(Func<List<string>> readRows)
    {
        _readRows = readRows;

        Text = "Engine";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 320);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Engine readings, updates live"
        };
        Controls.Add(_list);

        // Same cadence as the panel status displays. A blind pilot reading a row must not
        // have it rewritten under them faster than they can hear it.
        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) => Refresh0();

        Shown += (_, _) => Refresh0();
        FormClosed += (_, _) =>
        {
            _tick.Stop();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    /// <summary>
    /// Opens the window, or brings it back to the front when it is already open, the same
    /// steps as the MCDU windows' ShowForm.
    /// </summary>
    public void ShowForm()
    {
        IntPtr foreground = GetForegroundWindow();
        if (!IsHandleCreated || foreground != Handle) _previousWindow = foreground;
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        ActiveControl = _list;
        _list.Focus();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _tick.Start();
    }

    /// <summary>Escape closes; F5 refreshes now, the same two keys every display here uses.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == Keys.F5) { Refresh0(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Refresh0()
    {
        if (IsDisposed || !IsHandleCreated) return;
        List<string> rows;
        try { rows = _readRows() ?? new List<string>(); }
        catch { return; }   // a read that throws must not kill the window

        if (rows.Count == 0) rows.Add("Engine readings not available yet");
        DisplayList.UpdateInPlace(_list, rows, selectFirstOnPopulate: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tick.Stop(); _tick.Dispose(); }
        base.Dispose(disposing);
    }
}
