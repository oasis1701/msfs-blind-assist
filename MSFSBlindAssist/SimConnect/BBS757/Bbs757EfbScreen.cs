using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What an element on the tablet is.</summary>
public enum Bbs757EfbKind
{
    /// <summary>Plain text (a read-out, a caption, a note).</summary>
    Text,
    /// <summary>A section heading inside a page (unboxed short capitals above a group).</summary>
    Heading,
    /// <summary>An entry of the page's side menu, or a home-screen app.</summary>
    Menu,
    /// <summary>A boxed control: a button or a tile.</summary>
    Button,
    /// <summary>A two-state switch (a settings row, the checklist's INTERACTIVE).</summary>
    Toggle,
    /// <summary>A slider (a settings row, a cabin light).</summary>
    Slider,
    /// <summary>A checklist line with its box.</summary>
    Checkbox,
    /// <summary>A settings row with neither (a read-only value or a text field).</summary>
    Row,
    /// <summary>A value box, or a text box: Text is its name, Value its value (the text it holds).</summary>
    Field,
    /// <summary>A tab of a page; Selected is the current one.</summary>
    Tab,
    /// <summary>A long text: Text is the whole text, its lines separated by <c>\n</c>.</summary>
    LongText,
    /// <summary>A drop-down: Text is its name, Value its current entry ("" when it shows none of them, as Systems' SELECT), and
    /// <see cref="Bbs757EfbItem.Entries"/> its entries in BlueBird's order.</summary>
    Choice,
}

/// <summary>The color of a box: on BlueBird's tablet the color IS the state of a tile.</summary>
public enum Bbs757EfbTint { None, Blue, Red, Green, Grey, Other }

/// <summary>
/// A slider's geometry, in tablet pixels: where its track runs and where its knob is. A track runs from <see cref="TrackLeft"/>, its
/// 0 end, to <see cref="TrackRight"/>, its 100 end, and <see cref="KnobX"/> is the knob's place along it: x left to right for a
/// slider wider than tall (every settings slider, the cabin lights, Weight &amp; balance), and y for a <see cref="Vertical"/> one (the
/// hydraulic reservoirs, Systems' zoom), whose 0 end is its bottom (measured 2026-11-07: Systems' zoom "+" moves the knob up, and a
/// tap 25% from the bottom set 25.7%) and which is tapped at <see cref="AcrossX"/>, its track's x middle. When the knob is BlueBird's
/// own (<see cref="KnobRead"/>), the track is the range of the knob's center, since a tap centers the knob on the tap point (measured).
/// </summary>
public sealed record Bbs757EfbSlider(int TrackLeft, int TrackRight, int KnobX)
{
    /// <summary>Whether the track runs up and down (the slider's box is taller than wide).</summary>
    public bool Vertical { get; init; }

    /// <summary>Whether <see cref="KnobX"/> is where BlueBird's knob is, read from its memory, and the track the range of the knob's
    /// center; false when the knob is placed from the value the tablet shows (or at the start), on the track the slider's box spans.</summary>
    public bool KnobRead { get; init; }

    /// <summary>A vertical track's x middle, where it is tapped; 0 for a horizontal one.</summary>
    public int AcrossX { get; init; }

    /// <summary>The place along the track of <paramref name="percent"/> (0 to 100) of it.</summary>
    public int At(double percent) => TrackLeft + (int)Math.Round((TrackRight - TrackLeft) * percent / 100.0);

    /// <summary>How far along the track <paramref name="place"/> is, in percent, 0 to 100; 0 for a track of no length.</summary>
    public double PercentOf(int place)
    {
        if (TrackRight == TrackLeft) return 0;
        double percent = 100.0 * (place - TrackLeft) / (TrackRight - TrackLeft);
        return percent <= 0 ? 0 : percent >= 100 ? 100 : percent;   // never -0 (a vertical track's 0 end divides by a negative length)
    }

    private static readonly System.Text.RegularExpressions.Regex Count =
        new(@"^\s*(\d+)\s*/\s*(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The part of its track a slider's shown value says, in percent: a percent ("70%", "40"), or a count of its most ("8/16", Weight
    /// &amp; balance's passengers, as their titles show them); null for any other value ("100 ft", "OFF", nothing) and one past 0 to
    /// 100. Numbers read as another program writes them (CORE-17).
    /// </summary>
    public static double? PercentShown(string value)
    {
        var count = Count.Match(value);
        if (count.Success)
        {
            if (!int.TryParse(count.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
                || !int.TryParse(count.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int most)
                || most <= 0 || n > most) return null;
            return 100.0 * n / most;
        }
        string text = value.Trim().TrimEnd('%').Trim();
        return MSFSBlindAssist.Utils.NumberParsing.TryParseExternal(text, out double v) && double.IsFinite(v) && v is >= 0 and <= 100 ? v : null;
    }
}

/// <summary>
/// One element of the page. <see cref="Hit"/> is where to tap it (tablet pixels, the agent's click
/// coordinates); <see cref="Clickable"/> false means it is not a control (a read-out, a credential).
/// <see cref="Level"/> is a heading's level (2 or 3), 0 otherwise. <see cref="Label"/> is the name the page grammar gave a
/// control whose own words are a state or an action ("NOSE DOOR" for the button "OPEN"); <see cref="Name"/> is BlueBird's
/// own name for the control.
/// <see cref="Key"/> is stable while the element's state changes (its label without
/// the state word), so the window keeps the screen reader's focus on it across a refresh.
/// <see cref="Clip"/> is the drawn rectangle of the scrollbox the control sits in (its nearest <c>*_scrollbox</c> ancestor, within
/// any scrollbox above that one), the part of the tablet it can show in: the reader sets <see cref="Bbs757EfbScreen.PageArea"/>
/// for a control in no scrollbox, and an item built with none (the default) is read as the content area
/// (<see cref="Bbs757EfbPress.ClipOf"/>).
/// <see cref="Entries"/> are a <see cref="Bbs757EfbKind.Choice"/>'s entries, in BlueBird's order, null for any other kind;
/// <see cref="Open"/> says its list is shown, and <see cref="List"/> is that list as drawn (null while it is closed, or open with its
/// list not read). Neither is in the page's fingerprint: the window shows a drop-down as a choice list, open or not.
/// <see cref="TextState"/> is a <see cref="Bbs757EfbKind.Field"/>'s caret, maximum length and keypad flag when it is one of BlueBird's
/// text boxes and all three were read (plan 22), null otherwise; in the fingerprint only whether it is set (the window shows such a field
/// as an edit box, any other as a line), never the caret, which moves while the pilot types.
/// <see cref="Unit"/> is the unit BlueBird draws beside a dialog's text box ("Gal" beside the fuel input dialog's box), "" for any other
/// item: the box's <see cref="Value"/> is its own text alone ("752"), so a typing compares and reads back what the box holds, and the window
/// names it with the unit after its name ("Fuel (Gal)", plan 22, Task 5).
/// <see cref="ValueBox"/> marks a <see cref="Bbs757EfbKind.Field"/> that is one of BlueBird's value boxes: a word-less button holding one
/// label, its number (R7: <c>fuel_left_qty_box</c>, <c>wb_pax_business_weight_box</c>). A tap on those two opened BlueBird's fuel input
/// dialog (measured 2026-11-07); the window shows every value box as a button whose press taps it, as a sighted pilot would; it has no
/// caret and is never typed into.
/// </summary>
public sealed record Bbs757EfbItem(
    Bbs757EfbKind Kind,
    string Text,
    Rectangle Box,
    Point Hit,
    bool Clickable,
    string Key,
    Bbs757EfbTint Tint = Bbs757EfbTint.None,
    bool? On = null,
    bool Selected = false,
    string Value = "",
    Bbs757EfbSlider? Slider = null,
    int Level = 0,
    string Label = "",
    string Name = "",
    Rectangle Clip = default,
    IReadOnlyList<string>? Entries = null,
    bool Open = false,
    Bbs757EfbOpenList? List = null,
    Bbs757EfbTextState? TextState = null,
    string Unit = "",
    bool ValueBox = false);

/// <summary>
/// One of BlueBird's text boxes as the keyboard types into it (plan 22, measured live 2026-11-07):
/// <see cref="Caret"/>, the character boundary a typed character goes in at (0 to the text's length; a tap puts it at the nearest
/// boundary, a typed character then at the end; backspace deletes before it, <c>btn_delete</c> after); <see cref="MaxLength"/>, the most
/// characters it takes (a key tapped on a full box is dropped: Airport 4, the fuel dialog 8, SayIntentions 64); and <see cref="Keypad"/>,
/// whether it opens the numeric keypad rather than the QWERTY keyboard.
/// </summary>
public sealed record Bbs757EfbTextState(int Caret, int MaxLength, bool Keypad);

/// <summary>
/// BlueBird's keyboard as its app keeps it (plan 22, measured live 2026-11-07), beside
/// <see cref="Bbs757EfbScreenModel.Keyboard"/>, which says only where its keys are: <see cref="Shown"/> from a text box's tap until it goes
/// down; <see cref="Ready"/> once it has settled, 1.12 to 1.16 s after the tap (a key tapped before lands on the page beneath);
/// <see cref="Keypad"/> when the numeric keypad shows instead of the QWERTY keyboard (meaningful only while <see cref="Shown"/>: BlueBird
/// leaves it set after the keypad closes); <see cref="Target"/>, BlueBird's name for the text
/// box it types into ("" while down); <see cref="Numeric"/>, <see cref="Shift"/> and <see cref="Caps"/>, the third layer, the one-shot
/// shift and caps lock; and <see cref="KeypadBox"/>, where the numeric keypad is drawn (it has no controls in any tree), empty when its
/// rectangle read is no box on the tablet (no width or height, or not finite).
/// <para>The fields are read one at a time, not as one snapshot: a read while the keyboard comes up or goes down can pair
/// <see cref="Shown"/> with an empty <see cref="Target"/>, or <see cref="Ready"/> without <see cref="Shown"/>. A consumer that types
/// needs <see cref="Shown"/>, <see cref="Ready"/> and a non-empty <see cref="Target"/> together.</para>
/// </summary>
public sealed record Bbs757EfbKeyboardState(bool Shown, bool Ready, bool Keypad, string Target, bool Numeric, bool Shift, bool Caps,
    Rectangle KeypadBox);

/// <summary>
/// A drop-down's list while it is open (plan 21, Task 7; measured 2026-11-07, [B757-27]): the part
/// of the tablet its rows show in (<see cref="Viewport"/>, the drawn rectangle of its <c>item_scrollbox</c>: the runway list's 519,189
/// 150x228) and its rows in the list's order, one per entry, each with its words and where it is drawn now (the list's own scroll and the
/// page's taken off: the runway list's rows 38 px apart from y 189). A list taller than its cap does not scroll: its box clips the rows
/// past it, and no input measured scrolls it (2026-11-07, PERF calc's Surface list: Wet's row lies past the box, so it cannot be chosen;
/// B757-27).
/// </summary>
public sealed record Bbs757EfbOpenList(Rectangle Viewport, IReadOnlyList<Bbs757EfbListRow> Rows);

/// <summary>One row of an open drop-down's list: the entry's words it shows and where it is drawn.</summary>
public sealed record Bbs757EfbListRow(string Text, Rectangle Box);

/// <summary>A parsed tablet page: which app and menu entry, then its elements in reading order.</summary>
public sealed record Bbs757EfbScreenModel(
    string App,
    string Section,
    IReadOnlyList<Bbs757EfbItem> Menu,
    IReadOnlyList<Bbs757EfbItem> Items)
{
    /// <summary>Whether the page scrolls: a scrollbox of its content wraps a taller stackbox (its content does not fit the
    /// <see cref="Viewport"/>). The presses scroll a control of that scrollbox into view by dragging.</summary>
    public bool Scrollable { get; init; }

    /// <summary>The tablet's top bar, the window's status bar: from memory, the top bar app's labels (the time line, keyed
    /// <c>top:time_label</c>, <see cref="Bbs757EfbTopBar.KeyFor"/>); plan 18's models carry the departure countdown, the boarding control and
    /// the notification control, keyed <c>top:dep</c>, <c>top:boarding</c> and <c>top:notifications</c> whatever their words (see
    /// <see cref="Bbs757EfbTopBar"/>). Empty when none shows.</summary>
    public IReadOnlyList<Bbs757EfbItem> TopBar { get; init; } = Array.Empty<Bbs757EfbItem>();

    /// <summary>BlueBird's notifications, newest first (<see cref="Bbs757EfbNotice"/>, by id); empty when it has none, or when none could
    /// be read (<see cref="NoticesWhole"/>).</summary>
    public IReadOnlyList<Bbs757EfbNotice> Notices { get; init; } = Array.Empty<Bbs757EfbNotice>();

    /// <summary>Whether <see cref="Notices"/> is BlueBird's whole list: false when BBOS's vector read torn
    /// (<see cref="Bbs757EfbMemory.ReadNotices"/> gave none), so an empty <see cref="Notices"/> says nothing of what BlueBird holds.</summary>
    public bool NoticesWhole { get; init; } = true;

    /// <summary>A question the tablet shows over the page (BlueBird's dialog), with its answers (not in <see cref="Items"/>); null when
    /// none is up.</summary>
    public Bbs757EfbQuestion? Question { get; init; }

    /// <summary>The part of the tablet BlueBird's notification pop-up or notification list covers while it shows, in tablet pixels
    /// (the top bar app's panel, <see cref="Bbs757EfbMemory.NoticePanel"/>: about 1039,38 350x118 under a pop-up, 480 tall with the list
    /// open); null while none shows or none was read. BlueBird draws it over the page from its own fields, so the controls under it still
    /// read: a press there would land on the notification (<see cref="Bbs757EfbPress.UnderNotice"/>). Not in the fingerprint: it comes and
    /// goes over any page, as the notifications do.</summary>
    public Rectangle? NoticePanel { get; init; }

    /// <summary>The part of the tablet the page scrolls in, in tablet pixels: the drawn rectangle of the one scrollbox that scrolls
    /// (<see cref="Scrollable"/>), else the content area. Each control is shown, and tapped, within its own <see cref="Bbs757EfbItem.Clip"/>;
    /// only a control whose clip is this rectangle is dragged into view.</summary>
    public Rectangle Viewport { get; init; } = Bbs757EfbScreen.ContentArea;

    /// <summary>Where BlueBird's QWERTY keyboard keys are, null when the reader found none. BlueBird's keyboard tree reads
    /// visible whether it shows or not (measured 2026-11-06), so this is only where the keys are, never "the keyboard is up"
    /// (that is <see cref="KeyboardState"/>).</summary>
    public Bbs757EfbKeyboard? Keyboard { get; init; }

    /// <summary>Whether BlueBird's keyboard is up and ready, which text box it types into, and its layer (<see cref="Bbs757EfbKeyboardState"/>),
    /// from the keyboard app's own fields; null when none was read (a layout without the keyboard's fields, or a torn read). Not in the
    /// fingerprint: nothing on the page reads differently while the keyboard is up.</summary>
    public Bbs757EfbKeyboardState? KeyboardState { get; init; }

    /// <summary>"Aircraft, Ground services" / "Home".</summary>
    public string PageName =>
        App.Length == 0 ? "Unknown page"
        : Section.Length == 0 ? Bbs757EfbVocabulary.Pretty(App)
        : Bbs757EfbVocabulary.Pretty(App) + ", " + Bbs757EfbVocabulary.Pretty(Section);
}

/// <summary>One of BlueBird's notifications (<see cref="Bbs757EfbNotices.From"/>): its id, BlueBird's own, rising with each new one, which
/// tells one appearance from another however alike their words; its words as the tablet writes them, made readable ("GPU connected.",
/// a title before them when it has one: "Autobrake 1: TARGET 4.5 | ACTUAL 4.5 ft/s2"); and its time ("14:17Z"), null when none was
/// read.</summary>
public sealed record Bbs757EfbNotice(int Id, string Text, string? Time);

/// <summary>BlueBird's keyboard: the box it covers and its keys.</summary>
public sealed record Bbs757EfbKeyboard(Rectangle Box, IReadOnlyList<Bbs757EfbKey> Keys);

/// <summary>One key: BlueBird's name (<c>char_btn_q|Q|1</c> is Lower "q", Upper "Q", Third "1"; a command key such as
/// <c>btn_enter</c> has its name and empty layers) and where to tap it.</summary>
public sealed record Bbs757EfbKey(string Name, string Lower, string Upper, string Third, Point Hit);

/// <summary>A box the tablet shows over its page (BlueBird's dialog) with words and its answers (YES and NO, OK, CANCEL and INSERT): the
/// words, then the answers as buttons, left to right. <see cref="Rows"/> are the text boxes it holds (the fuel input dialog's quantity),
/// each a <see cref="Bbs757EfbKind.Field"/> holding its text, with the unit beside it as its <see cref="Bbs757EfbItem.Unit"/>; the window
/// shows one whose caret was read as an edit box the pilot types into (plan 22).</summary>
public sealed record Bbs757EfbQuestion(string Text, IReadOnlyList<Bbs757EfbItem> Buttons)
{
    /// <summary>The dialog's text boxes, in its reading order; none for a question of words and answers alone.</summary>
    public IReadOnlyList<Bbs757EfbItem> Rows { get; init; } = Array.Empty<Bbs757EfbItem>();
}

/// <summary>
/// The constants and the key/state-word helpers the memory tablet reader (<see cref="Bbs757EfbModelReader"/>) and the
/// window share: the side-menu boundary, the window's own Home button, and how a tile's key carries
/// its words. (The EFB is read from the module's memory, so only these shared pieces live here.)
/// </summary>
public static class Bbs757EfbScreen
{
    /// <summary>The part of the tablet that shows a page's content (right of the side menu, below the top bar and the app's title
    /// row).</summary>
    public static readonly Rectangle ContentArea = new(250, 82, 1147, 966);

    /// <summary>The part of the tablet an app draws its page in, right of the side menu and below the top bar (0 to 32): the content
    /// area with the app's title row above it (32 to 82), where Systems keeps its page menu (<c>pb_menu1</c>, 1097,32 300x50; plan 21,
    /// Task 7). A page control in no scrollbox is clipped to it, so a control of the title row is tapped where it is drawn.</summary>
    public static readonly Rectangle PageArea = new(250, 32, 1147, 1016);

    /// <summary>Controls left of this (tablet x) are the side menu.</summary>
    public const int MenuRight = 252;

    /// <summary>The home icon left of every app's title: the window's own Home button taps it, as the reader offers
    /// no control for the icon.</summary>
    public static readonly Point HomeButton = new(19, 62);

    /// <summary>The words a tile's text ends in that say its state, not its name ("FUEL TRUCK OFF" is FUEL TRUCK): the
    /// key strips them so a press that changes a tile's state keeps its key, and <see cref="Bbs757EfbPress.Relocate"/>
    /// leaves them out when it compares a tile's words with another's. "IN PLACE" is the chocks tile's other state (the picture's, joined to
    /// the tile's words by <see cref="Bbs757EfbPageRules.PictureState"/>). "MOVING" is a door button's state while the door moves
    /// (measured 2026-11-07: Cabin &amp; doors' AFT cargo between CLOSED and OPENED), so the button keeps its key and its label; "OPENED"
    /// is the door's state once open (the owner's in-sim check, 2026-11-07: without it the button read "Opened" alone).</summary>
    internal static readonly string[] StateWords = { "OFF", "ON", "REMOVED", "CONNECTED", "OPEN", "OPENED", "CLOSED", "ATTACHED", "UNAVAIL", "IN PLACE", "MOVING" };

    /// <summary>The words that make a control's own words an action, not its name (plan 20, R5): such a control is named by a label
    /// (<see cref="Bbs757EfbItem.Label"/>), as one whose words are a state word is.</summary>
    internal static readonly string[] ActionWords = { "OPEN", "CLOSE", "CLOSED", "LOGOUT", "UPDATE", "REMOVE", "RESET", "FAIL NOW", "SET UP", "REPAIR", "REPAIR ALTN" };

    /// <summary>The words a box's key carries, between its caption and its place ("btn:LATEST CYCLE|UPDATE@59,14" is UPDATE,
    /// "btn:|FUEL TRUCK@56,28" is FUEL TRUCK): the box's own words without their state word, never the caption or the row
    /// label the reader may name the box by; "" for a key that is no box's (a switch, the top bar, a menu entry).</summary>
    internal static string KeyWords(string key) =>
        key.StartsWith("btn:", StringComparison.Ordinal) ? key.Substring(4).Split('|').Last().Split('@')[0] : "";
}
