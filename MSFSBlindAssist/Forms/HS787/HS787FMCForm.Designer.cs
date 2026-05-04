namespace MSFSBlindAssist.Forms.HS787;

public partial class HS787FMCForm
{
    private Label statusLabel = null!;
    private ListBox fmcDisplay = null!;
    private TextBox scratchpadInput = null!;

    // Page buttons
    private Button btnInitRef  = null!;
    private Button btnRte      = null!;
    private Button btnDepArr   = null!;
    private Button btnAltn     = null!;
    private Button btnVnav     = null!;
    private Button btnFix      = null!;
    private Button btnLegs     = null!;
    private Button btnHold     = null!;
    private Button btnFmcComm  = null!;
    private Button btnProg     = null!;
    private Button btnNavRad   = null!;
    private Button btnPrevPage = null!;
    private Button btnNextPage = null!;

    // Special buttons
    private Button btnExec = null!;
    private Button btnClr  = null!;

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "787-9 FMC";
        this.Size = new Size(620, 530);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.KeyPreview = true;
        this.AccessibleName = "HorizonSim 787-9 FMC";

        int y = 10;

        // Status label
        statusLabel = new Label
        {
            Text = "FMC Bridge Not Connected",
            Location = new Point(10, y),
            Size = new Size(590, 20),
            AccessibleName = "FMC status",
            TabStop = false
        };
        y += 30;

        // FMC display
        fmcDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(590, 215),
            Font = new Font("Consolas", 10f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            SelectionMode = SelectionMode.One,
            IntegralHeight = false,
            AccessibleName = "FMC Display"
        };
        y += 225;

        // Scratchpad input
        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(590, 25),
            AccessibleName = "Scratchpad"
        };
        y += 35;

        // Page buttons — two rows
        int bw = 80, bh = 28, sp = 4, sx = 10;

        btnInitRef  = MakeBtn("Init Ref",  sx + 0 * (bw + sp), y, bw, bh);
        btnRte      = MakeBtn("RTE",       sx + 1 * (bw + sp), y, bw, bh);
        btnDepArr   = MakeBtn("Dep/Arr",   sx + 2 * (bw + sp), y, bw, bh);
        btnAltn     = MakeBtn("Altn",      sx + 3 * (bw + sp), y, bw, bh);
        btnVnav     = MakeBtn("VNAV",      sx + 4 * (bw + sp), y, bw, bh);
        btnFix      = MakeBtn("Fix",       sx + 5 * (bw + sp), y, bw, bh);
        btnLegs     = MakeBtn("Legs",      sx + 6 * (bw + sp), y, bw, bh);
        y += bh + sp;

        btnHold     = MakeBtn("Hold",      sx + 0 * (bw + sp), y, bw, bh);
        btnFmcComm  = MakeBtn("FMC Comm",  sx + 1 * (bw + sp), y, bw, bh);
        btnProg     = MakeBtn("Prog",      sx + 2 * (bw + sp), y, bw, bh);
        btnNavRad   = MakeBtn("Nav/Rad",   sx + 3 * (bw + sp), y, bw, bh);
        btnPrevPage = MakeBtn("Prev Page", sx + 4 * (bw + sp), y, bw, bh);
        btnNextPage = MakeBtn("Next Page", sx + 5 * (bw + sp), y, bw, bh);
        y += bh + sp + 6;

        btnExec = MakeBtn("Exec",    10,  y, 100, bh);
        btnClr  = MakeBtn("CLR/DEL", 120, y, 100, bh);

        // Accessible names with hotkeys
        btnInitRef.AccessibleName  = "Init Ref (Alt+I)";
        btnRte.AccessibleName      = "RTE (Alt+R)";
        btnDepArr.AccessibleName   = "Dep/Arr (Alt+D)";
        btnAltn.AccessibleName     = "Altn (Alt+A)";
        btnVnav.AccessibleName     = "VNAV (Alt+V)";
        btnFix.AccessibleName      = "Fix (Alt+F)";
        btnLegs.AccessibleName     = "Legs (Alt+G)";
        btnHold.AccessibleName     = "Hold (Alt+H)";
        btnFmcComm.AccessibleName  = "FMC Comm (Alt+O)";
        btnProg.AccessibleName     = "Prog (Alt+P)";
        btnNavRad.AccessibleName   = "Nav/Rad (Alt+N)";
        btnPrevPage.AccessibleName = "Prev Page (PageUp)";
        btnNextPage.AccessibleName = "Next Page (PageDown)";
        btnExec.AccessibleName     = "Execute (Alt+E)";
        btnClr.AccessibleName      = "CLR/DEL (Alt+C)";

        this.Controls.AddRange(new Control[]
        {
            statusLabel, fmcDisplay, scratchpadInput,
            btnInitRef, btnRte, btnDepArr, btnAltn, btnVnav, btnFix, btnLegs,
            btnHold, btnFmcComm, btnProg, btnNavRad, btnPrevPage, btnNextPage,
            btnExec, btnClr
        });

        int tab = 0;
        fmcDisplay.TabIndex      = tab++;
        scratchpadInput.TabIndex = tab++;
        btnInitRef.TabIndex      = tab++;
        btnRte.TabIndex          = tab++;
        btnDepArr.TabIndex       = tab++;
        btnAltn.TabIndex         = tab++;
        btnVnav.TabIndex         = tab++;
        btnFix.TabIndex          = tab++;
        btnLegs.TabIndex         = tab++;
        btnHold.TabIndex         = tab++;
        btnFmcComm.TabIndex      = tab++;
        btnProg.TabIndex         = tab++;
        btnNavRad.TabIndex       = tab++;
        btnPrevPage.TabIndex     = tab++;
        btnNextPage.TabIndex     = tab++;
        btnExec.TabIndex         = tab++;
        btnClr.TabIndex          = tab++;

        this.ResumeLayout(false);
    }

    private static Button MakeBtn(string text, int x, int y, int w, int h) =>
        new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), AccessibleName = text };
}
