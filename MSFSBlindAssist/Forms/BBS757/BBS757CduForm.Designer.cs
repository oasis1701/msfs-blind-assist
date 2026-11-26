namespace MSFSBlindAssist.Forms.BBS757;

public partial class BBS757CduForm
{
    // Read-only TextBox, not a Label: a Label is not in the tab order ([VAT-13]).
    private TextBox statusBox = null!;
    // A ListBox, never a multiline TextBox, so a right-hand field stays on its key's line ([A320-15]).
    private ListBox cduDisplay = null!;
    private TextBox scratchpadInput = null!;

    // Page buttons
    private Button btnInitRef = null!;
    private Button btnRte = null!;
    private Button btnDepArr = null!;
    private Button btnAtc = null!;
    private Button btnVnav = null!;
    private Button btnFix = null!;
    private Button btnLegs = null!;
    private Button btnHold = null!;
    private Button btnFmcComm = null!;
    private Button btnProg = null!;
    private Button btnMenu = null!;
    private Button btnNavRad = null!;
    private Button btnPrevPage = null!;
    private Button btnNextPage = null!;

    // Special buttons
    private Button btnExec = null!;
    private Button btnClr = null!;
    private Button btnDel = null!;
    private Button btnSwitchCdu = null!;

    // The Ctrl+M row "CDU Messages While Closed", shown here (plan 09)
    private CheckBox chkSpeakMessages = null!;

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = TitleFor(1);   // the captain's CDU is shown first; SelectCdu renames the window
        this.Size = new Size(640, 560);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.KeyPreview = true;

        int y = 10;

        statusBox = new TextBox
        {
            Text = WaitingText,
            Location = new Point(10, y),
            Size = new Size(605, 23),
            AccessibleName = "CDU status",
            ReadOnly = true,
            TabStop = true,
            BorderStyle = BorderStyle.FixedSingle,
        };
        y += 30;

        cduDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(605, 230),
            Font = new Font("Consolas", 10f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            SelectionMode = SelectionMode.One,
            IntegralHeight = false,
            AccessibleName = "CDU display",
        };
        y += 240;

        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(605, 25),
            AccessibleName = "Scratchpad (Alt+S, Enter to send)",
        };
        y += 35;

        // Page buttons: two rows of seven.
        int bw = 83, bh = 28, sp = 4, sx = 10;

        btnInitRef  = MakeBtn("Init Ref",  "Init Ref (Alt+I)",     sx + 0 * (bw + sp), y, bw, bh);
        btnRte      = MakeBtn("RTE",       "RTE (Alt+R)",          sx + 1 * (bw + sp), y, bw, bh);
        btnDepArr   = MakeBtn("Dep Arr",   "Dep Arr (Alt+D)",      sx + 2 * (bw + sp), y, bw, bh);
        btnAtc      = MakeBtn("ATC",       "ATC (Alt+T)",          sx + 3 * (bw + sp), y, bw, bh);
        btnVnav     = MakeBtn("VNAV",      "VNAV (Alt+V)",         sx + 4 * (bw + sp), y, bw, bh);
        btnFix      = MakeBtn("Fix",       "Fix (Alt+F)",          sx + 5 * (bw + sp), y, bw, bh);
        btnLegs     = MakeBtn("Legs",      "Legs (Alt+G)",         sx + 6 * (bw + sp), y, bw, bh);
        y += bh + sp;

        btnHold     = MakeBtn("Hold",      "Hold (Alt+H)",         sx + 0 * (bw + sp), y, bw, bh);
        btnFmcComm  = MakeBtn("FMC Comm",  "FMC Comm (Alt+O)",     sx + 1 * (bw + sp), y, bw, bh);
        btnProg     = MakeBtn("Prog",      "Prog (Alt+P)",         sx + 2 * (bw + sp), y, bw, bh);
        btnMenu     = MakeBtn("Menu",      "Menu (Alt+M)",         sx + 3 * (bw + sp), y, bw, bh);
        btnNavRad   = MakeBtn("Nav Rad",   "Nav Rad (Alt+N)",      sx + 4 * (bw + sp), y, bw, bh);
        btnPrevPage = MakeBtn("Prev Page", "Prev Page (PageUp)",   sx + 5 * (bw + sp), y, bw, bh);
        btnNextPage = MakeBtn("Next Page", "Next Page (PageDown)", sx + 6 * (bw + sp), y, bw, bh);
        y += bh + sp + 6;

        btnExec      = MakeBtn("Exec",       "Execute (Alt+E)",       10,  y, 100, bh);
        btnClr       = MakeBtn("CLR",        "CLR (Backspace; Alt+C clears all)", 120, y, 100, bh);
        btnDel       = MakeBtn("DEL",        "DEL (Delete)",          230, y, 100, bh);
        btnSwitchCdu = MakeBtn("Switch CDU", "Switch CDU (Ctrl+Tab; Ctrl+Shift+L captain, Ctrl+Shift+R first officer)", 340, y, 120, bh);
        y += bh + sp + 6;

        // Its state is the Ctrl+M row's (checked = the row is ticked), refreshed each time the window is shown.
        chkSpeakMessages = new CheckBox
        {
            Text = SpeakMessagesText,
            Location = new Point(10, y),
            Size = new Size(605, 24),
            AccessibleName = SpeakMessagesText,
            Checked = true,
        };

        this.Controls.AddRange(new Control[]
        {
            statusBox, cduDisplay, scratchpadInput,
            btnInitRef, btnRte, btnDepArr, btnAtc, btnVnav, btnFix, btnLegs,
            btnHold, btnFmcComm, btnProg, btnMenu, btnNavRad, btnPrevPage, btnNextPage,
            btnExec, btnClr, btnDel, btnSwitchCdu, chkSpeakMessages
        });

        // The display first, as in the HS787 window; the status box is reachable by Tab.
        int tab = 0;
        cduDisplay.TabIndex      = tab++;
        scratchpadInput.TabIndex = tab++;
        statusBox.TabIndex       = tab++;
        btnInitRef.TabIndex      = tab++;
        btnRte.TabIndex          = tab++;
        btnDepArr.TabIndex       = tab++;
        btnAtc.TabIndex          = tab++;
        btnVnav.TabIndex         = tab++;
        btnFix.TabIndex          = tab++;
        btnLegs.TabIndex         = tab++;
        btnHold.TabIndex         = tab++;
        btnFmcComm.TabIndex      = tab++;
        btnProg.TabIndex         = tab++;
        btnMenu.TabIndex         = tab++;
        btnNavRad.TabIndex       = tab++;
        btnPrevPage.TabIndex     = tab++;
        btnNextPage.TabIndex     = tab++;
        btnExec.TabIndex         = tab++;
        btnClr.TabIndex          = tab++;
        btnDel.TabIndex          = tab++;
        btnSwitchCdu.TabIndex    = tab++;
        chkSpeakMessages.TabIndex = tab++;

        this.ResumeLayout(false);
    }

    private static Button MakeBtn(string text, string accessibleName, int x, int y, int w, int h) =>
        new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), AccessibleName = accessibleName };
}
