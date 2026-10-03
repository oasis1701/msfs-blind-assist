using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.DA40;

/// <summary>
/// The G1000's whole page tree in one list, with a direct way to open any of it.
///
/// WHY THIS EXISTS. Reaching a page on a real G1000 means holding the cursor off, turning
/// the small FMS knob to step through five page GROUPS, then the large one to step through
/// up to nine pages inside the group, and waiting for a timer to commit — and the selector
/// closes itself if you pause. Doing that by ear, with nine names to count through and no
/// way to see where you are, is how the Aux group came to be reported as unreachable.
///
/// So this is the same navigation, listed. It is NOT a shortcut past the aeroplane: opening
/// a page here makes the identical <c>viewService.open</c> call the page selector makes when
/// its timer fires, so the page arrives in exactly the state the knob would have left it in,
/// and the knob still works unchanged for anyone who prefers it.
///
/// ⚠️ IT LISTS THE PAGES THE G1000 DOES NOT HAVE, and says so, rather than hiding them.
/// Seven of the nine Aux pages — Trip Planning, Utility, GPS Status, XM Radio, System
/// Status, Connext Setup and Databases — are names the stock Working Title G1000 draws for
/// pages it never implemented, and a sighted pilot turning the knob lands on them and gets
/// nothing too. Hiding them would leave a blind pilot unable to tell a page the aeroplane
/// never built from a page this reader is failing to read, which is the exact confusion
/// that made the Aux group feel broken.
/// </summary>
public sealed class CowsDA40PageJumpForm : Form
{
    /// <summary>One row of the G1000's page table. An empty Key means no page behind it.</summary>
    public sealed record PageEntry(string Group, string Name, string Key)
    {
        public bool Available => Key.Length > 0;

        public string Display => Group + " - " + Name +
            (Available ? "" : "   (not in this G1000)");
    }

    private readonly ListBox _list;
    private readonly List<PageEntry> _entries;
    private readonly ScreenReaderAnnouncer _announcer;

    /// <summary>The page the pilot chose, or null if they cancelled or chose a stub.</summary>
    public string? SelectedKey { get; private set; }

    public CowsDA40PageJumpForm(List<PageEntry> entries, ScreenReaderAnnouncer announcer,
        string currentPage)
    {
        _entries = entries;
        _announcer = announcer;

        Text = "Go to G1000 page";
        Size = new Size(520, 480);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            TabIndex = 0,
            AccessibleName = "G1000 pages",
            AccessibleDescription =
                "Choose a page and press Enter. Pages marked not in this G1000 are names " +
                "the display draws for pages it does not have; the knob cannot reach them " +
                "either. Escape cancels."
        };

        foreach (var e in entries) _list.Items.Add(e.Display);

        // Start on the page the pilot is already looking at, so the list opens where they
        // are rather than at the top - the same courtesy the real selector gives.
        int at = entries.FindIndex(e => e.Available &&
            string.Equals(e.Key, currentPage, StringComparison.Ordinal));
        _list.SelectedIndex = at >= 0 ? at : 0;

        _list.DoubleClick += (s, e) => Accept();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };

        var okButton = new Button
        {
            Text = "&Go",
            Location = new Point(310, 8),
            Size = new Size(90, 30),
            TabIndex = 1,
            AccessibleName = "Go to page"
        };
        okButton.Click += (s, e) => Accept();

        var cancelButton = new Button
        {
            Text = "&Cancel",
            Location = new Point(408, 8),
            Size = new Size(85, 30),
            TabIndex = 2,
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel"
        };

        bottom.Controls.AddRange(new Control[] { okButton, cancelButton });
        Controls.Add(_list);
        Controls.Add(bottom);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        Load += (s, e) => _list.Focus();
    }

    private void Accept()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _entries.Count) return;

        var chosen = _entries[_list.SelectedIndex];

        // A STUB IS NOT AN ERROR TO SHOUT ABOUT, and it is not a reason to close either -
        // the pilot picked a name off a list the aeroplane itself printed. Say what it is
        // and leave them in the list to pick something else.
        if (!chosen.Available)
        {
            _announcer.AnnounceImmediate(chosen.Name +
                " is a page this G1000 does not have. The knob cannot open it either.");
            return;
        }

        SelectedKey = chosen.Key;
        DialogResult = DialogResult.OK;
        Close();
    }
}
