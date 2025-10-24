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
        "Preflight Check",
        "Before Start Check",
        "After Start Check",
        "Taxi Check",
        "Before Takeoff Check",
        "Takeoff Procedure",
        "Climbing Checks",
        "Descent Check",
        "Approach and Landing Check",
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

            scrollPanel.Controls.Add(checkedListBox);
            checklistViews.Add(checkedListBox);

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
            ["Preflight Check"] = new List<string>
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
            ["Before Start Check"] = new List<string>
            {
                "Beacon light on",
                "Strobes auto",
                "Fuel pumps all on",
                "Set eng selector to ign",
                "Eng2 master on",
                "Eng1 master on",
                "Set eng mode selector normal"
            },
            ["After Start Check"] = new List<string>
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
            ["Taxi Check"] = new List<string>
            {
                "Contact ATC, ask for teleport if flying online",
                "Use input mode > shift+r to position aircraft on runway",
                "Confirm aligned on runway by checking heading output mode > H"
            },
            ["Before Takeoff Check"] = new List<string>
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
            ["Descent Check"] = new List<string>
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
            ["Approach and Landing Check"] = new List<string>
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

        // Populate each CheckedListBox with its items
        foreach (var checkedListBox in checklistViews)
        {
            if (checkedListBox.Tag is string category && checklistItems.ContainsKey(category))
            {
                checkedListBox.BeginUpdate();

                foreach (var itemText in checklistItems[category])
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
            }
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
