using FBWBA.Accessibility;

namespace FBWBA.Forms;
public partial class ChecklistForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TreeView checklistTreeView = null!;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow;

    // Static dictionary to persist checkbox states across show/hide cycles
    private static Dictionary<string, bool> checkboxStates = new Dictionary<string, bool>();

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
        checklistTreeView.Focus();
    }

    private void InitializeComponent()
    {
        Text = "Flight Checklist";
        Size = new Size(500, 600);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;

        // TreeView
        checklistTreeView = new TreeView
        {
            Location = new Point(10, 10),
            Size = new Size(465, 540),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            CheckBoxes = true,
            AccessibleName = "Checklist",
            AccessibleDescription = ""
        };

        checklistTreeView.AfterCheck += ChecklistTreeView_AfterCheck;
        checklistTreeView.KeyDown += ChecklistTreeView_KeyDown;

        Controls.Add(checklistTreeView);
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
        // Temporarily disable AfterCheck event to prevent stack overflow during population
        checklistTreeView.AfterCheck -= ChecklistTreeView_AfterCheck;

        checklistTreeView.BeginUpdate();

        // Preflight checklist
        var preflightNode = CreateParentNode("Preflight Checklist");
        AddChecklistItem(preflightNode, "Batteries on");
        AddChecklistItem(preflightNode, "APU on");
        AddChecklistItem(preflightNode, "MCDU opened in browser to check status");
        AddChecklistItem(preflightNode, "APU bleed air on");
        AddChecklistItem(preflightNode, "Crew Oxygen Supply on");
        AddChecklistItem(preflightNode, "IRS 1,2,3 set to nav");
        AddChecklistItem(preflightNode, "Nav and logo lights on");
        AddChecklistItem(preflightNode, "EFB Flight imported");
        AddChecklistItem(preflightNode, "EFB Ground OPS passengers boarded");
        AddChecklistItem(preflightNode, "EFB Ground OPS refueling completed");
        AddChecklistItem(preflightNode, "Seatbelts sign on");
        AddChecklistItem(preflightNode, "No smoking auto");
        AddChecklistItem(preflightNode, "Emergency lights, auto/armed");
        AddChecklistItem(preflightNode, "Transponder mode auto");
        AddChecklistItem(preflightNode, "Contact ATC for clearance and set squawk");
        AddChecklistItem(preflightNode, "Left and right side Barometer knobs pushed and set");
        AddChecklistItem(preflightNode, "Initial climb set through FCU");
        AddChecklistItem(preflightNode, "MCDU Flight Plan received through AOC menu");
        AddChecklistItem(preflightNode, "MCDU flight plan requested through init");
        AddChecklistItem(preflightNode, "MCDU departure Runway and sid selected");
        AddChecklistItem(preflightNode, "MCDU arrival runway and star selected, not recommended for online");
        AddChecklistItem(preflightNode, "MCDU FP page check, flight discontinuitys checked and cleared if required");
        AddChecklistItem(preflightNode, "MCDU Init fuel pred page completed");
        AddChecklistItem(preflightNode, "MCDU perf takeoff page completed, v1, 140, vr, 145, v2, 155, flaps as required");

        // Before start checklist
        var beforeStartNode = CreateParentNode("Before Start Checklist");
        AddChecklistItem(beforeStartNode, "Beacon light on");
        AddChecklistItem(beforeStartNode, "Strobes auto");
        AddChecklistItem(beforeStartNode, "Fuel pumps all on");
        AddChecklistItem(beforeStartNode, "Set eng selector to ign");
        AddChecklistItem(beforeStartNode, "Eng2 master on");
        AddChecklistItem(beforeStartNode, "Eng1 master on");
        AddChecklistItem(beforeStartNode, "Set eng mode selector normal");

        // After start checklist
        var afterStartNode = CreateParentNode("After Start Checklist");
        AddChecklistItem(afterStartNode, "Flaps set");
        AddChecklistItem(afterStartNode, "Spoilers armed");
        AddChecklistItem(afterStartNode, "APU bleed off");
        AddChecklistItem(afterStartNode, "APU master off");
        AddChecklistItem(afterStartNode, "Anti ice, as required");
        AddChecklistItem(afterStartNode, "RW Turn off lights, on");
        AddChecklistItem(afterStartNode, "Nose light, taxi");
        AddChecklistItem(afterStartNode, "PREDICTIVE WINDSHEAR SYSTEM - Auto");
        AddChecklistItem(afterStartNode, "Autobrakes set");
        AddChecklistItem(afterStartNode, "Check and set FCU controls, hdg, speed, altitude");

        // Taxi checklist
        var taxiNode = CreateParentNode("Taxi Checklist");
        AddChecklistItem(taxiNode, "Contact ATC, ask for teleport if flying online");
        AddChecklistItem(taxiNode, "Use input mode > shift+r to position aircraft on runway");
        AddChecklistItem(taxiNode, "Confirm aligned on runway by checking heading output mode > H");

        // Before takeoff checklist
        var beforeTakeoffNode = CreateParentNode("Before Takeoff Checklist");
        AddChecklistItem(beforeTakeoffNode, "Transponder Ident");
        AddChecklistItem(beforeTakeoffNode, "TCAS TA/RA");
        AddChecklistItem(beforeTakeoffNode, "Landing lights on");
        AddChecklistItem(beforeTakeoffNode, "Nose Light T O");
        AddChecklistItem(beforeTakeoffNode, "Parking brakes released");
        AddChecklistItem(beforeTakeoffNode, "T.O. config test button pressed, no audible alerts");
        AddChecklistItem(beforeTakeoffNode, "Departure clearance received");

        // Takeoff procedure
        var takeoffNode = CreateParentNode("Takeoff Procedure");
        AddChecklistItem(takeoffNode, "Apply Flex/MCT or TO/GA power");
        AddChecklistItem(takeoffNode, "Monitor ias with output mode > w");
        AddChecklistItem(takeoffNode, "Rotate at v1 speed");
        AddChecklistItem(takeoffNode, "Gear up");
        AddChecklistItem(takeoffNode, "Autopilot 1 on with input mode > shift+a as soon as possible");
        AddChecklistItem(takeoffNode, "Pull throttle back to CLB position");
        AddChecklistItem(takeoffNode, "Flaps up");
        AddChecklistItem(takeoffNode, "Spoilers disarmed");
        AddChecklistItem(takeoffNode, "Communicate with ATC, Adjust FCU as required");

        // Climbing checks
        var climbingNode = CreateParentNode("Climbing Checks");
        AddChecklistItem(climbingNode, "MCDU perf on climb mode check");
        AddChecklistItem(climbingNode, "Check MCDU FP page");
        AddChecklistItem(climbingNode, "Landing lights off at 10000");
        AddChecklistItem(climbingNode, "both Baro knobs pulled to STD position at correct altitude");
        AddChecklistItem(climbingNode, "Seatbelts sign off");

        // Descent checklist
        var descentNode = CreateParentNode("Descent Checklist");
        AddChecklistItem(descentNode, "FCU alt set, knob pushed or pulled as required");
        AddChecklistItem(descentNode, "MCDU set arrival runway and star per ATC instructions if not set before");
        AddChecklistItem(descentNode, "MCDU RadNav page check for ILS freq and course, most times automatically set by airbus");
        AddChecklistItem(descentNode, "Select destination runway through input mode > shift+d");
        AddChecklistItem(descentNode, "Optional, check runway threshold distance through output mode > control+I");
        AddChecklistItem(descentNode, "Follow ATC instructions or FMC managed path down into approach");
        AddChecklistItem(descentNode, "Activate approach phase in MCDU perf page when 40 miles out");
        AddChecklistItem(descentNode, "Landing lights on at 10000");
        AddChecklistItem(descentNode, "Seatbelts signs on");

        // Approach and landing checklist
        var approachNode = CreateParentNode("Approach and Landing Checklist");
        AddChecklistItem(approachNode, "Check Speed, FCU should manage, adjust if approach phase not properly engaged or ATC requests");
        AddChecklistItem(approachNode, "Flaps 1");
        AddChecklistItem(approachNode, "APPR mode on to establish");
        AddChecklistItem(approachNode, "Auto pilot 2 on after establish");
        AddChecklistItem(approachNode, "Flaps 2 on final");
        AddChecklistItem(approachNode, "Gear down");
        AddChecklistItem(approachNode, "Spoilers armed");
        AddChecklistItem(approachNode, "Autobrakes set");
        AddChecklistItem(approachNode, "RWY TURN OFF lights on");
        AddChecklistItem(approachNode, "Nose light set to T O");
        AddChecklistItem(approachNode, "Flaps 3");
        AddChecklistItem(approachNode, "Flaps full");
        AddChecklistItem(approachNode, "Land mode received from FMA");

        // On ground
        var onGroundNode = CreateParentNode("On Ground");
        AddChecklistItem(onGroundNode, "Apply reverse thrust when required");
        AddChecklistItem(onGroundNode, "Parking breaks set");
        AddChecklistItem(onGroundNode, "Flaps up");
        AddChecklistItem(onGroundNode, "AP red disconnect button pressed");
        AddChecklistItem(onGroundNode, "Teleport to gate through input mode > shift+g");

        // Shutdown
        var shutdownNode = CreateParentNode("Shutdown and Secure Aircraft");
        AddChecklistItem(shutdownNode, "Complete shutdown procedures");

        checklistTreeView.EndUpdate();

        // Re-enable AfterCheck event for user interaction
        checklistTreeView.AfterCheck += ChecklistTreeView_AfterCheck;
    }

    private TreeNode CreateParentNode(string text)
    {
        var node = new TreeNode(text);
        checklistTreeView.Nodes.Add(node);
        return node;
    }

    private void AddChecklistItem(TreeNode parent, string text)
    {
        var node = new TreeNode(text);
        parent.Nodes.Add(node);

        // Restore checkbox state if it exists
        string key = GetNodeKey(parent.Text, text);
        if (checkboxStates.ContainsKey(key))
        {
            node.Checked = checkboxStates[key];
        }
    }

    private string GetNodeKey(string parentText, string childText)
    {
        return $"{parentText}|{childText}";
    }

    private void ChecklistTreeView_AfterCheck(object? sender, TreeViewEventArgs e)
    {
        // Prevent checking parent nodes
        if (e.Node != null && e.Node.Nodes.Count > 0)
        {
            e.Node.Checked = false;
            return;
        }

        // Save checkbox state for child nodes
        if (e.Node?.Parent != null)
        {
            string key = GetNodeKey(e.Node.Parent.Text, e.Node.Text);
            checkboxStates[key] = e.Node.Checked;
        }
    }

    private void ChecklistTreeView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
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
