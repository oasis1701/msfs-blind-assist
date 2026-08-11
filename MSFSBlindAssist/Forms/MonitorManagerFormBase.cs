using System.Runtime.InteropServices;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Shared base for every per-aircraft Monitor Manager dialog (Ctrl+M). Owns the whole UI —
/// instructions, search box, Show filter, checked list — plus the filtering, the settings
/// write and the hide-on-close gate. A subclass supplies three things: the window title, its
/// rows, and the aircraft's disabled-variable collection.
///
/// Checked = announcing, unchecked = muted. The filter changes only WHICH rows are visible;
/// it never changes what a row means or what ticking one writes.
/// </summary>
public abstract class MonitorManagerFormBase : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Last-focused row, per CONCRETE form type. A single shared static would leak
    /// the A380 dialog's row index into the Fenix dialog after an aircraft switch.</summary>
    private static readonly Dictionary<Type, int> _lastIndexByForm = new();

    private readonly IReadOnlyList<MonitorRow> _allRows;

    /// <summary>The rows currently in the list box. Tracked separately because once a filter
    /// is applied, an index into the list box no longer maps to <see cref="_allRows"/>.</summary>
    private List<MonitorRow> _visibleRows = new();

    private TextBox _searchBox = null!;
    private ComboBox _showCombo = null!;
    private CheckedListBox _list = null!;
    private IntPtr _previousWindow;

    /// <summary>Guards <see cref="OnItemCheck"/> while a rebuild runs. SetItemChecked raises
    /// ItemCheck once per row, so without this a single filter keystroke on the 400-row PMDG
    /// list fires ~400 SettingsManager.Save() disk writes on the UI thread.</summary>
    private bool _suppressItemCheck;

    /// <summary>Set while <see cref="ShowForm"/> resets the search box and Show combo, so those
    /// two writes do not each trigger their own rebuild ahead of the single one it then makes
    /// itself.</summary>
    private bool _suppressFilter;

    /// <summary>The aircraft's persisted mute list. A PROPERTY, re-read on every access: the
    /// live List in UserSettings is the source of truth, and the ...Set HashSet sidecar can
    /// lag between a mutation and the next Save.</summary>
    protected abstract ICollection<string> DisabledVariables { get; }

    protected MonitorManagerFormBase(string title, IReadOnlyList<MonitorRow> rows)
    {
        _allRows = rows;
        InitializeComponent(title);

        // Hide instead of dispose so check state and window position survive between opens.
        MonitorManagerShared.HideOnClose(this, () =>
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        });
        // NOTE: no ApplyFilter() here on purpose. It reads DisabledVariables, which is
        // abstract — calling it from a base constructor would run subclass code before the
        // subclass is initialised. ShowForm() is the only entry point and always filters.
    }

    private void InitializeComponent(string title)
    {
        Text = title;
        Size = new Size(560, 460);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        // TabIndex is set on the labels too: a Label is not a tab STOP, but its mnemonic
        // (Alt+S / Alt+H) moves focus to the next control in tab ORDER, so each label must
        // sit immediately before the control it names.
        var instructions = new Label
        {
            Text = "Untick a variable to stop announcing it as it changes. Re-ticking restores it.",
            Location = new Point(10, 10),
            Size = new Size(530, 30),
            TabIndex = 0,
            AccessibleName = "Instructions"
        };

        var searchLabel = new Label
        {
            Text = "&Search:",
            Location = new Point(10, 50),
            Size = new Size(60, 22),
            TabIndex = 1,
            AccessibleName = "Search label"
        };

        _searchBox = new TextBox
        {
            Location = new Point(75, 48),
            Size = new Size(240, 22),
            TabIndex = 2,
            AccessibleName = "Search",
            AccessibleDescription = "Type to narrow the list to variables whose name contains the typed text"
        };
        _searchBox.TextChanged += (_, _) => { if (!_suppressFilter) ApplyFilter(); };

        var showLabel = new Label
        {
            Text = "S&how:",
            Location = new Point(330, 50),
            Size = new Size(50, 22),
            TabIndex = 3,
            AccessibleName = "Show label"
        };

        // A real DropDownList combo, so the screen reader announces the selection natively.
        _showCombo = new ComboBox
        {
            Location = new Point(385, 48),
            Size = new Size(155, 22),
            TabIndex = 4,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Show"
        };
        _showCombo.Items.AddRange(new object[] { "All", "Muted", "Unmuted" });
        _showCombo.SelectedIndex = 0;
        // Attached AFTER the initial SelectedIndex set above, not before: setting SelectedIndex
        // fires SelectedIndexChanged synchronously, and _list does not exist yet at this point in
        // the constructor (it's built a few lines below) — attach earlier and this handler's
        // ApplyFilter() call throws NullReferenceException on _list, taking every Ctrl+M down with
        // it. The same trap applies to _searchBox: never give it an initial Text in its own
        // initializer above for the same reason (TextChanged -> ApplyFilter -> null _list).
        _showCombo.SelectedIndexChanged += (_, _) => { if (!_suppressFilter) ApplyFilter(); };

        _list = new CheckedListBox
        {
            Location = new Point(10, 80),
            Size = new Size(530, 330),
            TabIndex = 5,
            // Placeholder only — ApplyFilter rewrites this with the live filter and count
            // before the dialog is ever shown. Kept non-empty so the list is never nameless.
            AccessibleName = "All variables",
            CheckOnClick = true
        };
        _list.ItemCheck += OnItemCheck;
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedIndex >= 0) _lastIndexByForm[GetType()] = _list.SelectedIndex;
        };

        Controls.Add(instructions);
        Controls.Add(searchLabel);
        Controls.Add(_searchBox);
        Controls.Add(showLabel);
        Controls.Add(_showCombo);
        Controls.Add(_list);
    }

    /// <summary>Maps the Show combo's selected index to a filter mode. Positionally coupled to
    /// the exact item order seeded in this file's InitializeComponent —
    /// <c>_showCombo.Items.AddRange(new object[] { "All", "Muted", "Unmuted" })</c> — where index
    /// 0 = All, 1 = Muted, 2 = Unmuted. Reordering or inserting an item there silently changes
    /// what this switch means; keep the two in sync.</summary>
    private MonitorFilterMode SelectedMode => _showCombo.SelectedIndex switch
    {
        1 => MonitorFilterMode.Muted,
        2 => MonitorFilterMode.Unmuted,
        _ => MonitorFilterMode.All
    };

    /// <summary>
    /// Rebuilds the visible rows from the search text + Show filter, re-reading every check
    /// state from the persisted set.
    ///
    /// Called on THREE events only: form open, search text changed, Show changed. NEVER from
    /// OnItemCheck — rebuilding on a tick would drop the row out from under the caret in
    /// Muted view and slide the next variable into its place, so the screen reader reads a
    /// name the pilot did not act on and a second Space press mutes the wrong variable.
    /// </summary>
    private void ApplyFilter()
    {
        // Snapshot into a HashSet for the two scans below (filter, then check state):
        // DisabledVariables is a List<string>, so ICollection.Contains is O(n) and a 400-row
        // PMDG rebuild would pay it 800 times per keystroke. NOT the UserSettings ...Set
        // sidecar — that is rebuilt on Save and lags a mutation, which is the whole reason
        // DisabledVariables hands out the live List.
        var disabled = new HashSet<string>(DisabledVariables, StringComparer.Ordinal);
        _visibleRows = MonitorVariableFilter.Apply(_allRows, _searchBox.Text, SelectedMode, disabled);

        _suppressItemCheck = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var row in _visibleRows)
            {
                int index = _list.Items.Add(row.Label);
                _list.SetItemChecked(index, !disabled.Contains(row.Key));   // checked = announcing
            }
        }
        finally
        {
            _list.EndUpdate();
            _suppressItemCheck = false;
        }

        // The active filter AND the result count ride on the list's accessible name — so
        // tabbing in speaks "Muted variables, 12 of 300" with no extra tab stop and no
        // app-generated speech talking over the pilot's typing. The filter has to be in the
        // NAME, not just the count: a fixed prefix made a Show change inaudible.
        _list.AccessibleName =
            MonitorVariableFilter.DescribeList(SelectedMode, _visibleRows.Count, _allRows.Count);

        // Land the caret on a row. Items.Clear() drops SelectedIndex to -1 and NOTHING puts it
        // back — not adding items, not the list receiving focus (measured, not assumed). With
        // no current row the screen reader announces the list with no item and the first Space
        // press does nothing, because a CheckedListBox toggles the item at SelectedIndex. That
        // is the same -1 the first-open path already had to fix; it applies to every search
        // keystroke and every Show change too.
        //
        // This write goes through SelectedIndexChanged into _lastIndexByForm, which is exactly
        // why ShowForm reads the remembered row BEFORE calling ApplyFilter.
        if (_list.Items.Count > 0 && _list.SelectedIndex < 0) _list.SelectedIndex = 0;
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppressItemCheck) return;
        // e.Index indexes the VISIBLE rows, which diverge from _allRows the moment a filter
        // is applied.
        if (e.Index < 0 || e.Index >= _visibleRows.Count) return;

        string key = _visibleRows[e.Index].Key;
        var disabled = DisabledVariables;

        if (e.NewValue == CheckState.Checked)
            disabled.Remove(key);
        else if (!disabled.Contains(key))
            disabled.Add(key);

        SettingsManager.Save();
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();

        // Read the remembered row FIRST. ApplyFilter selects row 0 when a rebuild leaves the
        // list with no selection, and that write lands in _lastIndexByForm through
        // SelectedIndexChanged — so reading afterwards would see 0 every time and the pilot's
        // last position would be silently lost on every open.
        // Default to 0, not "no selection": every one of the six forms this base replaced
        // restored unconditionally from a static that started at 0, so the FIRST open of a
        // dialog selected row 0.
        int last = _lastIndexByForm.TryGetValue(GetType(), out int v) ? v : 0;

        // Open clean, every time: no remembered search text, no remembered filter. A dialog
        // that reopened into "Muted" would show a fraction of the list for a reason the pilot
        // cannot see, and read as lost variables.
        //
        // Both resets are made with filtering suppressed so the rebuild happens ONCE below,
        // rather than up to three times (TextChanged, then SelectedIndexChanged, then the
        // unconditional call) on a list that can carry 400 rows.
        _suppressFilter = true;
        try
        {
            _searchBox.Text = string.Empty;
            _showCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilter = false;
        }

        // This is also what re-reads check states from the CURRENT persisted set: the form is
        // cached by MainForm and reused, so a populate that ran only at construction would show
        // stale checkboxes after any later change to the disabled set.
        ApplyFilter();

        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;

        if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Min(last, _list.Items.Count - 1);

        // Focus the search box: with 300+ rows, typing three letters beats arrowing.
        _searchBox.Focus();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Escape closes the dialog — except while the Show dropdown is open, where it belongs
        // to the dropdown. ProcessDialogKey runs before the combo ever sees the key, so without
        // this exemption backing out of the dropdown would close the whole dialog instead.
        if (keyData == Keys.Escape && !_showCombo.DroppedDown) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }
}
