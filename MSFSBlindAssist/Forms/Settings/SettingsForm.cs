using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

public class SettingsForm : Form
{
    private readonly AccessibleTabControl _tabs;
    private readonly List<ISettingsPanel> _panels = new();
    private ISettingsPanel? _currentPanel;

    public SettingsForm(Func<Task>? refreshTaxiwayNames = null)
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        ClientSize = new System.Drawing.Size(660, 560);

        _tabs = new AccessibleTabControl { Dock = DockStyle.Fill, AccessibleName = "Settings sections" };
        // Stop the outgoing panel's tone BEFORE the tab actually switches.
        _tabs.Selecting += (_, _) => _currentPanel?.OnLeaving();
        _tabs.SelectedIndexChanged += (_, _) =>
        { if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < _panels.Count) _currentPanel = _panels[_tabs.SelectedIndex]; };

        // Panels are added here in FINAL TAB ORDER by later tasks. Task 1 adds only SimBrief;
        // subsequent tasks INSERT their AddPanel(...) calls so the final order is:
        // Announcements, GeoNames, SimBrief, Gemini, HandFly, TaxiGuidance.
        AddPanel(new SimBriefPanel());

        var ok = new Button { Text = "OK", AccessibleName = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AccessibleName = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += OnOk;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        buttonRow.Controls.Add(cancel);
        buttonRow.Controls.Add(ok);

        Controls.Add(_tabs);
        Controls.Add(buttonRow);
        AcceptButton = ok; CancelButton = cancel;
    }

    private void AddPanel(ISettingsPanel panel)
    {
        var uc = (UserControl)panel;
        uc.Dock = DockStyle.Fill;
        var page = new TabPage(panel.TabTitle) { AccessibleName = panel.TabTitle };
        page.Controls.Add(uc);
        _tabs.TabPages.Add(page);
        _panels.Add(panel);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var current = SettingsManager.Current;
        foreach (var p in _panels) p.LoadFrom(current);
        if (_panels.Count > 0) { _currentPanel = _panels[0]; _tabs.SelectedIndex = 0; }
    }

    private void OnOk(object? sender, EventArgs e)
    {
        for (int i = 0; i < _panels.Count; i++)
        {
            if (!_panels[i].Validate(out string error, out Control? focus))
            {
                _tabs.SelectedIndex = i;
                MessageBox.Show(this, error, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                focus?.Focus();
                return; // do not save
            }
        }
        var current = SettingsManager.Current;
        foreach (var p in _panels) p.ApplyTo(current);
        SettingsManager.Save();
        foreach (var p in _panels) p.OnLeaving();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (var p in _panels) p.OnLeaving(); // stop tones on every close path
        base.OnFormClosing(e);
    }
}
