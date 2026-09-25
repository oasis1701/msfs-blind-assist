using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;
public partial class ChecklistForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Panel scrollPanel = null!;
    private List<CheckedListBox> checklistViews = new List<CheckedListBox>();
    private readonly string aircraftCode;
    private IntPtr previousWindow;

    // Static dictionary to persist checkbox states across show/hide cycles
    private static Dictionary<string, bool> checkboxStates = new Dictionary<string, bool>();

    // Static fields to persist focus position across show/hide cycles
    private static int lastFocusedListViewIndex = 0;
    private static int lastSelectedItemIndex = 0;

    public ChecklistForm(ScreenReaderAnnouncer announcer, string aircraftCode)
    {
        this.aircraftCode = aircraftCode;
        InitializeComponent();
        SetupAccessibility();
        PopulateChecklist();
    }

    public void ShowForm()
    {
        // Capture the current foreground window before showing
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false; // Flash to bring to front

        // Restore focus to last position
        if (checklistViews.Count > 0)
        {
            int viewIndex = Math.Min(lastFocusedListViewIndex, checklistViews.Count - 1);
            var targetControl = checklistViews[viewIndex];

            if (targetControl.Items.Count > 0)
            {
                int itemIndex = Math.Min(lastSelectedItemIndex, targetControl.Items.Count - 1);
                targetControl.SelectedIndex = itemIndex;
            }

            targetControl.Focus();
        }
    }

    private void InitializeComponent()
    {
        Text = "Checklist";
        Size = new Size(600, 600);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;

        // Create scrollable panel to contain all CheckedListBoxes (populated later)
        scrollPanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(565, 540),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true
        };

        Controls.Add(scrollPanel);
    }

    private void SetupAccessibility()
    {
        // Handle form closing to hide instead of dispose
        FormClosing += (sender, e) =>
        {
            // Let real app/OS shutdown through: Application.Exit raises FormClosing on
            // every open form (hidden included) and ABORTS the whole exit if any form
            // cancels — an unconditional cancel here left the auto-updater stalled
            // against a still-running exe. Everything else still hides.
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }

            // Cancel the close and hide instead
            e.Cancel = true;
            Hide();

            // Restore focus to the previous window (likely the simulator)
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        };
    }

    private string GetChecklistText()
    {
        // ⚠️ THE AIRCRAFT'S OWN CHECKLIST FIRST. Every MSFS aeroplane may ship one as plain
        // XML inside its package, and it is better than anything hand-transcribed here: it
        // is the vendor's, it is complete, and it follows the aircraft through updates. The
        // COWS DA40's also carries a "Tips and help" page with the warm-up times, the
        // rotate speeds, the traffic-pattern power settings and how to reset failures and
        // charge the batteries - operating knowledge this project spent days deriving by
        // probe while it sat in the package unread.
        string? native = Services.NativeChecklistReader.Render(aircraftCode);
        if (!string.IsNullOrWhiteSpace(native)) return native;

        // Map aircraft codes to checklist filenames
        var filenameMap = new Dictionary<string, string>
        {
            { "A320", "FBW_A320_Checklist.txt" },
            { "HW_A330", "FBW_A330_Checklist.txt" },
            { "FENIX_A320CEO", "Fenix_A320_Checklist.txt" },
            { "FBW_A380", "FBW_A380_Checklist.txt" },
            { "IFLY_737MAX8", "iFly_737MAX8_Checklist.txt" }
        };

        // ⚠️ NO AIRBUS FALLBACK. An aeroplane with no checklist of its own used to be shown
        // the A320's, so the DA40's checklist window was listing an Airbus procedure - which
        // is worse than an empty window, because it looks authoritative and is wrong about
        // an aeroplane the pilot is flying.
        if (!filenameMap.TryGetValue(aircraftCode, out string? filename))
        {
            return "[No checklist for this aircraft]\n" +
                   "This aeroplane ships no checklist of its own and MSFSBA carries none " +
                   "for it. Use the aircraft's own documentation.";
        }

        // Construct file path
        string appPath = AppDomain.CurrentDomain.BaseDirectory;
        string filePath = Path.Combine(appPath, "Checklists", filename);

        try
        {
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
            else
            {
                return $"[Error]\nChecklist file not found: {filePath}";
            }
        }
        catch (Exception ex)
        {
            return $"[Error]\nError loading checklist: {ex.Message}";
        }
    }

    private Dictionary<string, List<string>> ParseChecklistText(string text)
    {
        var checklistItems = new Dictionary<string, List<string>>();
        string? currentCategory = null;

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Check if line is a category (starts with [ and ends with ])
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                currentCategory = trimmedLine.Substring(1, trimmedLine.Length - 2);
                checklistItems[currentCategory] = new List<string>();
            }
            else if (currentCategory != null)
            {
                // Add item to current category
                checklistItems[currentCategory].Add(trimmedLine);
            }
        }

        return checklistItems;
    }

    private void PopulateChecklist()
    {
        // Load and parse checklist from aircraft-specific text file
        string checklistText = GetChecklistText();
        var checklistItems = ParseChecklistText(checklistText);

        // Clear existing controls and views
        scrollPanel.Controls.Clear();
        checklistViews.Clear();

        int yPosition = 10;
        int tabIndex = 0;

        // Create a CheckedListBox for each category from the file
        foreach (var categoryEntry in checklistItems)
        {
            string category = categoryEntry.Key;
            var items = categoryEntry.Value;

            // Create label for the category
            var label = new Label
            {
                Text = category,
                Location = new Point(10, yPosition),
                Size = new Size(520, 20),
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold),
                AccessibleName = category
            };
            scrollPanel.Controls.Add(label);
            yPosition += 25;

            // Create CheckedListBox for this category
            var checkedListBox = new CheckedListBox
            {
                Location = new Point(10, yPosition),
                Size = new Size(520, 150),
                TabIndex = tabIndex++,
                AccessibleName = category,
                Tag = category, // Store category name for checkbox persistence
                CheckOnClick = true // Toggle checkbox on single click
            };

            // Event handlers
            checkedListBox.ItemCheck += CheckedListBox_ItemCheck;
            checkedListBox.KeyDown += CheckedListBox_KeyDown;
            checkedListBox.Enter += CheckedListBox_Enter;
            checkedListBox.SelectedIndexChanged += CheckedListBox_SelectedIndexChanged;

            // Populate items for this category
            checkedListBox.BeginUpdate();
            foreach (var itemText in items)
            {
                int index = checkedListBox.Items.Add(itemText);

                // Restore checkbox state if it exists
                string key = GetItemKey(category, itemText);
                if (checkboxStates.ContainsKey(key))
                {
                    checkedListBox.SetItemChecked(index, checkboxStates[key]);
                }
            }
            checkedListBox.EndUpdate();

            scrollPanel.Controls.Add(checkedListBox);
            checklistViews.Add(checkedListBox);

            yPosition += 160;
        }
    }

    private string GetItemKey(string category, string itemText)
    {
        return $"{category}|{itemText}";
    }

    private void CheckedListBox_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (sender is CheckedListBox checkedListBox && checkedListBox.Tag is string category)
        {
            string itemText = checkedListBox.Items[e.Index].ToString() ?? "";
            string key = GetItemKey(category, itemText);
            checkboxStates[key] = (e.NewValue == CheckState.Checked);
        }
    }

    private void CheckedListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void CheckedListBox_Enter(object? sender, EventArgs e)
    {
        if (sender is CheckedListBox checkedListBox)
        {
            lastFocusedListViewIndex = checklistViews.IndexOf(checkedListBox);
        }
    }

    private void CheckedListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (sender is CheckedListBox checkedListBox && checkedListBox.SelectedIndex >= 0)
        {
            lastSelectedItemIndex = checkedListBox.SelectedIndex;
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

}
