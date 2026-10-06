using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.DA40;

/// <summary>
/// The G1000's whole page tree in one tree, with a direct way to open any of it.
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

    }

    private readonly Controls.NativeAccessibleTreeView _tree;
    private readonly ScreenReaderAnnouncer _announcer;

    /// <summary>The page the pilot chose, or null if they cancelled or chose a stub.</summary>
    public string? SelectedKey { get; private set; }

    public CowsDA40PageJumpForm(List<PageEntry> entries, ScreenReaderAnnouncer announcer,
        string currentPage)
    {
        _announcer = announcer;

        Text = "Go to G1000 page";
        Size = new Size(520, 480);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        // ⚠️ A TREE, NOT A LIST, SO THE GROUP IS SAID ONCE. As a list every entry read its
        // group first - "MAP - Navigation Map", "MAP - Traffic Map", "MAP - Weather Map" - the
        // same word on every arrow press (reported from the cockpit with the checklist's and
        // the menus' repeated names). The groups are the branches, as on the knob.
        _tree = new Controls.NativeAccessibleTreeView
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            TabIndex = 0,
            HideSelection = false,
            AccessibleName = "G1000 pages",
            AccessibleDescription =
                "Choose a page and press Enter. The page groups are the branches. Pages " +
                "marked not in this G1000 are names the display draws for pages it does not " +
                "have; the knob cannot reach them either. Escape cancels."
        };

        TreeNode? start = null;
        foreach (var group in GroupInOrder(entries))
        {
            var branch = new TreeNode(group.Key);
            foreach (var e in group.Value)
            {
                var node = new TreeNode(PageText(e)) { Tag = e };
                branch.Nodes.Add(node);
                // Start on the page the pilot is already looking at, so the tree opens where
                // they are rather than at the top - the same courtesy the real selector gives.
                if (start == null && e.Available &&
                    string.Equals(e.Key, currentPage, StringComparison.Ordinal))
                    start = node;
            }
            _tree.Nodes.Add(branch);
        }
        _tree.ExpandAll();
        _tree.SelectedNode = start ?? (_tree.Nodes.Count > 0 ? _tree.Nodes[0] : null);

        _tree.NodeMouseDoubleClick += (s, e) => Accept();

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
        Controls.Add(_tree);
        Controls.Add(bottom);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        Load += (s, e) => _tree.Focus();
    }

    /// <summary>A page's line under its group: the name, and whether the G1000 has it.</summary>
    internal static string PageText(PageEntry e) =>
        e.Name + (e.Available ? "" : "   (not in this G1000)");

    /// <summary>The groups in the order the G1000 lists them, each with its pages in order.</summary>
    internal static List<KeyValuePair<string, List<PageEntry>>> GroupInOrder(List<PageEntry> entries)
    {
        var groups = new List<KeyValuePair<string, List<PageEntry>>>();
        foreach (var e in entries)
        {
            int at = groups.FindIndex(g => g.Key == e.Group);
            if (at < 0) { groups.Add(new(e.Group, new List<PageEntry>())); at = groups.Count - 1; }
            groups[at].Value.Add(e);
        }
        return groups;
    }

    private void Accept()
    {
        // Enter on a GROUP opens or closes it, as in any tree; only a page is a choice.
        if (_tree.SelectedNode?.Tag is not PageEntry chosen)
        {
            var branch = _tree.SelectedNode;
            if (branch != null) { if (branch.IsExpanded) branch.Collapse(); else branch.Expand(); }
            return;
        }

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
