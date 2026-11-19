using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The press pipeline's decisions, pure (plan 18, Task 5). The bridge does the capturing, tapping and reading; what it decides
/// from the reads lives here, where it is tested without a tablet: finding the control the pilot pressed again in a fresh read
/// (<see cref="Relocate"/>), whether a switch still needs its tap (<see cref="NeedsTap"/>), which new word a switch of two choices
/// shows (<see cref="NewChoiceWord"/>), where to tap a slider with choices and where to tap next (<see cref="ChoiceX"/>,
/// <see cref="NextChoiceX"/>), where an open drop-down's row is tapped and whether an open list or BlueBird's notification panel covers a
/// press (<see cref="RowTapPoint"/>, <see cref="CoveringList"/>, <see cref="UnderNotice"/>), whether a press changed the page (<see cref="PageUnchanged"/>), what a button press says
/// (<see cref="ButtonOutcome"/>: the pressed control's new words, or that nothing changed), and the words said when a press did
/// not go through or a switch of two choices went to its other choice. Nothing here
/// refuses a press (the owner's ruling of 2026-10-29): a decision says where a control is now, whether its tap is still wanted,
/// or what became of the press.
/// </summary>
public static class Bbs757EfbPress
{
    /// <summary>A slider with choices is tapped at most this many times for one wish: its place, then steps toward it.</summary>
    public const int MaxSliderTaps = 4;

    /// <summary>The smallest step toward a choice, in tablet pixels (a slider with very many choices would step less).</summary>
    private const int MinStepPx = 4;

    // ------------------------------------------------------------------ the page as one string

    /// <summary>
    /// What a page says, as one string: two reads that say the same thing give the same one. It covers the page's name, its menu
    /// and controls (kind, key, words, value, state, selection, the place of a slider's knob read from memory, in whole percent, and a
    /// drop-down's entries),
    /// the question over it, the top bar and the notifications; not where anything is, so a pixel of wobble changes nothing. The bridge pushes a read only when this moved, so a notification or a
    /// question appearing over a page that did not change counts as a change.
    /// </summary>
    public static string Fingerprint(Bbs757EfbScreenModel m) => Describe(m, withBars: true);

    /// <summary>
    /// Whether the page itself is the same in two reads: <see cref="Fingerprint"/> without the top bar (the departure countdown, the
    /// boarding control, the notification count) and the notifications, which come and go over any page. A question appearing is a
    /// change.
    /// </summary>
    public static bool PageUnchanged(Bbs757EfbScreenModel before, Bbs757EfbScreenModel after) =>
        string.Equals(Describe(before, withBars: false), Describe(after, withBars: false), StringComparison.Ordinal);

    private static string Describe(Bbs757EfbScreenModel m, bool withBars)
    {
        var sb = new StringBuilder(m.PageName).Append('\n');
        foreach (var i in m.Menu.Concat(m.Items)) sb.Append(Line(i));
        if (m.Question is { } q)
        {
            sb.Append("question|").Append(q.Text).Append('\n');
            foreach (var r in q.Rows) sb.Append(Line(r));
            foreach (var b in q.Buttons) sb.Append(Line(b));
        }
        if (withBars)
        {
            sb.Append("top bar\n");
            foreach (var i in m.TopBar) sb.Append(Line(i));
            sb.Append("notices\n");
            foreach (var n in m.Notices)
                sb.Append(n.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(n.Time).Append('|').Append(n.Text).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>One control as the fingerprint says it: what the pilot would hear change. A slider whose knob BlueBird's memory gives
    /// (<see cref="Bbs757EfbSlider.KnobRead"/>) adds its place, in whole percent as the window shows it, so a knob moved alone (a zoom
    /// button's tap, a set with no value shown, BlueBird moving a reservoir's level) is a change; a pixel of wobble within the same
    /// percent is none. Any other slider's knob comes from its value, which the line has already. A drop-down adds its entries, which
    /// its choice list offers (another airport's runways under the same first one shown are a change). A field whose text state was read
    /// adds that it was (the final review's M4): the window shows it as an edit box and any other field as a line, so a read where a field
    /// gains or loses it is a change; its caret is not, as it moves while the pilot types.</summary>
    private static string Line(Bbs757EfbItem i)
    {
        var sb = new StringBuilder().Append(i.Kind).Append('|').Append(i.Key).Append('|').Append(i.Text).Append('|')
            .Append(i.Label).Append('|').Append(i.Level).Append('|').Append(i.Value).Append('|').Append(i.On).Append('|').Append(i.Selected)
            .Append('|').Append(i.Slider is { KnobRead: true } s
                ? Math.Round(s.PercentOf(s.KnobX)).ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        if (i.Entries is { } entries) sb.Append("|entries|").Append(string.Join('\u001f', entries));
        if (i.Unit.Length > 0) sb.Append("|unit|").Append(i.Unit);   // a dialog box's unit: its name says it (plan 22)
        if (i.TextState != null) sb.Append("|edit");                // an edit box in the window, not a line (plan 22)
        return sb.Append('\n').ToString();
    }

    // ------------------------------------------------------------------ finding a pressed control again

    /// <summary>
    /// The control the pilot pressed, found again in <paramref name="now"/>, a fresh read of the tablet taken after the page
    /// <paramref name="before"/> was read and the press was chosen from it; null when it is not there (the page changed under the
    /// pilot). A notification over it does not hide it: memory still holds it (<see cref="UnderNotice"/> says so). Searched in <paramref name="now"/>'s menu, controls, top bar and question buttons, by the
    /// first rule that finds one:
    /// <list type="number">
    /// <item>none, when the two reads are of different pages;</item>
    /// <item>the control with the same key;</item>
    /// <item>the control of the same kind whose box overlaps the pressed one's with intersection over union of at least 0.6: a
    /// tile changing its words in place ("Req fuel truck" becoming "Requesting...") keeps its box, give or take the few pixels the
    /// reader's box varies by from one capture to the next; the best overlap wins;</item>
    /// <item>the control of the same kind that overlaps the pressed box at all and has the same words once the state word that ends
    /// them is left out ("Fuel truck off", "Chocks, on"): the tile moved a little, which changes its key and most of its box;</item>
    /// <item>none.</item>
    /// </list>
    /// A neighbor with other words that covers the place in part is never taken: rule 3 wants it to be the same box, rule 4 the
    /// same words. Tapping it would press a different control than the one asked for.
    /// </summary>
    public static Bbs757EfbItem? Relocate(Bbs757EfbScreenModel before, Bbs757EfbScreenModel now, Bbs757EfbItem pressed)
    {
        if (!SamePage(before, now)) return null;
        var controls = now.Menu.Concat(now.Items).Concat(now.TopBar).Concat(now.Question?.Buttons ?? Array.Empty<Bbs757EfbItem>()).ToList();

        foreach (var c in controls)
            if (string.Equals(c.Key, pressed.Key, StringComparison.Ordinal)) return c;

        var covering = new List<Cover>();
        foreach (var c in controls)
        {
            if (c.Kind != pressed.Kind) continue;
            var (inter, union) = Overlap(pressed.Box, c.Box);
            if (inter > 0) covering.Add(new Cover(c, inter, union));
        }

        // 0.6 as 3/5, so that exactly 0.6 is in and no floating point decides it.
        var inPlace = covering.Where(c => 5 * c.Intersection >= 3 * c.Union).OrderByDescending(c => (double)c.Intersection / c.Union).FirstOrDefault();
        if (inPlace != null) return inPlace.Item;

        var words = StatelessWords(pressed.Text);
        if (words.Length == 0) return null;
        return covering.Where(c => words.SequenceEqual(StatelessWords(c.Item.Text), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Intersection).FirstOrDefault()?.Item;
    }

    private sealed record Cover(Bbs757EfbItem Item, long Intersection, long Union);

    private static bool SamePage(Bbs757EfbScreenModel a, Bbs757EfbScreenModel b) =>
        string.Equals(a.PageName, b.PageName, StringComparison.Ordinal);

    /// <summary>The area two boxes share and the area they cover together. A box with no area (the window's own Home button has
    /// none) shares nothing with any other.</summary>
    private static (long Intersection, long Union) Overlap(Rectangle a, Rectangle b)
    {
        var both = Rectangle.Intersect(a, b);
        long inter = (long)both.Width * both.Height;
        return (inter, (long)a.Width * a.Height + (long)b.Width * b.Height - inter);
    }

    private static readonly Regex WordRun = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);

    /// <summary>A control's words (letters and digits, in order) without the state word that ends them
    /// (<see cref="Bbs757EfbScreen.StateWords"/>): "Chocks, on" is Chocks, "Left FWD: Closed" is Left FWD. A state word alone is no
    /// words ("Open" stays Open), and "on" ends only a word of its own, never "Common".</summary>
    private static string[] StatelessWords(string text)
    {
        var words = WordRun.Matches(text).Select(m => m.Value).ToList();
        foreach (var state in Bbs757EfbScreen.StateWords)
        {
            var stateWords = state.Split(' ');
            if (words.Count > stateWords.Length && words.TakeLast(stateWords.Length).SequenceEqual(stateWords, StringComparer.OrdinalIgnoreCase))
            {
                words.RemoveRange(words.Count - stateWords.Length, stateWords.Length);
                break;
            }
        }
        return words.ToArray();
    }

    // ------------------------------------------------------------------ where a control is tapped, and scrolling it into view

    /// <summary>The fewest pixels of a control that must show for it to be tapped where it shows. At the end of Settings, Aircraft's
    /// scroll its last switch shows whole (drawn at 1000.5); a strip of it shows only mid-scroll, and a 10.2 px strip was measured to
    /// take a tap (offset 176.7: drawn 1037.8, the viewport's bottom 1048; 2026-11-07).</summary>
    public const int MinShownPx = 6;

    /// <summary>The part of the tablet <paramref name="item"/> can show in: its <see cref="Bbs757EfbItem.Clip"/>, or the content area for
    /// an item built with none.</summary>
    public static Rectangle ClipOf(Bbs757EfbItem item) => item.Clip == default ? Bbs757EfbScreen.ContentArea : item.Clip;

    /// <summary>
    /// Where to tap <paramref name="item"/> on the page <paramref name="m"/>: inside the part of its drawn box that is within its own
    /// <see cref="ClipOf">clip</see> (the scrollbox it sits in, the content area in none), top and bottom only: the reader's x, the
    /// reader's y when the whole box shows, else the middle of the strip that shows; null when that strip is under
    /// <see cref="MinShownPx"/> tall. A control that is not part of the page's content (the side menu, the top bar, a question's
    /// answers) is tapped where the reader found it.
    /// </summary>
    public static Point? TapPoint(Bbs757EfbScreenModel m, Bbs757EfbItem item)
    {
        if (item.Box.IsEmpty || !m.Items.Contains(item)) return item.Hit;
        // Vertically only: pages scroll up and down, and a content control may sit left of its clip (Systems' zoom buttons).
        var clip = ClipOf(item);
        int top = Math.Max(item.Box.Top, clip.Top), bottom = Math.Min(item.Box.Bottom, clip.Bottom);
        if (bottom - top < MinShownPx) return null;
        bool whole = top == item.Box.Top && bottom == item.Box.Bottom;
        int y = whole && item.Hit.Y >= top && item.Hit.Y < bottom ? item.Hit.Y : top + (bottom - top) / 2;
        return new Point(item.Hit.X, y);
    }

    /// <summary>
    /// One drag that scrolls <paramref name="item"/> toward the middle of the viewport: at x 250 into the viewport (the rows' label
    /// area, measured inert), from y0 to y1 so that y0 - y1 is how far the drawn middle has to go (at most the viewport's height less
    /// 200 in one drag; a longer way takes more). Null when the item can be tapped where it is, when the page does not scroll
    /// (<see cref="Bbs757EfbScreenModel.Scrollable"/>), and for every item that is not in the scrollbox that scrolls (its clip is not
    /// the <see cref="Bbs757EfbScreenModel.Viewport"/>): a drag would move nothing under it.
    /// </summary>
    public static (int X, int Y0, int Y1)? DragFor(Bbs757EfbScreenModel m, Bbs757EfbItem item)
    {
        // A page with no scrollbox that scrolls does not scroll: a drag there pans what is under it (the map, the diagram).
        if (!m.Scrollable || item.Box.IsEmpty || !m.Items.Contains(item) || ClipOf(item) != m.Viewport || TapPoint(m, item) != null)
            return null;
        var v = m.Viewport;
        int mid = v.Top + v.Height / 2;
        int delta = item.Box.Top + item.Box.Height / 2 - mid;   // positive: below the middle, the content has to move up
        int distance = Math.Clamp(Math.Abs(delta), 1, Math.Max(1, v.Height - 200));
        int start = mid - distance / 2;
        return delta > 0 ? (v.X + 250, start + distance, start) : (v.X + 250, start, start + distance);
    }

    // ------------------------------------------------------------------ an open drop-down's list (plan 21, Task 7)

    /// <summary>
    /// The part of the tablet an open drop-down's rows show in: its list's drawn rectangle (<see cref="Bbs757EfbOpenList.Viewport"/>),
    /// kept within the drop-down's own <see cref="ClipOf">clip</see> top and bottom, so a row is never tapped outside the part of the page
    /// the drop-down shows in either; null when no open list was read.
    /// </summary>
    public static Rectangle? ListViewOf(Bbs757EfbItem dropDown)
    {
        if (dropDown.List is not { } list) return null;
        var clip = ClipOf(dropDown);
        int top = Math.Max(list.Viewport.Top, clip.Top), bottom = Math.Min(list.Viewport.Bottom, clip.Bottom);
        return new Rectangle(list.Viewport.X, top, list.Viewport.Width, Math.Max(0, bottom - top));
    }

    /// <summary>The row of an open drop-down's list that shows <paramref name="entry"/> (its words exactly, as the window's choice list
    /// sends them), the first when two do; null when the list is not open, was not read, or shows no such row.</summary>
    public static Bbs757EfbListRow? RowOf(Bbs757EfbItem dropDown, string entry) =>
        dropDown.List?.Rows.FirstOrDefault(r => string.Equals(r.Text, entry, StringComparison.Ordinal));

    /// <summary>Whether all of <paramref name="row"/> shows in <paramref name="view"/>, top and bottom.</summary>
    public static bool RowShowsWhole(Rectangle view, Rectangle row) => row.Top >= view.Top && row.Bottom <= view.Bottom;

    /// <summary>
    /// Where to tap an open list's row: its middle when it shows whole in <paramref name="view"/>, else the middle of the strip of it that
    /// shows when at least <see cref="MinShownPx"/> does (as <see cref="TapPoint"/> taps a page control); null when less shows. x is the
    /// row's middle. Measured live 2026-11-07 on PERF calc's Surface list: Snow's 28 px strip took a tap (the face then read Snow), and a tap
    /// on Wet's row below the list's box closed the list without choosing, since the box clips the rows past it. An open list scrolls by no
    /// input (a drag in it, a drag on its scrollbar strip and a wheel turn all left its content offset at 0), so a row that shows less than
    /// <see cref="MinShownPx"/> cannot be chosen on the tablet at all (B757-27).
    /// </summary>
    public static Point? RowTapPoint(Rectangle view, Rectangle row)
    {
        int top = Math.Max(row.Top, view.Top), bottom = Math.Min(row.Bottom, view.Bottom);
        if (bottom - top < MinShownPx) return null;
        int x = row.Left + row.Width / 2;
        return RowShowsWhole(view, row) ? new Point(x, row.Top + row.Height / 2) : new Point(x, top + (bottom - top) / 2);
    }

    /// <summary>Whether an open list's rows are laid out: at least one, each inside the list's x span. Closed, BlueBird leaves them at
    /// [0,0 50x38] (measured), so a read that shows the list open before it laid them out has none to tap yet.</summary>
    public static bool RowsLaidOut(Bbs757EfbOpenList list) =>
        list.Rows.Count > 0 && list.Rows.All(r => r.Box.Left >= list.Viewport.Left && r.Box.Right <= list.Viewport.Right);

    /// <summary>
    /// The drop-down whose open list covers <paramref name="at"/>, a point of the page <paramref name="m"/> a press would put the mouse
    /// down on: where <paramref name="item"/> is tapped (its tap point, a slider's choice or percent place), or, with no item, where a page
    /// drag presses. BlueBird draws an open list over the page, so the press would land on one of its rows and choose that entry (the runway
    /// list of 25c covers the Flap Setting drop-down's face). Null when none does, and for an item that is no page control (the side menu,
    /// the top bar, a question's answers); a drop-down's own list never covers it.
    /// </summary>
    public static Bbs757EfbItem? CoveringList(Bbs757EfbScreenModel m, Bbs757EfbItem? item, Point at) =>
        item == null || m.Items.Contains(item)
            ? m.Items.FirstOrDefault(i => !ReferenceEquals(i, item) && i.Kind == Bbs757EfbKind.Choice && ListViewOf(i) is { } view && view.Contains(at))
            : null;

    // ------------------------------------------------------------------ BlueBird's notification panel (plan 21, the final review's I1)

    /// <summary>
    /// Whether BlueBird's notification pop-up or notification list covers <paramref name="at"/>, a point of the page <paramref name="m"/> a
    /// press would put the mouse down on: where <paramref name="item"/> is tapped, or, with no item, where a page drag presses. The top bar
    /// app draws the panel over the top right of the page from its own fields (<see cref="Bbs757EfbScreenModel.NoticePanel"/>, about
    /// 1039,38 350x118 for 10 s after a new notification: measured 2026-11-07), not as controls, so a fresh read still finds every control
    /// under it and the press would land on the notification. False while no panel shows, and for an item that is no page control (the side
    /// menu, the top bar, a question's answers), as <see cref="CoveringList"/>.
    /// </summary>
    public static bool UnderNotice(Bbs757EfbScreenModel m, Bbs757EfbItem? item, Point at) =>
        (item == null || m.Items.Contains(item)) && m.NoticePanel is { } panel && panel.Contains(at);

    // ------------------------------------------------------------------ BlueBird's keyboard over a press (plan 22, the final review's I2)

    /// <summary>
    /// Whether BlueBird's keyboard, shown on the read <paramref name="m"/> (<see cref="Bbs757EfbScreenModel.KeyboardState"/>, ready or still
    /// sliding up), covers <paramref name="at"/>, a point a press would put the mouse down on: the numeric keypad's rectangle while the keypad
    /// shows, else the QWERTY keyboard's box (<see cref="Bbs757EfbScreenModel.Keyboard"/>, kb_grid: [0,648 1397x400]). Up, it takes a press
    /// there as a key and types into the box it serves (plan 22, the final review's I2), wherever that box is, and the window cannot show it
    /// (B757-23). False while it reads down or unread, and when the read has no box for it (the keys or the keypad's rectangle not read).
    /// Every control is checked, a dialog's answers and the side menu too: the keypad below the fuel dialog's box covers its INSERT.
    /// </summary>
    public static bool UnderKeyboard(Bbs757EfbScreenModel m, Point at) =>
        m.KeyboardState is { Shown: true } k
        && (k.Keypad ? k.KeypadBox : m.Keyboard?.Box ?? Rectangle.Empty) is { IsEmpty: false } box
        && box.Contains(at);

    // ------------------------------------------------------------------ a question

    /// <summary>A question's words as two reads compare them: its letters and digits, upper-cased invariantly. Case, spaces and
    /// punctuation wobble from one read to the next ("defuel?", "defuel ?"); the words do not. A two-choice switch's words are
    /// compared the same way (<see cref="NewChoiceWord"/>).</summary>
    public static string QuestionWords(string text) => string.Concat(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));

    /// <summary>Whether two questions are the same question: both there, and their identities the same (<see cref="QuestionIdentity"/>).
    /// Every question's answers are keyed alike (q:YES, q:NO), so an answer is the one pressed only under the same question.</summary>
    public static bool SameQuestion(Bbs757EfbQuestion? a, Bbs757EfbQuestion? b) =>
        a != null && b != null && QuestionIdentity(a) == QuestionIdentity(b);

    /// <summary>What tells a question from another: its words (<see cref="QuestionWords"/>); for one with none (no dialog measured is
    /// wordless), its text boxes' names and its answers' words, marked so they never equal any words (a lone OK is not the question
    /// "OK").</summary>
    public static string QuestionIdentity(Bbs757EfbQuestion q)
    {
        string words = QuestionWords(q.Text);
        return words.Length > 0 ? words : "|" + string.Join('|', q.Rows.Concat(q.Buttons).Select(i => QuestionWords(i.Text)));
    }

    // ------------------------------------------------------------------ a switch

    /// <summary>Whether a switch or check box needs its tap to be as the pilot wished: false only when the tablet already shows the
    /// wished state (it changed since the page was read, so a tap would undo the wish). A state the reader could not tell is not
    /// known to be as wished, so it is tapped.</summary>
    public static bool NeedsTap(Bbs757EfbItem item, bool wantOn) => item.On != wantOn;

    /// <summary>
    /// The new choice of a switch of two choices that landed, for a row the settings catalog gives no word for (a row that is no
    /// setting, such as a label read as "CPDLC netwrk", or a setting whose choice before the tap is unknown): the word the control
    /// shows after its tap (<paramref name="now"/>, what the control found again reads), when it is a word and another than the
    /// one it showed before (<paramref name="before"/>). A word here is a value with a letter or digit that, with case, spaces and
    /// punctuation left out (<see cref="QuestionWords"/>), reads neither ON nor OFF ("Hoppie", "Option 1"), and the two values
    /// are compared folded so too, so a pill that moved while its word still reads the old one gives none. That is
    /// narrower than what the window labels a switch with ("CPDLC network: Hoppie", <see cref="Bbs757EfbElements"/>), which
    /// leaves out only an empty value and one that is exactly On or Off, case ignored: a misread "On." or "o n" is in the window's
    /// label but is no word here, so the press errs to saying nothing. Null in every other case: the screen reader says an On/Off
    /// switch's new state, and a switch with no word before has none to change.
    /// </summary>
    public static string? NewChoiceWord(string before, string? now)
    {
        if (now == null) return null;
        string was = QuestionWords(before), shows = QuestionWords(now);
        return IsChoiceWord(was) && IsChoiceWord(shows) && was != shows ? now : null;
    }

    // A switch's value as QuestionWords folds it: a word of two choices, not nothing and not the On or Off of a plain switch.
    private static bool IsChoiceWord(string folded) => folded.Length > 0 && folded is not ("ON" or "OFF");

    // ------------------------------------------------------------------ a slider with choices

    /// <summary>
    /// Where on a slider's track choice <paramref name="index"/> of <paramref name="count"/> sits: the choices are spread evenly,
    /// the first at the track's start and the last at its end (measured on IRS ALIGNMENT TIME, BOARDING &amp; LOAD RATE and
    /// NOTIFICATIONS FADE-OUT TIMER, 2026-10-26). A slider with one choice is tapped at its start.
    /// </summary>
    public static int ChoiceX(Bbs757EfbSlider s, int index, int count)
    {
        if (count <= 1) return s.TrackLeft;
        return s.TrackLeft + (int)Math.Round((double)index * (s.TrackRight - s.TrackLeft) / (count - 1));
    }

    /// <summary>
    /// Where to tap a slider next, when the last tap (at <paramref name="lastX"/>, the <paramref name="tapsSoFar"/>th) left it at
    /// choice <paramref name="readIndex"/> instead of <paramref name="targetIndex"/>: a third of a choice's width toward the target
    /// (at least 4 px), kept on the track, since a tap past its end lands beside it, on another control. Null when there is no tap
    /// to make: <see cref="MaxSliderTaps"/> taps are spent, the slider's value was not read (<paramref name="readIndex"/> is
    /// negative), or it already reads the target.
    /// </summary>
    public static int? NextChoiceX(Bbs757EfbSlider s, int lastX, int targetIndex, int readIndex, int count, int tapsSoFar)
    {
        if (tapsSoFar >= MaxSliderTaps || readIndex < 0 || readIndex == targetIndex) return null;
        double choice = (double)(s.TrackRight - s.TrackLeft) / Math.Max(1, count - 1);
        int step = (int)Math.Round(Math.Max(MinStepPx, choice / 3));
        int x = lastX + (targetIndex > readIndex ? step : -step);
        return Math.Min(Math.Max(x, s.TrackLeft), s.TrackRight);
    }

    // ------------------------------------------------------------------ what is said

    /// <summary>
    /// What a button or tile press says about itself, the one speaker of it (the window says a new page, and nothing of a pressed
    /// control's words: it patches a control that kept its key silently and rebuilds one whose key changed), or null when it says
    /// nothing. From <paramref name="before"/>, the page the control was tapped on, and <paramref name="after"/>, the read after the
    /// tap:
    /// <list type="bullet">
    /// <item>the control's new words, when the read finds it again (<see cref="Relocate"/>, whatever its key or kind now) with
    /// other words: "Req fuel truck" become "Requesting…", FETCH SIMBRIEF OFP become REFRESH SIMBRIEF OFP, "Fuel truck off" become
    /// "Fuel truck on", START BOARDING become the text "Boarding… 10 min", named as the window names the control
    /// (<see cref="SpokenName"/>: a door's "Left FWD: Open"). The screen reader cannot say them (CORE-7);</item>
    /// <item><see cref="NoChangeText"/> when the press changed nothing (<see cref="PageUnchanged"/>: a new notification over the page,
    /// or a top bar that ticked, is no change of the press's);</item>
    /// <item>null when the page name changed, when a question came up (the read that found it said it, and the dimmed page under
    /// it reads otherwise: bright/21 names IMPORT FROM SIMBRIEF by the label above it), when the question an answer was pressed on
    /// went (what lay under it is another control, whatever its box), when the control is gone, and when the control kept its words
    /// while something else on the page moved.</item>
    /// </list>
    /// </summary>
    public static string? ButtonOutcome(Bbs757EfbScreenModel before, Bbs757EfbScreenModel after, Bbs757EfbItem pressed)
    {
        if (!SamePage(before, after)) return null;
        if (after.Question != null && !SameQuestion(before.Question, after.Question)) return null;
        if (before.Question?.Buttons.Contains(pressed) == true && !SameQuestion(before.Question, after.Question)) return null;
        var now = Relocate(before, after, pressed);
        if (now == null) return null;
        if (!string.Equals(now.Text, pressed.Text, StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(now.Text) ? null : SpokenName(after, now);
        if (Line(now) != Line(pressed)) return null;
        return PageUnchanged(before, after) ? NoChangeText(SpokenName(before, pressed)) : null;
    }

    /// <summary>
    /// What a tab press says (the final review's I2): the tab's name and "(current)", once, when the read after the tap finds it
    /// (<see cref="Relocate"/>) the selected one and it was not before ("Hydraulic (current)": the window patches the new label
    /// silently, so the press is its one speaker). Null for any other press, a tab that was the current one already, a page that
    /// changed its name or brought up a question, and a tab the read did not find selected: the press is then judged as a button
    /// (<see cref="ButtonOutcome"/>).
    /// </summary>
    public static string? TabOutcome(Bbs757EfbScreenModel before, Bbs757EfbScreenModel after, Bbs757EfbItem pressed)
    {
        if (pressed.Kind != Bbs757EfbKind.Tab || pressed.Selected || !SamePage(before, after)) return null;
        if (after.Question != null && !SameQuestion(before.Question, after.Question)) return null;
        return Relocate(before, after, pressed) is { Kind: Bbs757EfbKind.Tab, Selected: true } now ? SpokenName(after, now) + " (current)" : null;
    }

    /// <summary>
    /// How the press runner names a control in what it says: as the window names it (<see cref="Bbs757EfbElements.NameOf"/>, sentence
    /// case, a labelled button's label first: "Left FWD: Open", "Dirty panel states"), and a top bar control by its words as the bar
    /// shows them ("Start boarding"), as the window does.
    /// </summary>
    public static string SpokenName(Bbs757EfbScreenModel page, Bbs757EfbItem item) =>
        page.TopBar.Contains(item) ? item.Text : Bbs757EfbElements.NameOf(item);

    // Said as alerts (spoken at once, once): verbatim from the plan's speech rules, the label being the element's name as the window
    // says it (SpokenName), and a control's new words named so too (ButtonOutcome). Plain concatenation of the given words, so no
    // culture can change one.

    /// <summary>A tap that could not run.</summary>
    public const string UnreachableText = "The tablet cannot be reached, so nothing was tapped.";

    /// <summary>The control is not on the tablet any more, so nothing was tapped.</summary>
    public static string GoneText(string label) => label + " is no longer on the tablet.";

    /// <summary>A button or tile press that changed nothing on the page.</summary>
    public static string NoChangeText(string label) => label + ": no change on the tablet.";

    /// <summary>A switch or check box the tablet did not follow in time.</summary>
    public static string NotChangedText(string label) => label + " did not change.";

    /// <summary>The new choice of a switch of two choices that landed ("CPDLC network: SayIntentions"). The screen reader says only
    /// checked or not checked of its check box, which names no word (CORE-7).</summary>
    public static string ChoiceText(string label, string word) => label + ": " + word;

    /// <summary>A control the page would not scroll to where it can be tapped.</summary>
    public static string NotInViewText(string name) => name + " could not be brought into view, not pressed.";

    /// <summary>A slider that did not reach the wished choice or value, or a drop-down whose entry was not chosen.</summary>
    public static string CouldNotSetText(string label, string value) => label + " could not be set to " + value + ".";

    /// <summary>A drop-down's entry its open list does not show, or shows less than <see cref="MinShownPx"/> of (<see cref="RowTapPoint"/>),
    /// so it was not chosen: an open list scrolls by no input (measured), so the tablet cannot choose it, by anyone (the final review's M3:
    /// Surface Condition's Wet).</summary>
    public static string NotShownInListText(string label, string entry) => label + " could not be set to " + entry + ": the tablet's list does not show it.";

    /// <summary>A control another drop-down's open list covers (<see cref="CoveringList"/>), so it was not tapped.</summary>
    public static string CoveredText(string name, string list) => name + " is under the open " + list + " list, not pressed.";

    /// <summary>A control BlueBird's notification pop-up or list covers (<see cref="UnderNotice"/>), so it was not tapped.</summary>
    public static string UnderNoticeText(string name) => name + " is under a BlueBird notification, not pressed.";

    /// <summary>A control BlueBird's keyboard or keypad still covers, shown and settled, after the runner put it down once
    /// (<see cref="UnderKeyboard"/>; the re-review's N3), so it was not tapped: the tap would be a key.</summary>
    public static string UnderKeyboardText(string name) => name + " is under the tablet's keyboard, not pressed.";

    // Typing into a field (plan 22, Bbs757EfbPressRunner.TypeAsync). The value is the tablet's own text, said as it reads.

    /// <summary>A field whose tap did not bring BlueBird's keyboard up, settled, typing into it, in time: nothing was typed.</summary>
    public static string NoKeyboardText(string name) => name + ": the tablet's keyboard did not come up.";

    /// <summary>A text with a character no key of the keyboard (or the keypad) carries: nothing was typed. The character is said as
    /// <see cref="CharacterName"/> names it.</summary>
    public static string NoKeyText(string name, char character) => name + ": the tablet's keyboard has no " + CharacterName(character) + ".";

    /// <summary>
    /// A character as the no-key alert says it (the final review's M3): a space, a tab, a no-break space and every ASCII symbol NVDA does
    /// not speak at its default punctuation level ("some") in words, since the raw character would be heard as nothing ("has no  .", a
    /// comma, an underscore); anything else as it is: a letter, a digit, and the symbols NVDA speaks at that level (% &amp; * + . / = @).
    /// </summary>
    private static string CharacterName(char c) => c switch
    {
        ' ' => "space",
        '\t' => "tab",
        '\u00A0' => "no-break space",
        '!' => "exclamation mark",
        '"' => "quotation mark",
        '#' => "number sign",
        '$' => "dollar sign",
        '\'' => "apostrophe",
        '(' => "left parenthesis",
        ')' => "right parenthesis",
        ',' => "comma",
        '-' => "hyphen",
        ':' => "colon",
        ';' => "semicolon",
        '<' => "less-than sign",
        '>' => "greater-than sign",
        '?' => "question mark",
        '[' => "left bracket",
        '\\' => "backslash",
        ']' => "right bracket",
        '^' => "caret",
        '_' => "underscore",
        '`' => "backtick",
        '{' => "left brace",
        '|' => "vertical bar",
        '}' => "right brace",
        '~' => "tilde",
        _ => c.ToString(),
    };

    /// <summary>A field that reads the typed text (case aside: PERF calc's Airport box upper-cases it) once it was typed: the one
    /// confirmation of a typing (CORE-7: the screen reader said the pilot's keys in the window's edit box, never what the tablet took).</summary>
    public static string TypedText(string name, string value) => name + ": " + value;

    /// <summary>A field that reads another text than the one typed (BlueBird's boxes filter what they take, and a full box drops keys).</summary>
    public static string TypedOtherText(string name, string value) => name + " reads " + value + ".";

    /// <summary>A field that reads empty once typed: emptied as the pilot asked, or every key it was typed dropped.</summary>
    public static string EmptyText(string name) => name + " is empty.";

    /// <summary>A typing that stopped because a run of its taps did not go out: what the field reads is not said.</summary>
    public static string NotTypedText(string name) => name + " not typed.";
}
