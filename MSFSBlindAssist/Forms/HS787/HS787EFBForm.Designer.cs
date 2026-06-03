namespace MSFSBlindAssist.Forms.HS787;

public partial class HS787EFBForm
{
    private Label statusLabel = null!;
    private Label pageTitleLabel = null!;
    private ListBox contentList = null!;
    private Panel buttonsPanel = null!;
    private Button refreshButton = null!;
    private Button groundServicesButton = null!;

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "787-9 EFB";
        this.Size = new Size(620, 600);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.KeyPreview = true;
        this.AccessibleName = "HorizonSim 787-9 EFB";

        int y = 10;

        // Connection status
        statusLabel = new Label
        {
            Text = "EFB Bridge Not Connected",
            Location = new Point(10, y),
            Size = new Size(590, 20),
            AccessibleName = "EFB connection status",
            TabStop = false
        };
        y += 26;

        // Current page title — shown prominently, announced when it changes
        pageTitleLabel = new Label
        {
            Text = "Page: —",
            Location = new Point(10, y),
            Size = new Size(590, 22),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            AccessibleName = "Current page",
            TabStop = false
        };
        y += 30;

        // Page content — ListBox so screen readers can navigate line-by-line with arrow keys
        var contentLabel = new Label
        {
            Text = "Page Content (Alt+P to focus):",
            Location = new Point(10, y),
            Size = new Size(590, 18),
            TabStop = false
        };
        y += 20;

        contentList = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(590, 185),
            Font = new Font("Consolas", 9.5f),
            SelectionMode = SelectionMode.One,
            HorizontalScrollbar = true,
            TabIndex = 0,
            AccessibleName = "EFB Page Content"
        };
        y += 193;

        // Buttons — dynamically generated, numbered 1-9 for keyboard shortcuts
        var buttonsLabel = new Label
        {
            Text = "Buttons (Tab/arrows to navigate, Space/Enter to press; Alt+1-9 for first 9; Alt+B to focus):",
            Location = new Point(10, y),
            Size = new Size(590, 18),
            TabStop = false
        };
        y += 22;

        buttonsPanel = new Panel
        {
            Location = new Point(10, y),
            Size = new Size(590, 230),
            AutoScroll = true,
            TabStop = false,
            TabIndex = 1,
            BorderStyle = BorderStyle.FixedSingle
        };
        y += 238;

        refreshButton = new Button
        {
            Text = "Refresh (Alt+R)",
            Location = new Point(10, y),
            Size = new Size(130, 28),
            TabIndex = 1000,
            AccessibleName = "Refresh EFB screen"
        };

        groundServicesButton = new Button
        {
            Text = "Ground Services (Alt+G)",
            Location = new Point(150, y),
            Size = new Size(190, 28),
            TabIndex = 1001,
            AccessibleName = "Navigate to Ground Services page"
        };

        this.Controls.AddRange(new Control[]
        {
            statusLabel, pageTitleLabel, contentLabel, contentList,
            buttonsLabel, buttonsPanel,
            refreshButton, groundServicesButton
        });

        this.ResumeLayout(false);
    }
}
