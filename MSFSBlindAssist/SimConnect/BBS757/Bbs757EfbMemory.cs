using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Reads BlueBird's EFB tablet control tree straight from the 757 module's memory through a layout
/// (<see cref="Bbs757EfbMemLayout"/>), so the text is BlueBird's own, with
/// visibility and tap rectangles and no screen capture. Reaches the EFB object through the gauge manager, then BBOS,
/// then its app map; walks each drawn app's roots and current page (the top bar's and an open dialog's among them),
/// keeping the controls whose own visible byte (and whose ancestors') is set, with the top bar's notification panel
/// (<see cref="NoticePanel"/>) and the keyboard app's state (<see cref="KeyboardState"/>); and reads BBOS's notifications (<see cref="ReadNotices"/>). Pure over an <see cref="IEfbMemory"/> so it runs against a fake or the live
/// simulator. The std::map/std::vector/std::string shapes it reads are the Emscripten libc++ ABI, stable across
/// BlueBird builds; only the BlueBird-specific field offsets come from the layout.
/// </summary>
public sealed class Bbs757EfbMemory
{
    private readonly IEfbMemory _m;
    private readonly Bbs757EfbMemLayout _l;

    public Bbs757EfbMemory(IEfbMemory memory, Bbs757EfbMemLayout layout)
    {
        _m = memory;
        _l = layout;
    }

    /// <summary>One control read from memory: its class kind, name, text, rectangle, and child controls. <c>Y</c> is
    /// where the control is DRAWN (its rectangle less its scrollbox ancestors' offsets); <c>Scroll</c> is a
    /// scrollbox's own offset (its content offset, B757-26), 0 for any other control. <c>Knob</c> is a slider's place along
    /// its track, 0 to 1, from BlueBird's knob (<see cref="ReadKnob"/>): across a slider at least as wide as tall from its left,
    /// up an upright one from its bottom; null for a slider whose knob is unreadable and for every other kind. <c>KnobSpan</c>
    /// is the knob's size along the track in tablet pixels, 0 with no <c>Knob</c>.
    /// <para>The text: a label's or button's own, a <c>textbox</c>'s its text label's, a <c>multiline</c> box's its whole text
    /// ('\n' between lines), a <c>pulldown</c>'s its face's (the current choice as shown), an <c>image</c>'s its picture's path.
    /// <c>Selected</c> is a button's current-entry state: true for the current side-menu entry or tab, false for another entry
    /// of a menu or tab row, null for a button that is neither (its selectable byte not 1) and for every other kind. A
    /// <c>pulldown</c> has <c>Entries</c> (its entries' texts in BlueBird's order), <c>Value</c> (the entry whose text is its
    /// face's, "" when none is, as Systems' "SELECT"), <c>Open</c> (its list shown) and, open, <c>Rows</c>: its list's entry
    /// buttons where they are drawn (their rectangles less the list's and its ancestors' scroll offsets), and <c>OpenList</c>:
    /// the list's scrollbox itself, where it is drawn and with its own <c>Scroll</c>, the viewport a row shows in, without
    /// children (its rows are <c>Rows</c>); both null closed. The face, the list and a text box's label are in no children
    /// vector, so they are never <c>Children</c>.</para>
    /// <para>A <c>textbox</c> has <c>Caret</c> (the character boundary a typed character goes in at, 0 to its text's length),
    /// <c>MaxLength</c> (the most characters it takes, 1 to <see cref="MaxTextboxLength"/>) and <c>Keypad</c> (whether it opens the
    /// numeric keypad rather than the QWERTY keyboard), each null when unreadable or out of its range (a torn read), and null for every
    /// other kind (<see cref="WithTyping"/>; plan 22, measured 2026-11-07).</para>
    /// <para>A <c>toggle</c> has <c>On</c>, its own byte's low bit (1 on, 0 off; <see cref="WithToggle"/>; plan 22, measured 2026-11-07):
    /// BlueBird's own state, which a row's value label only pictures. Null when the byte cannot be read and for every other kind.</para></summary>
    public sealed record Node(string Kind, string Name, string Text, float X, float Y, float W, float H, uint Address, IReadOnlyList<Node> Children,
        float Scroll = 0f, float? Knob = null, float KnobSpan = 0f,
        bool? Selected = null, string Value = "", IReadOnlyList<string>? Entries = null, bool Open = false, IReadOnlyList<Node>? Rows = null,
        Node? OpenList = null, int? Caret = null, int? MaxLength = null, bool? Keypad = null, bool? On = null);

    /// <summary>The class of the top bar's app (BlueBird's <c>app_overlay</c>), read on every page whatever its front flag.</summary>
    public const string TopBarClass = "overlay";

    /// <summary>The class of the dialog app (BlueBird's <c>app_modaldialog</c>), read only while BBOS's dialog byte is 1.</summary>
    public const string DialogClass = "modal";

    /// <summary>The class of the keyboard app (BlueBird's <c>app_keyboard</c>, BBOS app map key 2), which holds <c>kb_grid</c> and the
    /// keyboard's state (<see cref="KeyboardState"/>). Drawn by its front flag as any app (it reads 1, measured 2026-11-07). A layout
    /// without the class reads it as the fallback <see cref="UnknownAppClass"/>, as every tree captured before plan 22 holds it.</summary>
    public const string KeyboardClass = "keyboard";

    /// <summary>The class of an app whose vtable the layout names no class for (or whose vtable cannot be read).</summary>
    public const string UnknownAppClass = "app";

    /// <summary>The cap of every std::string read but a multi-line box's text: past it a string reads as none.</summary>
    public const int TextCap = 4096;

    /// <summary>The cap of a multi-line box's text: the Operating FP measured 91,869 characters (2026-11-07).</summary>
    public const int LongTextCap = 1_048_576;

    /// <summary>The most notification records read: BlueBird kept 47 (capacity 64) when measured; a count past this is a torn read.</summary>
    private const int MaxNotices = 256;

    /// <summary>One EFB app that is currently drawn (its front flag set, the top bar always, the dialog while one is up), with
    /// the controls of its roots and its current page; the top bar's app also with its notification panel (<see cref="Panel"/>,
    /// null for every other app and when its fields cannot be read), and the keyboard app with its state (<see cref="Keyboard"/>, null
    /// for every other app and when a byte of it cannot be read).</summary>
    public sealed record App(int Key, string Class, IReadOnlyList<Node> Controls, NoticePanel? Panel = null, string FlightTime = "",
        BoardingBox? Boarding = null, KeyboardState? Keyboard = null);

    /// <summary>BlueBird's keyboard as the keyboard app keeps it (measured live 2026-11-07): <c>Shown</c>
    /// from a text box's tap until the keyboard goes down; <c>Ready</c> once it has settled, 1.12 to 1.16 s after the tap (a key tapped
    /// before lands on the page beneath); <c>Keypad</c> when the numeric keypad shows instead of the QWERTY keyboard (BlueBird leaves it 1
    /// after the keypad closes, until the QWERTY keyboard next opens: it says which shows only while <c>Shown</c>); <c>Target</c>, the
    /// name of the text box it types into ("" while down, or when its name cannot be read); the layer bytes <c>Numeric</c> (the third
    /// layer), <c>Shift</c> (the one-shot shift) and <c>Caps</c> (caps lock); and the numeric keypad's rectangle in tablet pixels
    /// ([595.5,348 360x430] under the fuel input dialog; it has no controls in any tree).
    /// <para>The fields are read one at a time, not as one snapshot: a read while the keyboard comes up or goes down can pair
    /// <c>Shown</c> with an empty <c>Target</c>, or <c>Ready</c> without <c>Shown</c>. A consumer that types needs <c>Shown</c>,
    /// <c>Ready</c> and a non-empty <c>Target</c> together.</para></summary>
    public sealed record KeyboardState(bool Shown, bool Ready, bool Keypad, string Target, bool Numeric, bool Shift, bool Caps,
        float KeypadX, float KeypadY, float KeypadW, float KeypadH);

    /// <summary>The top bar's boarding box while BlueBird draws it (null otherwise), and what decides its words (decoded from BBS752.wasm's
    /// top bar draw, 2026-11-07): the box's rectangle in tablet pixels ([1144,4 176x24] offering START BOARDING); BBOS's loading
    /// <c>State</c> (0 idle, 1 just started, 2 running, 3 paused, 4 done); whether the loading is a <c>Deboarding</c>; whether the aircraft
    /// has <c>Flown</c> since its boarding (START DEBOARDING rather than START BOARDING); and the largest of the three remaining loading times,
    /// in seconds. <see cref="Bbs757EfbTopBar.BoardingWords"/> turns it into the words BlueBird draws.</summary>
    public sealed record BoardingBox(float X, float Y, float W, float H, int State, bool Deboarding, bool Flown, long RemainingSeconds);

    /// <summary>BlueBird's notification panel as the top bar's app keeps it (measured live 2026-11-07): <c>Shown</c> while a new
    /// notification's pop-up (about 10 s) or the notification list shows, <c>ListOpen</c> while the full list is open, and the panel's
    /// rectangle in tablet pixels ([1039.75,38 349.25x118] under a pop-up, 480 tall with the list open, 0 tall closed). The top bar app
    /// draws it from these fields, over the top right of the page: it is no control, so the controls under it still read.</summary>
    public sealed record NoticePanel(bool Shown, bool ListOpen, float X, float Y, float W, float H);

    /// <summary>The drawn apps in the map's walk order, each with its visible control tree; empty when the module is not
    /// reachable (not located, or BBOS/app map not where the layout says). An app is drawn while its front flag is set, but
    /// two: the top bar's app (<see cref="TopBarClass"/>) always, though its front flag reads 0 on every page, and the dialog
    /// app (<see cref="DialogClass"/>) only while BBOS's dialog byte is 1, whatever its front flag, as a closed dialog keeps
    /// its front flag and page (measured 2026-11-07: an answered defuel question stayed "front" for hours).</summary>
    public IReadOnlyList<App> ReadFrontApps()
    {
        if (!TryBbos(out uint bbos)) return Array.Empty<App>();
        var apps = new List<App>();
        var walk = new Walk();
        foreach (var (key, app) in AppMapEntries(bbos))
        {
            string cls = ClassName(app);
            if (!Drawn(app, cls, bbos)) continue;
            var controls = new List<Node>();
            foreach (uint root in Vector(app + (uint)_l.AppRoots)) AddVisible(root, controls, walk);
            if (_m.TryReadPtr(app + (uint)_l.AppPage, out uint page) && page != 0)
                foreach (uint root in Vector(page + (uint)_l.PageChildren)) AddVisible(root, controls, walk);
            bool bar = cls == TopBarClass;
            apps.Add(new App(key, cls, controls, bar ? ReadPanel(app) : null,
                bar ? _m.ReadStdString(app + (uint)_l.OverlayFlightTimeText) ?? "" : "",
                bar ? ReadBoarding(app, bbos) : null,
                cls == KeyboardClass ? ReadKeyboard(app) : null));
        }
        return apps;
    }

    /// <summary>The keyboard app's state (<see cref="KeyboardState"/>): its bytes at the layout's <c>KeyboardShown</c>, <c>KeyboardReady</c>,
    /// <c>KeyboardKeypad</c>, <c>KeyboardNumeric</c>, <c>KeyboardShift</c> and <c>KeyboardCaps</c>, each its low bit (as BlueBird tests its
    /// flags); the name of the control its <c>KeyboardTarget</c> pointer points at ("" for 0 or a name that is no readable string); the
    /// keypad's four floats at <c>KeypadRect</c>, all four read as 0 when any one is no number. Null when a byte or the
    /// pointer cannot be read, never a state of zeros.</summary>
    private KeyboardState? ReadKeyboard(uint app)
    {
        if (!_m.TryReadByte(app + (uint)_l.KeyboardShown, out byte shown) || !_m.TryReadByte(app + (uint)_l.KeyboardReady, out byte ready)
            || !_m.TryReadByte(app + (uint)_l.KeyboardKeypad, out byte keypad) || !_m.TryReadByte(app + (uint)_l.KeyboardNumeric, out byte numeric)
            || !_m.TryReadByte(app + (uint)_l.KeyboardShift, out byte shift) || !_m.TryReadByte(app + (uint)_l.KeyboardCaps, out byte caps)
            || !_m.TryReadPtr(app + (uint)_l.KeyboardTarget, out uint target)) return null;
        string name = target == 0 ? "" : _m.ReadStdString(target + (uint)_l.CtrlName) ?? "";
        uint rect = app + (uint)_l.KeypadRect;
        float x = _m.ReadFloat(rect), y = _m.ReadFloat(rect + 4), w = _m.ReadFloat(rect + 8), h = _m.ReadFloat(rect + 12);
        // A float that is no number is a torn read: all four read 0 together (no keypad), never a rectangle made of the other three.
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(w) || !float.IsFinite(h)) x = y = w = h = 0f;
        return new KeyboardState((shown & 1) == 1, (ready & 1) == 1, (keypad & 1) == 1, name, (numeric & 1) == 1, (shift & 1) == 1,
            (caps & 1) == 1, x, y, w, h);
    }

    /// <summary>The most seconds believed for a remaining loading time (a day): a torn or foreign word cannot make a count of minutes.</summary>
    private const long MaxRemainingSeconds = 86_400;

    /// <summary>The top bar's boarding box (<see cref="BoardingBox"/>): null when BlueBird does not draw it (its rectangle at the layout's
    /// <c>OverlayBoardingRect</c>, which BlueBird zeros on every frame without the box, is not a real one) or a field cannot be read. A
    /// remaining time outside 0 to <see cref="MaxRemainingSeconds"/> counts as none.</summary>
    private BoardingBox? ReadBoarding(uint app, uint bbos)
    {
        uint rect = app + (uint)_l.OverlayBoardingRect;
        float x = _m.ReadFloat(rect), y = _m.ReadFloat(rect + 4), w = _m.ReadFloat(rect + 8), h = _m.ReadFloat(rect + 12);
        if (!(w > 0 && h > 0 && w < 2000 && h < 2000) || !float.IsFinite(x) || !float.IsFinite(y)) return null;
        if (!_m.TryReadInt(bbos + (uint)_l.BbosLoadState, out int state)
            || !_m.TryReadByte(bbos + (uint)_l.BbosLoadDeboarding, out byte deboarding)
            || !_m.TryReadByte(bbos + (uint)_l.BbosLoadFlown, out byte flown)) return null;
        long remaining = 0;
        for (uint i = 0; i < 3; i++)
        {
            uint at = bbos + (uint)_l.BbosLoadRemaining + 8 * i;
            if (!_m.TryReadInt(at, out int lo) || !_m.TryReadInt(at + 4, out int hi)) return null;
            long seconds = (long)(uint)lo | ((long)hi << 32);
            if (seconds > 0 && seconds <= MaxRemainingSeconds) remaining = Math.Max(remaining, seconds);
        }
        // BlueBird tests each byte's low bit (load8_u, and 1).
        return new BoardingBox(x, y, w, h, state, (deboarding & 1) == 1, (flown & 1) == 1, remaining);
    }

    // The top bar app's FlightTime (above) is its flight-time std::string at the layout's OverlayFlightTimeText: "DEP 00:28" around
    // departure (measured live 2026-11-07), "" when it shows nothing; read with the normal text cap.

    /// <summary>The top bar app's notification panel (<see cref="NoticePanel"/>): its two bytes and its rectangle's floats (x, y and
    /// width at the layout's <c>OverlayPanelRect</c>, the height at <c>OverlayPanelHeight</c>); null when a byte cannot be read.</summary>
    private NoticePanel? ReadPanel(uint app)
    {
        if (!_m.TryReadByte(app + (uint)_l.OverlayPanelShown, out byte shown) || !_m.TryReadByte(app + (uint)_l.OverlayListOpen, out byte list))
            return null;
        uint rect = app + (uint)_l.OverlayPanelRect;
        return new NoticePanel(shown == 1, list == 1, _m.ReadFloat(rect), _m.ReadFloat(rect + 4), _m.ReadFloat(rect + 8),
            _m.ReadFloat(app + (uint)_l.OverlayPanelHeight));
    }

    /// <summary>One reading's walk: the controls it has entered. A torn or hostile tree cannot make it loop or blow up: a control
    /// is entered once per reading however many vectors list it (a children vector listing its own control twice made about 2^65
    /// walks under the depth guard alone), and no more than <see cref="MaxNodes"/> controls are entered.</summary>
    private sealed class Walk
    {
        private readonly HashSet<uint> _entered = new();

        public bool Spent => _entered.Count >= MaxNodes;

        /// <summary>True the first time a control is met in this reading while the budget lasts; false for one met before.</summary>
        public bool Enter(uint ctrl) => !Spent && _entered.Add(ctrl);
    }

    private bool Drawn(uint app, string cls, uint bbos) => cls switch
    {
        TopBarClass => true,
        DialogClass => _m.TryReadByte(bbos + (uint)_l.BbosModalOpen, out byte open) && open == 1,
        _ => _m.TryReadInt(app + (uint)_l.AppFront, out int front) && front != 0,
    };

    /// <summary>
    /// BlueBird's notifications, oldest record first: BBOS's std::vector (begin, end) at the layout's <c>BbosNotices</c>, records
    /// <c>NoticeStride</c> bytes apart, each its id, title, text, time and unread byte. An empty list when BBOS holds none (a whole
    /// vector with no record, or one never allocated: begin and end both 0, as a fresh BBOS starts). Null, not read, when the module is
    /// not reachable and from a torn vector: no buffer under an end, its end before its start or not a whole number of records from it,
    /// more than <see cref="MaxNotices"/> records, or a record whose id or unread byte cannot be read; so a reader can tell "BlueBird has
    /// none" from "none could be read" (<see cref="Bbs757EfbScreenModel.NoticesWhole"/>). A string that is not a readable one reads "".
    /// </summary>
    public IReadOnlyList<Bbs757EfbNoticeRecord>? ReadNotices()
    {
        if (!TryBbos(out uint bbos) || _l.NoticeStride <= 0) return null;
        uint vec = bbos + (uint)_l.BbosNotices;
        if (!_m.TryReadPtr(vec, out uint begin) || !_m.TryReadPtr(vec + 4, out uint end)) return null;
        if (begin == 0 && end == 0) return Array.Empty<Bbs757EfbNoticeRecord>();
        if (begin == 0 || end < begin) return null;
        uint stride = (uint)_l.NoticeStride, bytes = end - begin;
        if (bytes % stride != 0 || bytes / stride > MaxNotices) return null;
        var notices = new List<Bbs757EfbNoticeRecord>();
        for (uint r = begin; r < end; r += stride)
        {
            if (!_m.TryReadInt(r + (uint)_l.NoticeId, out int id) || !_m.TryReadByte(r + (uint)_l.NoticeUnread, out byte unread)) return null;
            notices.Add(new Bbs757EfbNoticeRecord(id, _m.ReadStdString(r + (uint)_l.NoticeTitle) ?? "",
                _m.ReadStdString(r + (uint)_l.NoticeText) ?? "", _m.ReadStdString(r + (uint)_l.NoticeTime) ?? "", unread == 1));
        }
        return notices;
    }

    /// <summary>BBOS: the pointer at the EFB object's <c>EfbToBbos</c>.</summary>
    private bool TryBbos(out uint bbos)
    {
        bbos = 0;
        return TryEfb(out uint efb) && _m.TryReadPtr(efb + (uint)_l.EfbToBbos, out bbos) && bbos != 0;
    }

    /// <summary>The EFB object: the first gauge-manager pointer whose object carries the efb vptr.</summary>
    public bool TryEfb(out uint efb)
    {
        efb = 0;
        for (uint off = 0; off + 4 <= _l.ManagerSpan; off += 4)
        {
            if (!_m.TryReadPtr(_l.ManagerBase + off, out uint p) || p == 0) continue;
            if (_m.TryReadPtr(p, out uint vptr) && vptr == _l.EfbVptr) { efb = p; return true; }
        }
        return false;
    }

    // ---- the control tree ----------------------------------------------------

    /// <summary>The largest scroll offset believed: a torn or foreign float cannot move a page by more.</summary>
    private const float MaxScroll = 100_000f;

    /// <summary>The deepest a walk goes (the tablet's trees are about 15 deep): a torn chain of distinct controls ends here, not
    /// in a stack overflow.</summary>
    internal const int MaxDepth = 64;

    /// <summary>The most controls one reading enters, hidden ones included (the largest committed reading has 230 visible ones:
    /// Settings, Callouts, with the keyboard's 53): a torn tree wide past it is cut there.</summary>
    internal const int MaxNodes = 20_000;

    /// <summary>Adds the control, with its visible descendants, when it and its own visible byte are readable and set; once per
    /// reading (<see cref="Walk"/>) and no deeper than <see cref="MaxDepth"/>.</summary>
    /// <param name="ancestorScroll">The sum of the offsets of the scrollboxes above this control; its rectangle
    /// never moves, but it is drawn that much higher.</param>
    private void AddVisible(uint ctrl, List<Node> into, Walk walk, float ancestorScroll = 0f, int depth = 0)
    {
        if (depth > MaxDepth || ctrl == 0 || !walk.Enter(ctrl)) return;
        if (!_m.TryReadByte(ctrl + (uint)_l.CtrlVisible, out byte vis) || (vis & 1) == 0) return;
        string kind = KindOf(ctrl);
        float scroll = kind == "scrollbox" ? ReadScroll(ctrl) : 0f;
        var children = new List<Node>();
        foreach (uint ch in Vector(ctrl + (uint)_l.CtrlChildren))
        {
            if (walk.Spent) break;
            AddVisible(ch, children, walk, ancestorScroll + scroll, depth + 1);
        }
        var node = ReadNode(ctrl, kind, children, ancestorScroll, scroll);
        into.Add(kind switch
        {
            "button" => node with { Selected = ReadSelected(ctrl) },
            "pulldown" => WithEntries(node, ctrl, walk, ancestorScroll, depth),
            "textbox" => WithTyping(node, ctrl),
            "toggle" => WithToggle(node, ctrl),
            _ => node,
        });
    }

    /// <summary>A toggle with its own state (<see cref="Node.On"/>): the low bit of its byte at the layout's <c>ToggleOn</c> (as BlueBird
    /// tests its flags; +0x189 beside it reads 1 on every toggle, so only the low bit of +0x188 is the state). Null when it cannot be
    /// read.</summary>
    private Node WithToggle(Node node, uint ctrl) =>
        node with { On = _m.TryReadByte(ctrl + (uint)_l.ToggleOn, out byte on) ? (on & 1) == 1 : null };

    /// <summary>The longest maximum length believed for a text box (SayIntentions' ACARS key box, 64, is the longest measured): a torn
    /// or foreign word past it reads as none.</summary>
    internal const int MaxTextboxLength = 1000;

    /// <summary>A text box with its caret, maximum length and keypad flag (<see cref="Node"/>): the ints at the layout's
    /// <c>TextboxCaret</c>, <c>TextboxMaxLength</c> and <c>TextboxKeypad</c> (measured live 2026-11-07).
    /// Each is null when it cannot be read; a maximum length outside 1 to <see cref="MaxTextboxLength"/> and a keypad int other than 0 (the
    /// QWERTY keyboard) or 1 (the numeric keypad) read as none too, a torn read. A caret outside 0 to the text's length is kept inside it
    /// (the final review's M4): a text BlueBird sets itself may leave the caret where a longer one had it, the planner clamps it anyway, and
    /// dropping it would make the text box a line in the window.</summary>
    private Node WithTyping(Node node, uint ctrl)
    {
        int? caret = _m.TryReadInt(ctrl + (uint)_l.TextboxCaret, out int c) ? Math.Clamp(c, 0, node.Text.Length) : null;
        int? maxLength = _m.TryReadInt(ctrl + (uint)_l.TextboxMaxLength, out int m) && m is >= 1 and <= MaxTextboxLength ? m : null;
        bool? keypad = _m.TryReadInt(ctrl + (uint)_l.TextboxKeypad, out int k) && k is 0 or 1 ? k == 1 : null;
        return node with { Caret = caret, MaxLength = maxLength, Keypad = keypad };
    }

    private string KindOf(uint ctrl) =>
        (_m.TryReadPtr(ctrl, out uint vptr) ? _l.KindFor(vptr) : null) ?? "other";

    /// <summary>A scrollbox's offset: 0 when unreadable or not finite, clamped to 0..<see cref="MaxScroll"/>.</summary>
    private float ReadScroll(uint ctrl)
    {
        float v = _m.ReadFloat(ctrl + (uint)_l.ScrollboxOffset);
        return float.IsFinite(v) ? Math.Clamp(v, 0f, MaxScroll) : 0f;
    }

    private Node ReadNode(uint ctrl, string kind, IReadOnlyList<Node> children, float ancestorScroll, float scroll)
    {
        string name = _m.ReadStdString(ctrl + (uint)_l.CtrlName) ?? "";
        float X = _m.ReadFloat(ctrl + (uint)_l.CtrlRect), Y = _m.ReadFloat(ctrl + (uint)_l.CtrlRect + 4);
        float W = _m.ReadFloat(ctrl + (uint)_l.CtrlRect + 8), H = _m.ReadFloat(ctrl + (uint)_l.CtrlRect + 12);
        var (knob, span) = kind == "slider" ? ReadKnob(ctrl, X, Y, W, H) : (null, 0f);
        return new Node(kind, name, TextOf(ctrl, kind), X, Y - ancestorScroll, W, H, ctrl, children, scroll, knob, span);
    }

    /// <summary>A control's text by its kind (<see cref="Node"/>); "" when it is not a readable string.</summary>
    private string TextOf(uint ctrl, string kind) => kind switch
    {
        // A text box holds no text itself: its text label, outside every children vector, does.
        "textbox" => _m.TryReadPtr(ctrl + (uint)_l.TextboxLabel, out uint label) && label != 0
            ? _m.ReadStdString(label + (uint)_l.CtrlLabelText) ?? "" : "",
        "multiline" => _m.ReadStdString(ctrl + (uint)_l.MultilineText, LongTextCap) ?? "",
        "pulldown" => FaceText(ctrl),
        // A button keeps its text at CtrlButtonText, a label (and an image its path) at CtrlLabelText; the other field is an
        // empty string (not null), so take the first non-empty of the two.
        _ => NonEmpty(_m.ReadStdString(ctrl + (uint)_l.CtrlButtonText)) ?? NonEmpty(_m.ReadStdString(ctrl + (uint)_l.CtrlLabelText)) ?? "",
    };

    /// <summary>A drop-down's face's text, the current choice as shown: its face is a button outside every children vector.</summary>
    private string FaceText(uint ctrl) =>
        _m.TryReadPtr(ctrl + (uint)_l.PulldownFace, out uint face) && face != 0 ? _m.ReadStdString(face + (uint)_l.CtrlButtonText) ?? "" : "";

    /// <summary>A button's current-entry state (<see cref="Node.Selected"/>): null unless its selectable byte is 1, then whether its
    /// unselected byte is 0 (measured 2026-11-07 on side menus and tab rows; a tile that takes no tap has 0 in both).</summary>
    private bool? ReadSelected(uint ctrl)
    {
        if (!_m.TryReadByte(ctrl + (uint)_l.ButtonSelectable, out byte selectable) || selectable != 1) return null;
        return _m.TryReadByte(ctrl + (uint)_l.ButtonUnselected, out byte unselected) ? unselected == 0 : null;
    }

    /// <summary>
    /// A drop-down with its entries' texts (its vector of entry records, in BlueBird's order: the list's drawn order), its value
    /// (the entry whose text is the face's; it has no index field) and whether its list is open. Open, its rows are the entry
    /// buttons of its list (a scrollbox) > its stack > the buttons, and its open list the scrollbox itself (its drawn rectangle and
    /// own offset, the rows' viewport). Both are read as any control inside a scrollbox is: the rows drawn the list's offset and
    /// the drop-down's ancestors' higher, the list its ancestors' (the page's scroll taken off them is logic only: no drop-down read
    /// so far sits in a scrollbox). The list is entered once per reading, as every control is, so a list that points back up
    /// the tree reads none. Closed, BlueBird leaves the rows at [0,0 50x38], so none are read.
    /// </summary>
    private Node WithEntries(Node node, uint ctrl, Walk walk, float ancestorScroll, int depth)
    {
        var entries = new List<string>();
        foreach (uint record in Vector(ctrl + (uint)_l.PulldownEntries))
            entries.Add(_m.ReadStdString(record + (uint)_l.PulldownEntryText) ?? "");
        bool open = _m.TryReadByte(ctrl + (uint)_l.PulldownOpen, out byte b) && b == 1;
        List<Node>? rows = null;
        Node? openList = null;
        if (open)
        {
            var list = new List<Node>();
            if (_m.TryReadPtr(ctrl + (uint)_l.PulldownList, out uint scrollbox)) AddVisible(scrollbox, list, walk, ancestorScroll, depth + 1);
            rows = list.SelectMany(l => l.Children).SelectMany(stack => stack.Children).Where(r => r.Kind == "button").ToList();
            if (list.Count == 1) openList = list[0] with { Children = Array.Empty<Node>() };
        }
        return node with { Value = entries.Contains(node.Text) ? node.Text : "", Entries = entries, Open = open, Rows = rows, OpenList = openList };
    }

    /// <summary>The slack a knob's place may sit past either end of its track and still be read (then as that end).</summary>
    private const float KnobSlack = 0.01f;

    /// <summary>
    /// A slider's place along its track, 0 to 1, from its knob's rectangle (four floats at the layout's <c>SliderKnob</c>: x, y,
    /// width, height, in the slider rectangle's own coordinates, so a scroll moves neither), and the knob's size along the track.
    /// Across a slider at least as wide as tall: (knob x - X) / (W - knob width). Upright: 1 - (knob y - Y) / (H - knob height),
    /// its bottom 0 (measured 2026-11-07: Systems' zoom "+" moves the knob up). <paramref name="x"/>..<paramref name="h"/> are the
    /// slider's rectangle as stored, before any scroll. No place (null, 0) when a float is not finite, when the knob is not
    /// smaller than the slider along its track or of negative size, or when the place is more than <see cref="KnobSlack"/> past
    /// either end; within that, the end.
    /// </summary>
    private (float? Place, float Span) ReadKnob(uint ctrl, float x, float y, float w, float h)
    {
        uint at = ctrl + (uint)_l.SliderKnob;
        float kx = _m.ReadFloat(at), ky = _m.ReadFloat(at + 4), kw = _m.ReadFloat(at + 8), kh = _m.ReadFloat(at + 12);
        if (!float.IsFinite(kx) || !float.IsFinite(ky) || !float.IsFinite(kw) || !float.IsFinite(kh)
            || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(w) || !float.IsFinite(h)) return (null, 0f);
        bool upright = h > w;
        float span = upright ? kh : kw, track = (upright ? h : w) - span;
        if (span < 0 || track <= 0) return (null, 0f);
        float place = upright ? 1 - (ky - y) / track : (kx - x) / track;
        if (!float.IsFinite(place) || place < -KnobSlack || place > 1 + KnobSlack) return (null, 0f);
        return (Math.Clamp(place, 0f, 1f), span);
    }

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private string ClassName(uint obj) =>
        _m.TryReadPtr(obj, out uint vptr) ? _l.KindFor(vptr) ?? UnknownAppClass : UnknownAppClass;

    // ---- BBOS app map (libc++ std::map<int, std::vector<app*>>) --------------

    /// <summary>The app map's (key, app pointer) entries. The map node is libc++'s: left/right/parent at 0/4/8,
    /// the key int at 0x10, the value vector (begin,end) at 0x14; the vector holds 8-byte entries (app*, pad).</summary>
    private IEnumerable<(int Key, uint App)> AppMapEntries(uint bbos)
    {
        uint header = bbos + (uint)_l.BbosToAppMap;
        if (!_m.TryReadPtr(header + 4, out uint root) || root == 0) yield break;
        if (!_m.TryReadInt(header + 8, out int size) || size is <= 0 or > 64) yield break;
        var seen = new HashSet<uint>();
        var stack = new Stack<uint>();
        stack.Push(root);
        int guard = 0;
        while (stack.Count > 0 && guard++ < 128)
        {
            uint n = stack.Pop();
            if (n == 0 || !seen.Add(n)) continue;
            if (!_m.TryReadInt(n + 0x10, out int key)) continue;
            uint vb = 0, ve = 0;
            if (_m.TryReadPtr(n + 0x14, out vb) && _m.TryReadPtr(n + 0x18, out ve) && vb != 0 && ve > vb && ve - vb <= 0x400)
                for (uint p = vb; p + 4 <= ve; p += 8)
                    if (_m.TryReadPtr(p, out uint app) && app != 0) yield return (key, app);
            if (_m.TryReadPtr(n, out uint l)) stack.Push(l);
            if (_m.TryReadPtr(n + 4, out uint r)) stack.Push(r);
        }
    }

    /// <summary>A libc++ std::vector of pointers stored in 8-byte entries (pointer, padding): the pointers in
    /// [begin, end), capped so a torn read cannot loop.</summary>
    private IEnumerable<uint> Vector(uint vec)
    {
        if (!_m.TryReadPtr(vec, out uint begin) || !_m.TryReadPtr(vec + 4, out uint end)) yield break;
        if (begin == 0 || end <= begin || end - begin > 0x4000) yield break;
        for (uint p = begin; p + 4 <= end; p += 8)
            if (_m.TryReadPtr(p, out uint e) && e != 0) yield return e;
    }
}

/// <summary>One of BlueBird's notifications as BBOS keeps it (<see cref="Bbs757EfbMemory.ReadNotices"/>): its id, rising with each
/// new one; its title, empty but for a few (the autobrake ones); its text ("GPU CONNECTED."); its time ("21:37Z"); and whether
/// the pilot has not yet opened the notification list since it came.</summary>
public sealed record Bbs757EfbNoticeRecord(int Id, string Title, string Text, string Time, bool Unread);

/// <summary>Read-only access to the located 757 module's memory, by wasm address, for the EFB reader. The live
/// implementation wraps <see cref="ModuleMemory"/>; tests supply a fake.</summary>
public interface IEfbMemory
{
    bool TryReadInt(uint wasmAddress, out int value);
    bool TryReadByte(uint wasmAddress, out byte value);
    float ReadFloat(uint wasmAddress);

    /// <summary>A pointer-sized (32-bit wasm) read; false outside the module.</summary>
    bool TryReadPtr(uint wasmAddress, out uint value);

    /// <summary>A libc++ std::string at this address (short-string optimisation or long form); null when it is not
    /// a readable printable string or is longer than <paramref name="maxLength"/> characters.</summary>
    string? ReadStdString(uint wasmAddress, int maxLength = Bbs757EfbMemory.TextCap);
}

/// <summary>
/// <see cref="IEfbMemory"/> over a located <see cref="ModuleMemory"/>. The std::string read is the Emscripten
/// libc++ 12-byte layout this build uses (validated 2026-11-06): byte +11's high bit is the long flag; long form
/// is pointer +0, size +4; short form stores the size in +11's low 7 bits with the data from +0.
/// </summary>
public sealed class ModuleEfbMemory : IEfbMemory
{
    private readonly ModuleMemory _memory;

    public ModuleEfbMemory(ModuleMemory memory) => _memory = memory;

    public bool TryReadInt(uint wasmAddress, out int value) => _memory.TryReadInt32(wasmAddress, out value);
    public bool TryReadByte(uint wasmAddress, out byte value) => _memory.TryReadByte(wasmAddress, out value);

    public bool TryReadPtr(uint wasmAddress, out uint value)
    {
        value = 0;
        if (!_memory.TryReadInt32(wasmAddress, out int v)) return false;
        value = unchecked((uint)v);
        return true;
    }

    public float ReadFloat(uint wasmAddress) =>
        _memory.TryReadInt32(wasmAddress, out int v) ? BitConverter.Int32BitsToSingle(v) : 0f;

    public string? ReadStdString(uint wasmAddress, int maxLength = Bbs757EfbMemory.TextCap)
    {
        Span<byte> head = stackalloc byte[12];
        if (!_memory.TryRead(wasmAddress, head)) return null;
        bool isLong = (head[11] & 0x80) != 0;
        if (isLong)
        {
            uint ptr = (uint)(head[0] | head[1] << 8 | head[2] << 16 | head[3] << 24);
            int size = head[4] | head[5] << 8 | head[6] << 16 | head[7] << 24;
            if (size <= 0 || size > maxLength || ptr == 0) return null;
            var buffer = new byte[size];
            if (!_memory.TryRead(ptr, buffer)) return null;
            return Printable(buffer);
        }
        int shortSize = head[11] & 0x7f;
        if (shortSize is <= 0 or > 10) return shortSize == 0 ? "" : null;
        return Printable(head[..shortSize]);
    }

    /// <summary>The bytes as text when every one is printable ASCII (or tab/newline); null otherwise, so a
    /// non-string field never reads as garbage.</summary>
    private static string? Printable(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
            if (!(b is >= 0x20 and < 0x7f || b is 9 or 10 or 13)) return null;
        return System.Text.Encoding.ASCII.GetString(bytes);
    }
}
