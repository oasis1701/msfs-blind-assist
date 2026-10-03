using System;
using System.Collections.Generic;
using System.Drawing;
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
/// </summary>
public sealed class CowsDA40EngineGlanceForm : Form
{
    private readonly DisplayListBox _list;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly Func<List<string>> _readRows;

    public CowsDA40EngineGlanceForm(Func<List<string>> readRows)
    {
        _readRows = readRows;

        Text = "Engine";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 320);
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
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

        Shown += (_, _) => { Refresh0(); _list.Focus(); };
        FormClosed += (_, _) => _tick.Stop();
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
        DisplayList.UpdateInPlace(_list, rows);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tick.Stop(); _tick.Dispose(); }
        base.Dispose(disposing);
    }
}
