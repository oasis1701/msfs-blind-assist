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
        // The aircraft's own checklist first, MSFSBA's bundled file second, never another
        // aircraft's — see ChecklistContent, which owns the order and the file map.
        => Services.ChecklistContent.Load(
            aircraftCode,
            Services.NativeChecklistReader.Render,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Checklists"));

    private void PopulateChecklist()
    {
        // Load and parse checklist from aircraft-specific text file
        var sections = Services.ChecklistContent.Parse(GetChecklistText());

        // Clear existing controls and views
        scrollPanel.Controls.Clear();
        checklistViews.Clear();

        int yPosition = 10;
        int tabIndex = 0;

        // Create a CheckedListBox for each category, in the checklist's own order
        foreach (var section in sections)
        {
            string category = section.Title;
            var items = section.Items;

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
        => Services.ChecklistContent.ItemKey(aircraftCode, category, itemText);

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
