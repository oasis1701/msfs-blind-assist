using System.Runtime.InteropServices;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Read-only view of the SayIntentions flight information: one list box per section,
/// stacked in report order.
///
/// This replaces a single spoken run-on sentence. That was fine while the readout held
/// three facts; with the gate, the runway configuration and two airports' altimeters in
/// it, speaking the lot leaves a blind pilot no way to re-hear one part without hearing
/// all of it — and no way to stop it.
///
/// A LIST is the control for it, not a box of text. The window is a lookup surface: you
/// open it to find ONE value, so the structure has to be something you can jump around
/// rather than a run you arrow through from the top. Tab moves between sections, the
/// arrow keys move within one, and typing a letter jumps to the next item starting with
/// it. It also brailles correctly — a list item is a discrete object, so it comes out as
/// one unit and the reader announces its position ("3 of 7"), where a multi-line text box
/// can only braille the caret LINE and line boundaries there are a rendering artefact
/// (the same reasoning that put the A32NX DCDU in a ListBox).
///
/// The section list boxes are the same <see cref="DisplayListBox"/> every other display
/// window in the app uses, so the reading behaviour a pilot has learned in the Weather
/// Radar window carries over unchanged.
/// </summary>
public class SayIntentionsInfoForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int Gutter = 12;
    private const int HeadingHeight = 20;
    private const int HeadingGap = 2;
    private const int ClientWidth = 680;
    private const int ButtonRowHeight = Gutter + 32 + Gutter;

    /// <summary>A section taller than this scrolls inside its own list rather than
    /// pushing the rest of the report off the window. Nothing the report emits is
    /// anywhere near it today — the longest section is six items — so in practice every
    /// section shows whole.</summary>
    private const int MaxVisibleItems = 12;

    private readonly IntPtr _previousWindow;
    private readonly List<DisplayListBox> _sectionBoxes = new();
    private Panel _sectionPanel = null!;
    private Button _closeButton = null!;

    public SayIntentionsInfoForm(IReadOnlyList<InfoSection> sections)
    {
        _previousWindow = GetForegroundWindow();
        InitializeComponent(sections);
    }

    private void InitializeComponent(IReadOnlyList<InfoSection> sections)
    {
        Text = "SayIntentions Flight Information";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = true;

        // The sections scroll as a group. There are three or four of them today and the
        // count moves with the flight, so more of them than fit the screen has to stay
        // reachable rather than being clipped off the bottom.
        //
        // The panel is given its final WIDTH before a single child goes in. Children are
        // laid out against the width they are added into, so sizing the panel afterwards
        // would stretch every one of them by the difference.
        _sectionPanel = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ClientWidth, 100),
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            TabIndex = 0
        };

        // The scrollbar's width is reserved up front rather than left to appear over the
        // content, and the sections are a fixed width anchored top-left: a section that
        // resized as the scrollbar came and went would be a section moving under the
        // reading cursor, and these lines are short enough that width buys nothing.
        int boxWidth = ClientWidth - (2 * Gutter) - SystemInformation.VerticalScrollBarWidth;
        int y = Gutter;
        int tabIndex = 0;

        foreach (var section in sections)
        {
            // A visual label AND the same text as the list's own AccessibleName. The
            // label is what a sighted user reads; the AccessibleName is what the screen
            // reader speaks as focus arrives, so tabbing in says which section you are
            // in before it says the first item. A Label alone could not do it — labels
            // have no tab stop, so the text would be unreachable by keyboard.
            var heading = new Label
            {
                Text = section.Heading,
                Location = new Point(Gutter, y),
                Size = new Size(boxWidth, HeadingHeight),
                AccessibleName = $"{section.Heading} label",
                TabIndex = tabIndex++
            };
            y += HeadingHeight + HeadingGap;

            var box = new DisplayListBox
            {
                Location = new Point(Gutter, y),
                AccessibleName = section.Heading,
                TabIndex = tabIndex++
            };
            box.Size = new Size(boxWidth, BoxHeight(box, section.Items.Count));
            box.SetLines(section.Items);

            // Item 0 selected. This is NOT the text box's select-all, which read the
            // whole report in one breath and was the behaviour this window exists to
            // replace: selecting a list item speaks ONE line. What it buys is that
            // tabbing into a section says "heading, first item, 1 of n" — the pilot
            // learns where they are, what is there and how much of it, in one utterance,
            // instead of landing on a list that says nothing until they press Down.
            if (box.Items.Count > 0) box.SelectedIndex = 0;

            y += box.Height + Gutter;

            _sectionPanel.Controls.Add(heading);
            _sectionPanel.Controls.Add(box);
            _sectionBoxes.Add(box);
        }

        _closeButton = new Button
        {
            Text = "&Close",
            Size = new Size(100, 32),
            Anchor = AnchorStyles.Bottom,
            AccessibleName = "Close",
            AccessibleDescription = "Close the SayIntentions flight information window",
            TabIndex = 1
        };
        _closeButton.Click += (_, _) => Close();

        // Fit the window to what there is, but never past the screen — beyond that the
        // panel scrolls. Only the HEIGHT moves here; the width the sections were laid
        // out against is the width they keep.
        int maxPanelHeight = (Screen.PrimaryScreen?.WorkingArea.Height ?? 800) - 120 - ButtonRowHeight;
        int panelHeight = Math.Clamp(y, 120, Math.Max(120, maxPanelHeight));

        ClientSize = new Size(ClientWidth, panelHeight + ButtonRowHeight);
        MinimumSize = new Size(360, 240);
        _sectionPanel.Height = panelHeight;
        _closeButton.Location = new Point((ClientWidth - _closeButton.Width) / 2, panelHeight + Gutter);

        Controls.Add(_sectionPanel);
        Controls.Add(_closeButton);

        CancelButton = _closeButton;

        Load += (_, _) =>
        {
            BringToFront();
            Activate();

            // Focus the first section. HasContent gates the caller so an empty report
            // never gets this far, but a window with nothing to focus must still land
            // somewhere the keyboard can act on.
            if (_sectionBoxes.Count > 0) _sectionBoxes[0].Focus();
            else _closeButton.Focus();
        };
    }

    /// <summary>Tall enough to show the whole section without a scrollbar, up to
    /// <see cref="MaxVisibleItems"/>. Measured off the font rather than read back from
    /// the control, which would force the window handle to exist first.</summary>
    private static int BoxHeight(DisplayListBox box, int itemCount)
    {
        int itemHeight = box.Font.Height + 2;
        return (Math.Clamp(itemCount, 1, MaxVisibleItems) * itemHeight) + 6;
    }

    /// <summary>Escape closes, like every other read-only window in the app.</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        // Hand the foreground back to whatever had it — normally the simulator.
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }
}
