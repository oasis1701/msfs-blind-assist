using System.Runtime.InteropServices;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The "Fenix windows" list (Ctrl+Shift+F in input mode): one row per <see cref="FenixWindowEntry"/> the
/// <see cref="FenixWindowRegistry"/> offers. Enter or a double-click closes the list and opens the
/// selected window; Escape closes it and hands the foreground back. With no entries the list is
/// replaced by a read-only text box saying so.
/// <para>
/// No announcements beyond a failure to open: the screen reader already reads the window title, the
/// focused row and the window that opens next. Each row is a plain ListBox string, "Name. Description",
/// so the text that is shown and the text that is spoken are the same string.
/// </para>
/// </summary>
public sealed class FenixWindowsForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string EmptyText = "No Fenix windows yet.";

    private readonly IReadOnlyList<FenixWindowEntry> _entries;
    private readonly FenixWindowContext _context;
    private readonly ListBox? _list;
    private readonly TextBox? _status;
    private IntPtr _previousWindow;

    public FenixWindowsForm(IReadOnlyList<FenixWindowEntry> entries, FenixWindowContext context)
    {
        _entries = entries;
        _context = context;

        Text = "Fenix windows";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        ClientSize = new Size(520, 320);

        if (entries.Count == 0)
        {
            // Status text is a read-only TextBox in the tab order, never a Label [VAT-13].
            _status = new TextBox
            {
                Text = EmptyText,
                ReadOnly = true,
                TabStop = true,
                AccessibleName = "Status",
                Location = new Point(12, 12),
                Size = new Size(ClientSize.Width - 24, 27),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            Controls.Add(_status);
            ActiveControl = _status;
            return;
        }

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            AccessibleName = "Windows",
        };
        foreach (var entry in entries) _list.Items.Add(ItemText(entry));
        _list.SelectedIndex = 0;
        // Claim Enter as an input key so KeyDown reaches us, whatever the form does with dialog keys.
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            OpenSelected();
        };
        // Only a double-click ON a row opens it, not one in the empty space under the last row.
        _list.MouseDoubleClick += (_, e) =>
        {
            if (_list.IndexFromPoint(e.Location) >= 0) OpenSelected();
        };
        Controls.Add(_list);
        ActiveControl = _list;
    }

    /// <summary>The shown and spoken text of one row: "Name. Description", or just the name when it has no description.</summary>
    internal static string ItemText(FenixWindowEntry entry)
        => string.IsNullOrWhiteSpace(entry.Description) ? entry.Name : $"{entry.Name}. {entry.Description}";

    /// <summary>
    /// Shows the list with focus on its first row (or on the status text). Safe to call again on an open
    /// list, which brings it forward.
    /// </summary>
    public void ShowForm()
    {
        // Remembered on the first show only, so a second call from inside the list cannot name the list.
        if (!Visible) _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        (_list ?? (Control)_status!).Focus();
    }

    /// <summary>
    /// Opens the selected entry. The list closes FIRST, so the window that opens is never behind a list
    /// that is still on screen and the list is gone from the aircraft's tracked windows before the new
    /// one registers. A window that throws while opening is logged and spoken by name; it never escapes
    /// into the hotkey or the key handler that got us here.
    /// </summary>
    internal void OpenSelected()
    {
        int index = _list?.SelectedIndex ?? -1;
        if (index < 0 || index >= _entries.Count) return;
        var entry = _entries[index];

        Close();
        try
        {
            entry.Open(_context);
        }
        catch (Exception ex)
        {
            Log.Error("Fenix", $"Could not open the Fenix window '{entry.Name}'.", ex);
            _context.Announcer.AnnounceImmediate($"Could not open {entry.Name}.");
        }
    }

    // Escape closes the list. The foreground goes back to the window the pilot came from BEFORE the close,
    // while this process still holds it: once the list is destroyed Windows no longer lets us choose.
    // Enter does not do this, because the window that opens next takes the foreground itself.
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
}
