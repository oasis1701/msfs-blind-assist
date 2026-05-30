namespace MSFSBlindAssist.Forms.PMDG737;

public partial class PMDG737CDUForm
{
    private Label statusLabel = null!;
    private ListBox cduDisplay = null!;
    private TextBox scratchpadInput = null!;
    private ComboBox cduSelector = null!;

    // Page buttons (NG3 SDK: INIT_REF, RTE, CLB, CRZ, DES, MENU, LEGS,
    // DEP_ARR, HOLD, PROG, N1_LIMIT, FIX, PREV_PAGE, NEXT_PAGE)
    private Button btnInitRef = null!;
    private Button btnRte = null!;
    private Button btnClb = null!;
    private Button btnCrz = null!;
    private Button btnDes = null!;
    private Button btnMenu = null!;
    private Button btnLegs = null!;
    private Button btnDepArr = null!;
    private Button btnHold = null!;
    private Button btnProg = null!;
    private Button btnN1Limit = null!;
    private Button btnFix = null!;
    private Button btnPrevPage = null!;
    private Button btnNextPage = null!;

    // Special buttons
    private Button btnExec = null!;
    private Button btnClr = null!;

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "PMDG 737 CDU";
        this.Size = new Size(600, 540);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.KeyPreview = true;
        this.AccessibleName = "PMDG 737 CDU";

        int y = 10;

        // Status label
        statusLabel = new Label
        {
            Text = "CDU Not Connected",
            Location = new Point(10, y),
            Size = new Size(400, 20),
            AccessibleName = "CDU status",
            TabStop = false
        };

        // CDU selector — NG3 has two CDUs only (Captain / First Officer).
        // Dropdown index maps directly to PMDG SDK index (0=L, 1=R), no swap.
        cduSelector = new ComboBox
        {
            Location = new Point(420, y),
            Size = new Size(160, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "CDU selector"
        };
        cduSelector.Items.AddRange(new object[] { "Left (Captain)", "Right (First Officer)" });
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
            AccessibleName = "CDU Display"
        };
        y += 220;

        // Scratchpad input
        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(570, 25),
            AccessibleName = "Scratchpad"
        };
        y += 35;

        // Page buttons — two rows of 7
        int pageBtnWidth = 78;
        int pageBtnHeight = 28;
        int pageSpacing = 4;
        int pageStartX = 10;

        // Row 1: Init Ref, RTE, CLB, CRZ, DES, Menu, Legs
        btnInitRef = CreatePageButton("Init Ref", pageStartX + 0 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnRte     = CreatePageButton("RTE",      pageStartX + 1 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnClb     = CreatePageButton("CLB",      pageStartX + 2 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnCrz     = CreatePageButton("CRZ",      pageStartX + 3 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnDes     = CreatePageButton("DES",      pageStartX + 4 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnMenu    = CreatePageButton("Menu",     pageStartX + 5 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnLegs    = CreatePageButton("Legs",     pageStartX + 6 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        y += pageBtnHeight + pageSpacing;

        // Row 2: Dep/Arr, Hold, Prog, N1 Limit, Fix, Prev Page, Next Page
        btnDepArr   = CreatePageButton("Dep/Arr",   pageStartX + 0 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnHold     = CreatePageButton("Hold",      pageStartX + 1 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnProg     = CreatePageButton("Prog",      pageStartX + 2 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnN1Limit  = CreatePageButton("N1 Limit",  pageStartX + 3 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnFix      = CreatePageButton("Fix",       pageStartX + 4 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnPrevPage = CreatePageButton("Prev Page", pageStartX + 5 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        btnNextPage = CreatePageButton("Next Page", pageStartX + 6 * (pageBtnWidth + pageSpacing), y, pageBtnWidth, pageBtnHeight);
        y += pageBtnHeight + pageSpacing + 6;

        // Special buttons
        btnExec = CreatePageButton("Exec",    10,  y, 100, pageBtnHeight);
        btnClr  = CreatePageButton("CLR/DEL", 120, y, 100, pageBtnHeight);

        // Button accessible names: label + hotkey
        btnInitRef.AccessibleName  = "Init Ref (Alt+I)";
        btnRte.AccessibleName      = "RTE (Alt+R)";
        btnClb.AccessibleName      = "CLB (Alt+B)";
        btnCrz.AccessibleName      = "CRZ (Alt+Z)";
        btnDes.AccessibleName      = "DES (Alt+D)";
        btnMenu.AccessibleName     = "Menu (Alt+M)";
        btnLegs.AccessibleName     = "Legs (Alt+G)";
        btnDepArr.AccessibleName   = "Dep/Arr (Alt+A)";
        btnHold.AccessibleName     = "Hold (Alt+H)";
        btnProg.AccessibleName     = "Prog (Alt+P)";
        btnN1Limit.AccessibleName  = "N1 Limit (Alt+N)";
        btnFix.AccessibleName      = "Fix (Alt+F)";
        btnPrevPage.AccessibleName = "Prev Page (PageUp)";
        btnNextPage.AccessibleName = "Next Page (PageDown)";
        btnExec.AccessibleName     = "Execute (Alt+E)";
        btnClr.AccessibleName      = "CLR/DEL (Alt+C)";

        // Add all controls
        this.Controls.AddRange(new Control[]
        {
            statusLabel, cduSelector, cduDisplay, scratchpadInput,
            btnInitRef, btnRte, btnClb, btnCrz, btnDes, btnMenu, btnLegs,
            btnDepArr, btnHold, btnProg, btnN1Limit, btnFix, btnPrevPage, btnNextPage,
            btnExec, btnClr
        });

        // Set logical tab order
        int tabIdx = 0;
        cduDisplay.TabIndex      = tabIdx++;
        scratchpadInput.TabIndex = tabIdx++;
        cduSelector.TabIndex     = tabIdx++;
        btnInitRef.TabIndex  = tabIdx++;
        btnRte.TabIndex      = tabIdx++;
        btnClb.TabIndex      = tabIdx++;
        btnCrz.TabIndex      = tabIdx++;
        btnDes.TabIndex      = tabIdx++;
        btnMenu.TabIndex     = tabIdx++;
        btnLegs.TabIndex     = tabIdx++;
        btnDepArr.TabIndex   = tabIdx++;
        btnHold.TabIndex     = tabIdx++;
        btnProg.TabIndex     = tabIdx++;
        btnN1Limit.TabIndex  = tabIdx++;
        btnFix.TabIndex      = tabIdx++;
        btnPrevPage.TabIndex = tabIdx++;
        btnNextPage.TabIndex = tabIdx++;
        btnExec.TabIndex = tabIdx++;
        btnClr.TabIndex  = tabIdx++;

        this.ResumeLayout(false);
    }

    private static Button CreatePageButton(string text, int x, int y, int width, int height)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            AccessibleName = text
        };
    }
}
