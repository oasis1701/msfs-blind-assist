namespace MSFSBlindAssist.Forms.PMDG777;

public partial class PMDG777CDUForm
{
    private Label statusLabel = null!;
    private ListBox cduDisplay = null!;
    private TextBox scratchpadInput = null!;
    private ComboBox cduSelector = null!;

    // Page buttons
    private Button btnInitRef = null!;
    private Button btnRte = null!;
    private Button btnDepArr = null!;
    private Button btnAltn = null!;
    private Button btnVnav = null!;
    private Button btnFix = null!;
    private Button btnLegs = null!;
    private Button btnHold = null!;
    private Button btnFmcComm = null!;
    private Button btnProg = null!;
    private Button btnMenu = null!;
    private Button btnPrevPage = null!;
    private Button btnNextPage = null!;

    // Special buttons
    private Button btnExec = null!;
    private Button btnClr = null!;
    private Button btnDel = null!;

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "PMDG 777 CDU";
        this.Size = new Size(600, 540);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.KeyPreview = true;
        this.AccessibleName = "PMDG 777 CDU";
        this.AccessibleDescription = "PMDG 777 CDU display and controls";

        int y = 10;

        // Status label
        statusLabel = new Label
        {
            Text = "CDU Not Connected",
            Location = new Point(10, y),
            Size = new Size(400, 20),
            AccessibleName = "CDU status",
            AccessibleDescription = "Shows whether the CDU is connected and powered",
            TabStop = false
        };

        // CDU selector
        cduSelector = new ComboBox
        {
            Location = new Point(420, y),
            Size = new Size(160, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "CDU selector",
            AccessibleDescription = "Choose which CDU to display: Left (Captain), Center, or Right (First Officer)"
        };
        cduSelector.Items.AddRange(new object[] { "Left (Captain)", "Center", "Right (First Officer)" });
        cduSelector.SelectedIndex = 0;
        y += 30;

        // CDU display
        cduDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(570, 210),
            Font = new Font("Consolas", 10f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            SelectionMode = SelectionMode.One,
            IntegralHeight = false,
            AccessibleName = "CDU Display",
            AccessibleDescription = "Shows the current CDU screen content. Use arrow keys to read lines."
        };
        y += 220;

        // Scratchpad input
        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(570, 25),
            AccessibleName = "Scratchpad",
            AccessibleDescription = "Type text and press Enter to send to the CDU scratchpad. Press Backspace on empty field to send CLR."
        };
        y += 35;

        // Page buttons — two rows of 7 and 6
        int pageBtnWidth = 78;
        int pageBtnHeight = 28;
        int pageSpacing = 4;
        int pageStartX = 10;

        btnInitRef  = CreatePageButton("Init Ref",  "INIT_REF",  pageStartX + 0  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnRte      = CreatePageButton("RTE",        "RTE",       pageStartX + 1  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnDepArr   = CreatePageButton("Dep/Arr",    "DEP_ARR",   pageStartX + 2  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnAltn     = CreatePageButton("Altn",       "ALTN",      pageStartX + 3  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnVnav     = CreatePageButton("VNAV",       "VNAV",      pageStartX + 4  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnFix      = CreatePageButton("Fix",        "FIX",       pageStartX + 5  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnLegs     = CreatePageButton("Legs",       "LEGS",      pageStartX + 6  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        y += pageBtnHeight + pageSpacing;

        btnHold     = CreatePageButton("Hold",       "HOLD",      pageStartX + 0  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnFmcComm  = CreatePageButton("FMC Comm",   "FMCCOMM",   pageStartX + 1  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnProg     = CreatePageButton("Prog",       "PROG",      pageStartX + 2  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnMenu     = CreatePageButton("Menu",       "MENU",      pageStartX + 3  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnPrevPage = CreatePageButton("Prev Page",  "PREV_PAGE", pageStartX + 4  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnNextPage = CreatePageButton("Next Page",  "NEXT_PAGE", pageStartX + 5  * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        y += pageBtnHeight + pageSpacing + 6;

        // Special buttons
        btnExec = CreatePageButton("Exec",   "EXEC", 10,  y, 100, pageBtnHeight);
        btnClr  = CreatePageButton("CLR",    "CLR",  120, y, 100, pageBtnHeight);
        btnDel  = CreatePageButton("DEL",    "DEL",  230, y, 100, pageBtnHeight);

        btnExec.AccessibleName = "Execute";
        btnExec.AccessibleDescription = "Execute flight plan modification (Alt+E)";
        btnClr.AccessibleName = "Clear";
        btnClr.AccessibleDescription = "Clear scratchpad (Alt+C)";
        btnDel.AccessibleName = "Delete";
        btnDel.AccessibleDescription = "Delete selected field (Alt+L)";

        // Accessible descriptions with hotkeys for page buttons
        btnInitRef.AccessibleDescription = "CDU Init Ref page (Alt+I)";
        btnRte.AccessibleDescription = "CDU Route page (Alt+R)";
        btnDepArr.AccessibleDescription = "CDU Dep/Arr page (Alt+D)";
        btnAltn.AccessibleDescription = "CDU Altn page (Alt+A)";
        btnVnav.AccessibleDescription = "CDU VNAV page (Alt+V)";
        btnFix.AccessibleDescription = "CDU Fix page (Alt+F)";
        btnLegs.AccessibleDescription = "CDU Legs page (Alt+G)";
        btnHold.AccessibleDescription = "CDU Hold page (Alt+H)";
        btnFmcComm.AccessibleDescription = "CDU FMC Comm (Alt+O)";
        btnProg.AccessibleDescription = "CDU Prog page (Alt+P)";
        btnMenu.AccessibleDescription = "CDU Menu page (Alt+M)";
        btnPrevPage.AccessibleDescription = "Previous page (PageUp)";
        btnNextPage.AccessibleDescription = "Next page (PageDown)";

        // Add all controls
        this.Controls.AddRange(new Control[]
        {
            statusLabel, cduSelector, cduDisplay, scratchpadInput,
            btnInitRef, btnRte, btnDepArr, btnAltn, btnVnav, btnFix, btnLegs,
            btnHold, btnFmcComm, btnProg, btnMenu, btnPrevPage, btnNextPage,
            btnExec, btnClr, btnDel
        });

        // Set logical tab order
        int tabIdx = 0;
        cduDisplay.TabIndex     = tabIdx++;
        scratchpadInput.TabIndex = tabIdx++;
        cduSelector.TabIndex    = tabIdx++;
        btnInitRef.TabIndex  = tabIdx++;
        btnRte.TabIndex      = tabIdx++;
        btnDepArr.TabIndex   = tabIdx++;
        btnAltn.TabIndex     = tabIdx++;
        btnVnav.TabIndex     = tabIdx++;
        btnFix.TabIndex      = tabIdx++;
        btnLegs.TabIndex     = tabIdx++;
        btnHold.TabIndex     = tabIdx++;
        btnFmcComm.TabIndex  = tabIdx++;
        btnProg.TabIndex     = tabIdx++;
        btnMenu.TabIndex     = tabIdx++;
        btnPrevPage.TabIndex = tabIdx++;
        btnNextPage.TabIndex = tabIdx++;
        btnExec.TabIndex = tabIdx++;
        btnClr.TabIndex  = tabIdx++;
        btnDel.TabIndex  = tabIdx++;

        this.ResumeLayout(false);
    }

    private static Button CreatePageButton(string text, string suffix, int x, int y, int width, int height)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            AccessibleName = text,
            AccessibleDescription = $"CDU {text} page button"
        };
    }
}
