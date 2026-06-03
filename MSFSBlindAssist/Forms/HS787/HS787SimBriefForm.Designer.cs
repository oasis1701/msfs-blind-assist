namespace MSFSBlindAssist.Forms.HS787;

public partial class HS787SimBriefForm
{
    private Label statusLabel = null!;
    private Label pilotIdLabel = null!;
    private TextBox pilotIdBox = null!;
    private Button fetchButton = null!;
    private ListBox flightInfoList = null!;
    private Button loadFuelButton = null!;
    private Button openFmcButton = null!;

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "787-9 SimBrief & Fuel";
        this.Size = new Size(560, 460);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.KeyPreview = true;
        this.AccessibleName = "HorizonSim 787-9 SimBrief and Fuel";

        int y = 10;

        // ── Pilot ID row ──────────────────────────────────────────────────────

        pilotIdLabel = new Label
        {
            Text = "SimBrief Pilot ID:",
            Location = new Point(10, y + 4),
            Size = new Size(130, 20),
            TabStop = false
        };

        pilotIdBox = new TextBox
        {
            Location = new Point(145, y),
            Size = new Size(230, 24),
            TabIndex = 0,
            AccessibleName = "SimBrief Pilot ID"
        };

        fetchButton = new Button
        {
            Text = "Fetch (Alt+F)",
            Location = new Point(384, y - 1),
            Size = new Size(150, 28),
            TabIndex = 1,
            AccessibleName = "Fetch SimBrief flight plan"
        };
        y += 36;

        // ── Flight info list ──────────────────────────────────────────────────

        var flightInfoLabel = new Label
        {
            Text = "Flight Details:",
            Location = new Point(10, y),
            Size = new Size(530, 18),
            TabStop = false
        };
        y += 20;

        flightInfoList = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(524, 230),
            TabIndex = 2,
            SelectionMode = SelectionMode.One,
            AccessibleName = "Flight Details"
        };
        y += 238;

        // ── Action buttons ────────────────────────────────────────────────────

        loadFuelButton = new Button
        {
            Text = "Load Fuel (Alt+L)",
            Location = new Point(10, y),
            Size = new Size(180, 30),
            TabIndex = 3,
            Enabled = false,
            AccessibleName = "Load fuel into simulator"
        };

        openFmcButton = new Button
        {
            Text = "Open FMC Init (Alt+I)",
            Location = new Point(200, y),
            Size = new Size(180, 30),
            TabIndex = 4,
            AccessibleName = "Open FMC Init/Ref page"
        };
        y += 38;

        // ── Status label ──────────────────────────────────────────────────────

        statusLabel = new Label
        {
            Text = "Enter your SimBrief Pilot ID and press Fetch.",
            Location = new Point(10, y),
            Size = new Size(524, 20),
            TabStop = false,
            AccessibleName = "Status"
        };

        this.Controls.AddRange(new Control[]
        {
            pilotIdLabel, pilotIdBox, fetchButton,
            flightInfoLabel, flightInfoList,
            loadFuelButton, openFmcButton,
            statusLabel
        });

        this.ResumeLayout(false);
    }
}
