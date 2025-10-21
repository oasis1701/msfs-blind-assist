using FBWBA.Accessibility;

namespace FBWBA.Forms;
public partial class ChecklistForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Panel scrollPanel = null!;
    private List<ListView> checklistViews = new List<ListView>();
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow;

    // Static dictionary to persist checkbox states across show/hide cycles
    private static Dictionary<string, bool> checkboxStates = new Dictionary<string, bool>();

    // Static fields to persist focus position across show/hide cycles
    private static int lastFocusedListViewIndex = 0;
    private static int lastSelectedItemIndex = 0;

    // Checklist category names
    private readonly List<string> checklistCategories = new List<string>
    {
        "Preflight Checklist",
        "Before Start Checklist",
        "After Start Checklist",
        "Taxi Checklist",
        "Before Takeoff Checklist",
        "Takeoff Procedure",
        "Climbing Checks",
        "Descent Checklist",
        "Approach and Landing Checklist",
        "On Ground",
        "Shutdown and Secure Aircraft"
    };

    public ChecklistForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;
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
            var targetListView = checklistViews[viewIndex];

            if (targetListView.Items.Count > 0)
            {
                int itemIndex = Math.Min(lastSelectedItemIndex, targetListView.Items.Count - 1);
                targetListView.Items[itemIndex].Selected = true;
                targetListView.Items[itemIndex].Focused = true;
                targetListView.EnsureVisible(itemIndex);
            }

            targetListView.Focus();
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

        // Create scrollable panel to contain all ListViews
        scrollPanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(565, 540),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true
        };

        int yPosition = 10;
        int tabIndex = 0;

        // Create a ListView for each checklist category
        foreach (var category in checklistCategories)
        {
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

            // Create ListView for this category
            var listView = new ListView
            {
                Location = new Point(10, yPosition),
                Size = new Size(520, 150),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.None,
                TabIndex = tabIndex++,
                AccessibleName = category,
                Tag = category // Store category name for checkbox persistence
            };

            // Add a single column that takes up the full width
            listView.Columns.Add("Item", 495);

            // Event handlers
            listView.ItemChecked += ListView_ItemChecked;
            listView.KeyDown += ListView_KeyDown;
            listView.Enter += ListView_Enter;
            listView.SelectedIndexChanged += ListView_SelectedIndexChanged;

            scrollPanel.Controls.Add(listView);
            checklistViews.Add(listView);

            yPosition += 160;
        }

        Controls.Add(scrollPanel);
    }

    private void SetupAccessibility()
    {
        // Handle form closing to hide instead of dispose
        FormClosing += (sender, e) =>
        {
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

    private void PopulateChecklist()
    {
        // Define checklist items for each category
        var checklistItems = new Dictionary<string, List<string>>
        {
            ["Preflight Checklist"] = new List<string>
            {
                "Batteries on",
                "APU on",
                "MCDU opened in browser to check status",
                "APU bleed air on",
                "Crew Oxygen Supply on",
                "IRS 1,2,3 set to nav",
                "Nav and logo lights on",
                "EFB Flight imported",
                "EFB Ground OPS passengers boarded",
                "EFB Ground OPS refueling completed",
                "Seatbelts sign on",
                "No smoking auto",
                "Emergency lights, auto/armed",
                "Transponder mode auto",
                "Contact ATC for clearance and set squawk",
                "Left and right side Barometer knobs pushed and set",
                "Initial climb set through FCU",
                "MCDU Flight Plan received through AOC menu",
                "MCDU flight plan requested through init",
                "MCDU departure Runway and sid selected",
                "MCDU arrival runway and star selected, not recommended for online",
                "MCDU FP page check, flight discontinuitys checked and cleared if required",
                "MCDU Init fuel pred page completed",
                "MCDU perf takeoff page completed, v1, 140, vr, 145, v2, 155, flaps as required"
            },
            ["Before Start Checklist"] = new List<string>
            {
                "Beacon light on",
                "Strobes auto",
                "Fuel pumps all on",
                "Set eng selector to ign",
                "Eng2 master on",
                "Eng1 master on",
                "Set eng mode selector normal"
            },
            ["After Start Checklist"] = new List<string>
            {
                "Flaps set",
                "Spoilers armed",
                "APU bleed off",
                "APU master off",
                "Anti ice, as required",
                "RW Turn off lights, on",
                "Nose light, taxi",
                "PREDICTIVE WINDSHEAR SYSTEM - Auto",
                "Autobrakes set",
                "Check and set FCU controls, hdg, speed, altitude"
            },
            ["Taxi Checklist"] = new List<string>
            {
                "Contact ATC, ask for teleport if flying online",
                "Use input mode > shift+r to position aircraft on runway",
                "Confirm aligned on runway by checking heading output mode > H"
            },
            ["Before Takeoff Checklist"] = new List<string>
            {
                "Transponder Ident",
                "TCAS TA/RA",
                "Landing lights on",
                "Nose Light T O",
                "Parking brakes released",
                "T.O. config test button pressed, no audible alerts",
                "Departure clearance received"
            },
            ["Takeoff Procedure"] = new List<string>
            {
                "Apply Flex/MCT or TO/GA power",
                "Monitor ias with output mode > w",
                "Rotate at v1 speed",
                "Gear up",
                "Autopilot 1 on with input mode > shift+a as soon as possible",
                "Pull throttle back to CLB position",
                "Flaps up",
                "Spoilers disarmed",
                "Communicate with ATC, Adjust FCU as required"
            },
            ["Climbing Checks"] = new List<string>
            {
                "MCDU perf on climb mode check",
                "Check MCDU FP page",
                "Landing lights off at 10000",
                "both Baro knobs pulled to STD position at correct altitude",
                "Seatbelts sign off"
            },
            ["Descent Checklist"] = new List<string>
            {
                "FCU alt set, knob pushed or pulled as required",
                "MCDU set arrival runway and star per ATC instructions if not set before",
                "MCDU RadNav page check for ILS freq and course, most times automatically set by airbus",
                "Select destination runway through input mode > shift+d",
                "Optional, check runway threshold distance through output mode > control+I",
                "Follow ATC instructions or FMC managed path down into approach",
                "Activate approach phase in MCDU perf page when 40 miles out",
                "Landing lights on at 10000",
                "Seatbelts signs on"
            },
            ["Approach and Landing Checklist"] = new List<string>
            {
                "Check Speed, FCU should manage, adjust if approach phase not properly engaged or ATC requests",
                "Flaps 1",
                "APPR mode on to establish",
                "Auto pilot 2 on after establish",
                "Flaps 2 on final",
                "Gear down",
                "Spoilers armed",
                "Autobrakes set",
                "RWY TURN OFF lights on",
                "Nose light set to T O",
                "Flaps 3",
                "Flaps full",
                "Land mode received from FMA"
            },
            ["On Ground"] = new List<string>
            {
                "Apply reverse thrust when required",
                "Parking breaks set",
                "Flaps up",
                "AP red disconnect button pressed",
                "Teleport to gate through input mode > shift+g"
            },
            ["Shutdown and Secure Aircraft"] = new List<string>
            {
                "Complete shutdown procedures"
            }
        };

        // Populate each ListView with its items
        foreach (var listView in checklistViews)
        {
            if (listView.Tag is string category && checklistItems.ContainsKey(category))
            {
                listView.BeginUpdate();

                foreach (var itemText in checklistItems[category])
                {
                    var listItem = new ListViewItem(itemText);

                    // Restore checkbox state if it exists
                    string key = GetItemKey(category, itemText);
                    if (checkboxStates.ContainsKey(key))
                    {
                        listItem.Checked = checkboxStates[key];
                    }

                    listView.Items.Add(listItem);
                }

                listView.EndUpdate();
            }
        }
    }

    private string GetItemKey(string category, string itemText)
    {
        return $"{category}|{itemText}";
    }

    private void ListView_ItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (sender is ListView listView && listView.Tag is string category)
        {
            string key = GetItemKey(category, e.Item.Text);
            checkboxStates[key] = e.Item.Checked;
        }
    }

    private void ListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ListView_Enter(object? sender, EventArgs e)
    {
        if (sender is ListView listView)
        {
            lastFocusedListViewIndex = checklistViews.IndexOf(listView);
        }
    }

    private void ListView_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (sender is ListView listView && listView.SelectedIndices.Count > 0)
        {
            lastSelectedItemIndex = listView.SelectedIndices[0];
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
