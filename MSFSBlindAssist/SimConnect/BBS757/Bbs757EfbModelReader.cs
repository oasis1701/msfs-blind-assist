using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Turns the EFB memory walk (<see cref="Bbs757EfbMemory"/>) into the window's model (<see cref="Bbs757EfbScreenModel"/>),
/// the reader of the tablet (<see cref="Bbs757EfbScreen"/> keeps its shared key helpers). The front content app's page becomes the model:
/// its side menu (<c>GENERAL</c>, <c>FUEL</c>, …) as <see cref="Bbs757EfbKind.Menu"/> entries, the home screen's five app
/// icons as menu entries that navigate, and the page's own controls as items. The keys it gives match the schemes the
/// press runner, the elements builder and the settings matcher already speak (<c>app:</c>, <c>menu:</c>,
/// <c>btn:…|words@x,y</c>), so the press runner reads them as they are; a control's name in BlueBird's tree is
/// its stable identity, kept as the key of a settings row so a re-read relocates it exactly.
///
/// Settings rows (a grid holding a toggle or slider with its label and value) become one Toggle or Slider item carrying
/// the value the tablet shows and the switch state: the toggle's own byte (<see cref="Bbs757EfbMemory.Node.On"/>, BlueBird's own state)
/// when the walk read it, else the one its value label gives. BlueBird's text boxes become fields holding their text,
/// its multi-line text boxes long texts and its drop-downs choices (plan 21). The current section, also the selected side-menu entry,
/// and a tab row's current tab are the ones BlueBird's selected state marks (<see cref="Bbs757EfbMemory.Node.Selected"/>,
/// <see cref="SectionOf"/>); a read that marks none (a tree captured before plan 21) names no section and no current tab. Whether the
/// page scrolls, and its viewport, come from the one scrollbox holding a taller stackbox, and each item is clipped to the scrollbox it
/// sits in. Beside the page (plan 21, Task 6): the top bar app's labels are the status bar, BBOS's notification records the
/// notifications, the dialog app, while one is up, the question over the page, and the top bar app's notification panel, while it
/// shows, the part of the tablet a notification covers (<see cref="Bbs757EfbScreenModel.NoticePanel"/>). Pure over the walk
/// (<see cref="Read(IReadOnlyList{Bbs757EfbMemory.App}, IReadOnlyList{Bbs757EfbNoticeRecord})"/>): tested against synthetic trees and
/// the committed trees.
/// </summary>
public sealed class Bbs757EfbModelReader
{
    private readonly Bbs757EfbMemory _memory;

    public Bbs757EfbModelReader(Bbs757EfbMemory memory) => _memory = memory;

    private const int MenuRight = Bbs757EfbScreen.MenuRight; // controls left of this are the side menu

    /// <summary>The App tokens whose app is a content screen (the reader's class comes from the app's vtable, which
    /// the layout maps to these tokens); "HOME" is the home screen, handled apart.</summary>
    private static readonly HashSet<string> ContentApps = new(StringComparer.Ordinal)
        { "AIRCRAFT", "NAVIGATION", "SYSTEMS", "CHECKLIST", "SETTINGS", "SIGN IN" };

    /// <summary>The current page as the window's model, with BlueBird's notifications; null when the EFB is not reachable (not located
    /// yet).</summary>
    public Bbs757EfbScreenModel? Read() => Read(_memory.ReadFrontApps(), _memory.ReadNotices());

    /// <summary>The page the front apps show, as the window's model, with no notifications; null when none is drawn. Pure, for the tests.</summary>
    public static Bbs757EfbScreenModel? Read(IReadOnlyList<Bbs757EfbMemory.App> apps) => Read(apps, Array.Empty<Bbs757EfbNoticeRecord>());

    /// <summary>The page the front apps show, as the window's model, with the status bar (<see cref="StatusBarOf"/>), BlueBird's
    /// notifications <paramref name="notices"/> (<see cref="Bbs757EfbNotices.From"/>; null when they could not be read, a torn vector:
    /// none, and <see cref="Bbs757EfbScreenModel.NoticesWhole"/> false) and the dialog over the page (<see cref="QuestionOf"/>); null
    /// when no page is drawn. Pure, for the tests.</summary>
    public static Bbs757EfbScreenModel? Read(IReadOnlyList<Bbs757EfbMemory.App> apps, IReadOnlyList<Bbs757EfbNoticeRecord>? notices)
    {
        if (apps.Count == 0) return null;
        apps = ImagesAsOther(apps);
        return PageOf(apps) is { } page
            ? page with
            {
                TopBar = StatusBarOf(apps), Notices = Bbs757EfbNotices.From(notices ?? Array.Empty<Bbs757EfbNoticeRecord>()),
                NoticesWhole = notices is not null, Question = QuestionOf(apps), Keyboard = KeyboardOf(apps),
                KeyboardState = KeyboardStateOf(apps), NoticePanel = NoticePanelOf(apps),
            }
            : null;
    }

    /// <summary>
    /// The keyboard's state (<see cref="Bbs757EfbScreenModel.KeyboardState"/>) from the keyboard app's (<see cref="Bbs757EfbMemory.KeyboardState"/>),
    /// its keypad's rectangle truncated to whole pixels as every box the reader gives (<see cref="Box"/>), or empty for one that is not
    /// finite, lies past <see cref="MaxPanelPx"/> (a torn read) or has no width or no height (as <see cref="NoticePanelOf"/> treats its panel);
    /// null when none was read.
    /// </summary>
    private static Bbs757EfbKeyboardState? KeyboardStateOf(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        if (apps.Select(a => a.Keyboard).FirstOrDefault(k => k is not null) is not { } k) return null;
        static bool Sane(float v) => float.IsFinite(v) && Math.Abs(v) <= MaxPanelPx;
        var keypad = Sane(k.KeypadX) && Sane(k.KeypadY) && Sane(k.KeypadW) && Sane(k.KeypadH) && k.KeypadW > 0 && k.KeypadH > 0
            ? new Rectangle((int)k.KeypadX, (int)k.KeypadY, (int)k.KeypadW, (int)k.KeypadH)
            : Rectangle.Empty;
        return new Bbs757EfbKeyboardState(k.Shown, k.Ready, k.Keypad, k.Target, k.Numeric, k.Shift, k.Caps, keypad);
    }

    /// <summary>
    /// The part of the tablet BlueBird's notification panel covers (<see cref="Bbs757EfbScreenModel.NoticePanel"/>): the top bar app's
    /// panel rectangle while its shown byte is 1 (a pop-up or the list), rounded outward to whole pixels so no point the panel touches is
    /// left out; null while it is not shown, when it was not read, and for a rectangle that is not finite, lies past
    /// <see cref="MaxPanelPx"/> (a torn read) or has no area (closed, its height reads 0).
    /// </summary>
    private static Rectangle? NoticePanelOf(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        if (apps.FirstOrDefault(a => a.Class == Bbs757EfbMemory.TopBarClass)?.Panel is not { Shown: true } p) return null;
        static bool Sane(float v) => float.IsFinite(v) && Math.Abs(v) <= MaxPanelPx;
        if (!Sane(p.X) || !Sane(p.Y) || !Sane(p.W) || !Sane(p.H) || p.W <= 0 || p.H <= 0) return null;
        int left = (int)Math.Floor(p.X), top = (int)Math.Floor(p.Y);
        return Rectangle.FromLTRB(left, top, (int)Math.Ceiling(p.X + p.W), (int)Math.Ceiling(p.Y + p.H));
    }

    /// <summary>The farthest a notification panel's or the keypad's float is believed from 0, in tablet pixels (the tablet is 1397x1058).</summary>
    private const float MaxPanelPx = 100_000f;

    private static Bbs757EfbScreenModel? PageOf(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        // The content app: the front content app with the most visible controls (the one drawn on top).
        var content = apps
            .Where(a => ContentApps.Contains(a.Class) && Count(a.Controls) > 0)
            .OrderByDescending(a => Count(a.Controls))
            .FirstOrDefault();

        if (content is null)
        {
            var home = apps.FirstOrDefault(a => a.Class == "HOME" && Count(a.Controls) > 0);
            return home is null ? null : HomeModel(home);
        }

        var grammar = new Grammar(content);
        string section = SectionOf(content.Class, grammar);
        var menu = new List<Bbs757EfbItem>();
        var items = new List<Bbs757EfbItem>();
        // A control in no scrollbox shows anywhere the app draws, its title row included (Systems' page menu, plan 21 Task 7).
        EmitChildren(content.Controls, content, grammar, menu, items, Bbs757EfbScreen.PageArea, parent: null);
        var scroller = PageScrollbox(content);
        return new Bbs757EfbScreenModel(content.Class, section, menu, Bbs757EfbPageRules.Reorder(content.Class, section, items))
        {
            Scrollable = scroller is not null,
            Viewport = scroller is null ? Bbs757EfbScreen.ContentArea : Within(Box(scroller), Bbs757EfbScreen.ContentArea),
        };
    }

    /// <summary>BlueBird's image class reads as "other" did before plan 21's layout knew it (its text is its picture's path: no line,
    /// unless the page rules give its file a state, which joins the button in its box: the chocks, R10). Text boxes, multi-line text
    /// boxes and drop-downs keep their kinds (<see cref="EmitNode"/>). The top bar's and the dialog's apps
    /// (<see cref="Bbs757EfbMemory.TopBarClass"/>, <see cref="Bbs757EfbMemory.DialogClass"/>) are no content app, home screen or
    /// keyboard app: they are read apart (<see cref="StatusBarOf"/>, <see cref="QuestionOf"/>).</summary>
    private static IReadOnlyList<Bbs757EfbMemory.App> ImagesAsOther(IReadOnlyList<Bbs757EfbMemory.App> apps) =>
        apps.Select(a => a with { Controls = a.Controls.Select(ImageAsOther).ToList() }).ToList();

    private static Bbs757EfbMemory.Node ImageAsOther(Bbs757EfbMemory.Node n) =>
        n with { Kind = n.Kind == "image" ? "other" : n.Kind, Children = n.Children.Select(ImageAsOther).ToList() };

    /// <summary>BlueBird's keyboard from the keyboard app's <c>kb_grid</c>: its box and its keys in tree order. A <c>char_btn_</c> key
    /// (and the comma and period keys, whose words are <c>,|,|!</c> and <c>.|.|?</c>) carries three layers split on <c>|</c>; a
    /// <c>btn_*</c> command key keeps its name and empty layers. Null without a <c>kb_grid</c>. The grid reads visible whether the
    /// keyboard shows or not, so this says where the keys are, never that the keyboard is up (<see cref="KeyboardStateOf"/> does). The
    /// keyboard app is <see cref="Bbs757EfbMemory.KeyboardClass"/> under a layout that knows its class (plan 22), the fallback class
    /// <see cref="Bbs757EfbMemory.UnknownAppClass"/> under one that does not and in every tree captured before.</summary>
    private static Bbs757EfbKeyboard? KeyboardOf(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        var grid = apps.Where(a => a.Class is Bbs757EfbMemory.KeyboardClass or Bbs757EfbMemory.UnknownAppClass).SelectMany(a => Flatten(a.Controls))
            .FirstOrDefault(n => n.Name == "kb_grid");
        if (grid is null) return null;
        var keys = new List<Bbs757EfbKey>();
        foreach (var k in Descendants(grid).Where(n => n.Kind == "button"))
        {
            string[] layers = k.Name.StartsWith("char_btn_", StringComparison.Ordinal) ? k.Name["char_btn_".Length..].Split('|') : k.Text.Split('|');
            keys.Add(layers.Length == 3
                ? new Bbs757EfbKey(k.Name, layers[0], layers[1], layers[2], Hit(k))
                : new Bbs757EfbKey(k.Name, "", "", "", Hit(k)));
        }
        return new Bbs757EfbKeyboard(Box(grid), keys);
    }

    // ---- the status bar and the dialog (plan 21, Task 6) ------------------------

    /// <summary>
    /// The status bar: the top bar app's (<see cref="Bbs757EfbMemory.TopBarClass"/>, read on every page) visible labels with words, in
    /// tree order, each a read-out in BlueBird's words as presented, keyed by its control name (<see cref="Bbs757EfbTopBar.KeyFor"/>) so
    /// its key stays as its words change: the time line ("Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C", <c>time_label</c>), the
    /// departure label when it shows (<c>etd_label</c>), and KEYBOARD LOCKED while BlueBird writes it (<c>keyboard_locked_label</c>, under
    /// the fuel input dialog). A label of marks alone (the "|" before the departure label) is a separator, no line. Then what the top bar
    /// app draws from its own fields, not controls: the departure countdown and the boarding box (<see cref="Bbs757EfbMemory.BoardingBox"/>,
    /// in the words <see cref="Bbs757EfbTopBar.BoardingWords"/> gives). The notification button is not read here (its notifications are
    /// read from their records).
    /// </summary>
    private static IReadOnlyList<Bbs757EfbItem> StatusBarOf(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        var bar = apps.Where(a => a.Class == Bbs757EfbMemory.TopBarClass).ToList();
        var items = bar
            .SelectMany(a => Flatten(a.Controls))
            .Where(n => n.Kind == "label" && n.Text.Any(char.IsLetterOrDigit) && !IsNoise(n))
            .Select(n => new Bbs757EfbItem(Bbs757EfbKind.Text, n.Text, Box(n), Hit(n), false, Bbs757EfbTopBar.KeyFor(n.Name), Name: n.Name))
            .ToList();
        // The departure countdown is no label: the top bar app keeps it as a string of its own (measured 2026-11-07), read after the
        // time line. "DEP 00:28" is plan 18's departure item (its words, the sign kept), any other words as written; empty adds nothing.
        string flight = bar.Select(a => a.FlightTime.Trim()).FirstOrDefault(t => t.Length > 0) ?? "";
        if (flight.Length > 0)
        {
            string? dep = Bbs757EfbTopBar.DepartureText(flight);
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Text, dep ?? flight, Rectangle.Empty, Point.Empty, false,
                dep != null ? Bbs757EfbTopBar.DepartureKey : Bbs757EfbTopBar.FlightTimeKey));
        }
        // The boarding box is no control either: the top bar app draws it from its own rectangle and BBOS's loading state (decoded
        // 2026-11-07), right of the countdown. Read in BlueBird's words: a button while it offers to start, tapped at its middle; text
        // while a loading runs or is paused.
        var box = bar.Select(a => a.Boarding).FirstOrDefault(b => b is not null);
        if (box is not null && Bbs757EfbTopBar.BoardingText(Bbs757EfbTopBar.BoardingWords(box)) is var (isButton, text))
            items.Add(new Bbs757EfbItem(isButton ? Bbs757EfbKind.Button : Bbs757EfbKind.Text, text,
                new Rectangle((int)box.X, (int)box.Y, (int)box.W, (int)box.H), new Point((int)(box.X + box.W / 2), (int)(box.Y + box.H / 2)),
                isButton, Bbs757EfbTopBar.BoardingKey));
        return items;
    }

    /// <summary>
    /// The dialog over the page: the dialog app (<see cref="Bbs757EfbMemory.DialogClass"/>), which the walk returns only while BBOS's
    /// dialog byte says one is up (a closed dialog keeps its page and visible bytes, measured 2026-11-07), as a question; null when it
    /// is not there or shows nothing. Its words are its labels with words, in tree order, their lines and runs of spaces read as one
    /// space (<c>text_label</c>, the fuel input dialog's <c>fuel_input_prompt_label</c>; an empty label, an image's path, a separator and
    /// a text box's unit are none). Its answers are the buttons under its <c>btn_grid</c> (YES and NO, OK, CANCEL and INSERT), keyed
    /// <c>q:</c> and their words (<c>q:YES</c>), each tapped at the middle of its drawn rectangle. A text box in it is a row
    /// (<see cref="Bbs757EfbQuestion.Rows"/>): a field named by its own name's words (<see cref="OwnName"/>: <c>fuel_input_textbox</c> is
    /// FUEL) holding its own text ("752"), with the unit beside it as its <see cref="Bbs757EfbItem.Unit"/> (the label whose name is the
    /// box's with <c>unit</c>: <c>fuel_input_unit_label</c> "Gal"), kept apart so a typing compares and reads back what the box holds
    /// (plan 22, Task 5).
    /// </summary>
    private static Bbs757EfbQuestion? QuestionOf(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        var dialog = apps.FirstOrDefault(a => a.Class == Bbs757EfbMemory.DialogClass && a.Controls.Count > 0);
        if (dialog is null) return null;
        var all = Flatten(dialog.Controls).ToList();
        var labels = all.Where(n => n.Kind == "label" && n.Text.Any(char.IsLetterOrDigit) && !IsNoise(n)).ToList();
        var units = new HashSet<Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance);
        var rows = new List<Bbs757EfbItem>();
        foreach (var box in all.Where(n => n.Kind == "textbox"))
        {
            var unit = labels.FirstOrDefault(l => !units.Contains(l) && IsUnitOf(l, box));
            if (unit is not null) units.Add(unit);
            rows.Add(new Bbs757EfbItem(Bbs757EfbKind.Field, OwnName(box.Name), Box(box), Hit(box), true, box.Name, Value: box.Text, Name: box.Name,
                TextState: TextStateOf(box), Unit: unit?.Text ?? ""));
        }
        var answerGrid = all.FirstOrDefault(n => n.Name == "btn_grid");
        var answers = answerGrid is null ? new List<Bbs757EfbItem>() : Descendants(answerGrid)
            .Where(b => b.Kind == "button" && b.Text.Any(char.IsLetterOrDigit))
            .Select(b => new Bbs757EfbItem(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(OneLine(b.Text)), Box(b), Hit(b), true,
                "q:" + OneLine(b.Text).ToUpperInvariant(), Name: b.Name))
            .ToList();
        var inAnswers = answerGrid is null ? new HashSet<Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance)
            : new HashSet<Bbs757EfbMemory.Node>(Descendants(answerGrid), ReferenceEqualityComparer.Instance);
        string words = string.Join(' ', labels.Where(l => !units.Contains(l) && !inAnswers.Contains(l)).Select(l => OneLine(l.Text)));
        if (words.Length == 0 && rows.Count == 0 && answers.Count == 0) return null;
        return new Bbs757EfbQuestion(words, answers) { Rows = rows };
    }

    /// <summary>Whether <paramref name="label"/> is the unit BlueBird draws beside the text box <paramref name="box"/>: its name holds
    /// <c>unit</c>, and without it, its role and control words is the box's (<c>fuel_input_unit_label</c> and <c>fuel_input_textbox</c>
    /// are both FUEL).</summary>
    private static bool IsUnitOf(Bbs757EfbMemory.Node label, Bbs757EfbMemory.Node box)
    {
        if (!Bbs757EfbPageRules.NameParts(label.Name).Contains("unit")) return false;
        string own = string.Join(' ', Bbs757EfbPageRules.ControlKey(label.Name).Split(' ').Where(p => p != "unit"));
        return own.Length > 0 && own == Bbs757EfbPageRules.ControlKey(box.Name);
    }

    /// <summary>A text box's caret, maximum length and keypad flag as a field carries them (<see cref="Bbs757EfbItem.TextState"/>): all
    /// three the walk read (<see cref="Bbs757EfbMemory.Node"/>), or none.</summary>
    private static Bbs757EfbTextState? TextStateOf(Bbs757EfbMemory.Node box) =>
        box.Caret is int caret && box.MaxLength is int maxLength && box.Keypad is bool keypad ? new Bbs757EfbTextState(caret, maxLength, keypad) : null;

    /// <summary>A text's lines and runs of spaces as one space each, trimmed.</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A control's name from its own control name, for a slider, text box or drop-down no label names (R11): its parts
    /// without the control words, upper-cased (<c>planning_charts_zoom_slider</c> is PLANNING CHARTS ZOOM, <c>map_provider_dropdown</c>
    /// MAP PROVIDER); the whole name when nothing is left.</summary>
    private static string OwnName(string name)
    {
        string own = string.Join(' ', Bbs757EfbPageRules.NameParts(name).Where(p => !Bbs757EfbPageRules.ControlWords.Contains(p))).ToUpperInvariant();
        return own.Length > 0 ? own : name;
    }

    // ---- the home screen -----------------------------------------------------

    /// <summary>The home screen: the five app icons as menu entries that navigate (<c>app:AIRCRAFT</c>), so a press
    /// takes the tablet into that app.</summary>
    private static Bbs757EfbScreenModel HomeModel(Bbs757EfbMemory.App home)
    {
        var tiles = new List<Bbs757EfbItem>();
        foreach (var n in Flatten(home.Controls))
        {
            if (n.Kind != "button" || IsChrome(n)) continue;
            string app = AppToken(n);
            if (app.Length == 0) continue;
            tiles.Add(new Bbs757EfbItem(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(app), Box(n), Hit(n), true, "app:" + app));
        }
        return new Bbs757EfbScreenModel("HOME", "", Array.Empty<Bbs757EfbItem>(), tiles);
    }

    /// <summary>The app a home icon opens, from its control name (<c>aircraft_btn</c> → AIRCRAFT) or its words; "" when
    /// it names none of the apps.</summary>
    private static string AppToken(Bbs757EfbMemory.Node n)
    {
        string fromName = n.Name.EndsWith("_btn", StringComparison.Ordinal)
            ? n.Name[..^4].Replace("_", " ").ToUpperInvariant() : "";
        foreach (var a in Bbs757EfbVocabulary.Apps)
            if (string.Equals(a, fromName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, n.Text, StringComparison.OrdinalIgnoreCase)) return a;
        return "";
    }

    // ---- flattening a content page -------------------------------------------

    /// <summary>One node and what is under it, as items; each item the node itself makes is clipped to <paramref name="clip"/>, the
    /// drawn rectangle of the scrollbox it sits in (the content area in none), and a scrollbox's own descendants to that scrollbox.</summary>
    private static void Emit(Bbs757EfbMemory.Node n, Bbs757EfbMemory.App content, Grammar g, List<Bbs757EfbItem> menu,
        List<Bbs757EfbItem> items, Rectangle clip)
    {
        int first = items.Count;
        EmitNode(n, content, g, menu, items, IsScrollbox(n) ? Within(Box(n), clip) : clip);
        Clip(items, first, clip);
    }

    private static void EmitNode(Bbs757EfbMemory.Node n, Bbs757EfbMemory.App content, Grammar g, List<Bbs757EfbItem> menu,
        List<Bbs757EfbItem> items, Rectangle inner)
    {
        if (IsChrome(n)) return; // the home button and the page title: the window adds its own Home and shows the page name

        // A settings row: a grid that directly holds a toggle or slider, with its label and value beside it; then a text box the row shows
        // beside its switch (TOD pause's distance, shown while the switch is on: plan 22, measured 2026-11-07), named by the row's label.
        if (n.Kind == "grid" && n.Children.FirstOrDefault(c => c.Kind is "toggle" or "slider") is { } control)
        {
            var row = Row(n, control);
            items.Add(row);
            foreach (var box in Descendants(n).Where(c => c.Kind == "textbox"))
                items.Add(g.InputItem(box, row.Text));
            return;
        }

        // A side-menu entry: a control in the left column (an app's section list, the checklist's sections).
        if (IsMenu(n, content.Class) && n.Kind is "button" or "toggle")
        {
            EmitMenu(n, content.Class, g, menu, items);
            return;
        }

        switch (n.Kind)
        {
            case "button":
                if (g.FieldButtons.Contains(n)) { items.Add(g.FieldItem(n)); return; } // a value box: its number is the field's value
                if (g.ButtonItem(n) is { } button) items.Add(button);
                return;
            case "toggle":
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Toggle, n.Text, Box(n), Hit(n), true, n.Name, On: n.On ?? StateOf(n.Text), Value: n.Text));
                return;
            case "slider":
                items.Add(g.SliderItem(n));
                return;
            case "textbox":
            case "pulldown":
                // A text box or a drop-down a title names reads where the title is (EmitText); any other one, here.
                if (!g.TitledInputs.Contains(n)) items.Add(g.InputItem(n, g.InputNameOf(n)));
                return;
            case "multiline":
                // A multi-line text box (the Operating FP, Weather's report, the ACARS route): its whole text, every line of it.
                if (n.Text.Length > 0) items.Add(new Bbs757EfbItem(Bbs757EfbKind.LongText, n.Text, Box(n), Hit(n), false, KeyOf(n), Name: n.Name));
                return;
            case "label":
            case "other":
                if (n.Text.Length > 0 && n.Children.Count == 0) EmitText(n, g, items);
                EmitChildren(n.Children, content, g, menu, items, inner, n);
                return;
            default: // grid, colorbox, canvas, gradientbox, stack, pan_indicator, scrollbox: containers
                EmitChildren(n.Children, content, g, menu, items, inner, n);
                return;
        }
    }

    /// <summary>The items from <paramref name="first"/> on that have no clip yet get <paramref name="clip"/>.</summary>
    private static void Clip(List<Bbs757EfbItem> items, int first, Rectangle clip)
    {
        for (int i = first; i < items.Count; i++)
            if (items[i].Clip == default) items[i] = items[i] with { Clip = clip };
    }

    /// <summary>A scrollbox's drawn rectangle kept within the scrollbox around it (top and bottom only: pages scroll up and down), so a
    /// control in a scrollbox scrolled out of another is never shown, nor tapped, outside either.</summary>
    private static Rectangle Within(Rectangle box, Rectangle outer)
    {
        int top = Math.Max(box.Top, outer.Top), bottom = Math.Min(box.Bottom, outer.Bottom);
        return new Rectangle(box.X, top, box.Width, Math.Max(0, bottom - top));
    }

    /// <summary>A scrollbox: BlueBird's scrollbox class, or a control named <c>*_scrollbox</c> (the committed trees predate the class).</summary>
    private static bool IsScrollbox(Bbs757EfbMemory.Node n) =>
        n.Kind == "scrollbox" || n.Name.EndsWith("_scrollbox", StringComparison.Ordinal);

    /// <summary>The children of a container in order; the buttons of a tab row (two or more siblings named as
    /// <see cref="Bbs757EfbPageRules.IsTabName"/> says) come out as <see cref="Bbs757EfbKind.Tab"/> items keyed
    /// <c>tab:&lt;parent's name&gt;|&lt;words&gt;</c>, the current one selected. Every item is clipped to <paramref name="clip"/>, the
    /// scrollbox the children sit in. <paramref name="parent"/> is the container they are the children of (null for the page's roots).</summary>
    private static void EmitChildren(IReadOnlyList<Bbs757EfbMemory.Node> kids, Bbs757EfbMemory.App content, Grammar g,
        List<Bbs757EfbItem> menu, List<Bbs757EfbItem> items, Rectangle clip, Bbs757EfbMemory.Node? parent)
    {
        // A card's own title reads before the rest of its card (the final review's M3: Maintenance's REPAIR ALTN comes before
        // maintenance_gear_doors_title in maintenance_gear_doors_canvas). Its own: the one heading named for the container
        // (Bbs757EfbPageRules.TitleKeyOf); a heading among a list's rows (Callouts) or a page's headings (Cockpit presets) stays put.
        if (parent is not null && Bbs757EfbPageRules.TitleKeyOf(parent.Name) is { Length: > 0 } own)
        {
            var heads = kids.Where(k => g.LevelOf.ContainsKey(k) && Bbs757EfbPageRules.ReadName(k.Name).CoreKey == own).ToList();
            if (heads.Count == 1 && !ReferenceEquals(kids[0], heads[0]))
                kids = heads.Concat(kids.Where(k => !ReferenceEquals(k, heads[0]))).ToList();
        }
        var tabs = kids.Where(k => k.Kind == "button" && k.Text.Length > 0 && Bbs757EfbPageRules.IsTabName(k.Name)).ToList();
        if (tabs.Count < 2)
        {
            foreach (var k in kids) Emit(k, content, g, menu, items, clip);
            return;
        }
        var current = TheOneSelected(tabs);
        foreach (var k in kids)
        {
            if (!tabs.Contains(k)) { Emit(k, content, g, menu, items, clip); continue; }
            string words = string.Join(' ', k.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Tab, Bbs757EfbVocabulary.Pretty(words), Box(k), Hit(k), true,
                "tab:" + ParentNameOf(content, k) + "|" + words, Selected: ReferenceEquals(k, current), Name: k.Name, Clip: clip));
        }
    }

    /// <summary>The control a tab button sits in (the scope of the tab's key), found from the content's tree.</summary>
    private static string ParentNameOf(Bbs757EfbMemory.App content, Bbs757EfbMemory.Node child)
    {
        foreach (var n in Flatten(content.Controls))
            if (n.Children.Any(c => ReferenceEquals(c, child))) return n.Name;
        return "";
    }

    /// <summary>The one of <paramref name="buttons"/> BlueBird's selected state marks current (<see cref="Bbs757EfbMemory.Node.Selected"/>:
    /// a tab row's current tab, a side menu's current entry); null when none is, or two or more are (a torn read names none). A read
    /// with no selected state (a tree captured before plan 21, or a row BlueBird keeps none for: Failures' tabs) marks none.</summary>
    private static Bbs757EfbMemory.Node? TheOneSelected(IEnumerable<Bbs757EfbMemory.Node> buttons)
    {
        var selected = buttons.Where(b => b.Selected == true).Take(2).ToList();
        return selected.Count == 1 ? selected[0] : null;
    }

    /// <summary>A left-column control: a side-menu entry (<c>menu:GENERAL</c>, selected when it is the one BlueBird's selected state
    /// marks current, <see cref="Grammar.CurrentEntry"/>), or the checklist's own RESET ALL button and INTERACTIVE switch, which sit in
    /// that column but are not sections.</summary>
    private static void EmitMenu(Bbs757EfbMemory.Node n, string app, Grammar g, List<Bbs757EfbItem> menu, List<Bbs757EfbItem> items)
    {
        if (n.Kind == "toggle")
        {
            // The checklist's INTERACTIVE switch has no value label; its state is its own byte's when the walk read it (n.On), else none.
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Toggle, Bbs757EfbVocabulary.Pretty(Clean(n.Text)), Box(n), Hit(n), true,
                "tgl:" + Clean(n.Text), On: n.On));
            return;
        }
        string word = MenuWord(app, n.Text);
        if (IsChecklistReset(app, n)) // the checklist's RESET ALL is a button, not a section
        {
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(word), Box(n), Hit(n), true, "btn:RESET ALL"));
            return;
        }
        bool selected = ReferenceEquals(n, g.CurrentEntry);
        menu.Add(new Bbs757EfbItem(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(word), Box(n), Hit(n), true, "menu:" + word, Selected: selected));
    }

    /// <summary>Whether a left-column control is the checklist's RESET ALL: a button in the side column, read selected (measured
    /// 2026-11-07), but no section. The one test of it, so the menu and the current entry never disagree on which control it is.</summary>
    private static bool IsChecklistReset(string app, Bbs757EfbMemory.Node n) => MenuWord(app, n.Text) == "RESET ALL";

    /// <summary>A page button or tile: keyed <c>btn:|words@x,y</c> so the press runner reads
    /// its words (<see cref="Bbs757EfbScreen.KeyWords"/>) and a ground-service tile is found (<see cref="Bbs757GroundServices.ForTile"/>).
    /// The words are the control's text without its trailing state word ("GPU REMOVED" → GPU).</summary>
    private static Bbs757EfbItem Tile(Bbs757EfbMemory.Node n, string? pictureState, string? text = null, string label = "")
    {
        text ??= n.Text;
        var hit = Hit(n);
        // The key comes from the button's own words, so a state the tile's picture shows (CHOCKS, then "IN PLACE" or "REMOVED") never changes it.
        string key = "btn:|" + StripState(text) + "@" + (hit.X / 20).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "," + (hit.Y / 20).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string words = pictureState is null ? text : text + " " + pictureState;
        return new Bbs757EfbItem(Bbs757EfbKind.Button, Bbs757EfbVocabulary.Pretty(words), Box(n), hit, true, key, Label: label, Name: n.Name);
    }

    /// <summary>A label's words as the page grammar reads them (<see cref="Grammar"/>): a line with its value (and unit), a heading,
    /// or plain text; nothing for a path, a separator or a label another line took.</summary>
    private static void EmitText(Bbs757EfbMemory.Node n, Grammar g, List<Bbs757EfbItem> items)
    {
        if (IsNoise(n) || g.Taken.Contains(n)) return;
        if (g.RowValues.TryGetValue(n, out var rowValues)) // an index row's name: one line per other label of the row
        {
            foreach (var v in rowValues)
                items.Add(new Bbs757EfbItem(Bbs757EfbKind.Row, n.Text, Box(n), Hit(n), false, rowValues.Count == 1 ? KeyOf(n) : KeyOf(v),
                    Value: v.Text, Name: n.Name));
            return;
        }
        if (g.InputOf.TryGetValue(n, out var input)) // a title naming an input: the input (a text box, a drop-down), named by the title
        {
            items.Add(g.InputItem(input, n.Text));
            return;
        }
        if (g.ValueOf.TryGetValue(n, out var value))
        {
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Row, n.Text, Box(n), Hit(n), false, KeyOf(n),
                Value: g.WithUnit(value), Name: n.Name));
            return;
        }
        if (g.NamedBy.TryGetValue(n, out var namer)) // a value under the label that names a control beside it ("Nose door: 0%")
        {
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Row, namer.Text, Box(n), Hit(n), false, KeyOf(n), Value: g.WithUnit(n), Name: n.Name));
            return;
        }
        // A separator ("|"), or marks with no word in them; a value of marks ("------", "---") that pairs with nothing still reads.
        if (!n.Text.Any(char.IsLetterOrDigit) && !g.IsValueOfMarks(n)) return;
        if (g.LevelOf.TryGetValue(n, out int level))
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Heading, n.Text, Box(n), Hit(n), false, KeyOf(n), Level: level, Name: n.Name));
        else if (n.H > LongTextHeight || n.Text.Contains('\n'))
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.LongText, n.Text, Box(n), Hit(n), false, KeyOf(n), Name: n.Name));
        else
            items.Add(new Bbs757EfbItem(Bbs757EfbKind.Text, g.WithUnit(n), Box(n), Hit(n), false, KeyOf(n), Name: n.Name));
    }

    /// <summary>A text taller than this (or holding a line break) is a long text, read whole (the route, the VNAV note).</summary>
    private const int LongTextHeight = 60;

    /// <summary>A settings row as one item: the control's kind and tap point, the label's words, and the value the
    /// row's value label shows (the toggle/slider has no text of its own). The row's control name is its key, stable
    /// across reads, so a re-read relocates it exactly.</summary>
    private static Bbs757EfbItem Row(Bbs757EfbMemory.Node grid, Bbs757EfbMemory.Node control)
    {
        // The descriptive label and the value label are not always direct children of the row grid (they sit
        // inside a nested layout grid), so search the whole row. The label is the leftmost non-value words.
        var leaves = Descendants(grid)
            .Where(c => c.Kind is "label" or "other" && c.Text.Length > 0 && !IsNoise(c)).ToList();
        string label = leaves
            .Where(c => !IsValue(c) && c.X < 1180)
            .OrderBy(c => c.X).ThenBy(c => c.Y).FirstOrDefault()?.Text ?? grid.Name;
        string value = leaves.FirstOrDefault(IsValue)?.Text ?? "";
        var kind = control.Kind == "toggle" ? Bbs757EfbKind.Toggle : Bbs757EfbKind.Slider;
        return new Bbs757EfbItem(kind, label, Box(control), Hit(control), true, control.Name, On: control.On ?? StateOf(value), Value: value,
            Slider: kind == Bbs757EfbKind.Slider ? SliderOf(control, value) : null);
    }

    /// <summary>
    /// A slider's track, left to right, or bottom to top for a box taller than wide (the hydraulic reservoirs, Systems' zoom; bottom is 0,
    /// measured 2026-11-07), and its knob. A slider whose knob the walk read (<see cref="Bbs757EfbMemory.Node.Knob"/>) runs along the
    /// range of its knob's center, X + kw/2 to X + W - kw/2 (upright: from Y + H - kh/2 up to Y + kh/2, as drawn), since a tap centers
    /// the knob on the tap point (measured: Charts' zoom, tap x 1204, knob center 1204.5), and its knob is at the place read, marked as
    /// BlueBird's own. Any other runs along its rectangle, its knob at the part of the track its value reads as (a percent, "70%", or a
    /// count of its most, "16/16"), else at the start (the choices of a settings slider are told by the catalog, not by the knob; a
    /// slider whose value shows nowhere keeps R8's start). The value the tablet shows names the slider either way.
    /// </summary>
    private static Bbs757EfbSlider SliderOf(Bbs757EfbMemory.Node slider, string value)
    {
        bool vertical = slider.H > slider.W;
        int acrossX = vertical ? (int)(slider.X + slider.W / 2) : 0;
        if (slider.Knob is float place)
        {
            float half = slider.KnobSpan / 2;
            var (from, to) = vertical
                ? ((int)(slider.Y + slider.H - half), (int)(slider.Y + half))
                : ((int)(slider.X + half), (int)(slider.X + slider.W - half));
            var read = new Bbs757EfbSlider(from, to, from) { Vertical = vertical, AcrossX = acrossX, KnobRead = true };
            return read with { KnobX = read.At(place * 100.0) };
        }
        int start, end;
        if (vertical)
        {
            int top = (int)slider.Y;
            (start, end) = (top + (int)slider.H, top);
        }
        else
        {
            int left = (int)slider.X;
            (start, end) = (left, left + (int)slider.W);
        }
        var track = new Bbs757EfbSlider(start, end, start) { Vertical = vertical, AcrossX = acrossX };
        // Only a value that is a percent or a count places the knob: a bare number on a settings slider is one of its choices.
        double? shown = value.EndsWith('%') || value.Contains('/') ? Bbs757EfbSlider.PercentShown(value) : null;
        return shown is double percent ? track with { KnobX = track.At(percent) } : track;
    }

    /// <summary>A switch state from the word a value label shows: on for ON, off for OFF, unknown for a word of two
    /// choices (AUTO, a cycle time) that the window reads as the value instead.</summary>
    private static bool? StateOf(string value) =>
        value.Equals("ON", StringComparison.OrdinalIgnoreCase) ? true
        : value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? false : null;

    // ---- the section and whether the page scrolls ----------------------------

    /// <summary>
    /// The section the page shows, as a side-menu word (CALLOUTS, LIVERY OPTIONS): the word of the side-menu entry BlueBird's selected
    /// state marks current (<see cref="Grammar.CurrentEntry"/>; plan 21, measured 2026-11-07 on three side menus). "" for an app with no
    /// sections (Systems, Checklist) and for a read that marks no entry (a tree captured before plan 21 carries no selected state).
    /// </summary>
    private static string SectionOf(string app, Grammar g) =>
        Bbs757EfbPageRules.HasSections(app) && g.CurrentEntry is { } entry ? MenuWord(app, entry.Text) : "";

    /// <summary>The one scrollbox of the content that scrolls: the first that wraps a taller <c>*_stackbox</c> (its content does not
    /// fit it; Settings' pages). Null when none does: an inner scrollbox holding a text (the ACARS route, Weather's report, the
    /// checklist's lines) does not make the page scroll, and the controls around it are clipped to the content area, not to it.</summary>
    private static Bbs757EfbMemory.Node? PageScrollbox(Bbs757EfbMemory.App content) =>
        Flatten(content.Controls).FirstOrDefault(n => IsScrollbox(n)
            && Descendants(n).Any(d => d.Name.EndsWith("_stackbox", StringComparison.Ordinal) && d.H > n.H + 4));

    // ---- helpers -------------------------------------------------------------

    /// <summary>The page chrome the model never lists: the home button (the window adds its own) and the page title
    /// (the window shows the page name).</summary>
    private static bool IsChrome(Bbs757EfbMemory.Node n) =>
        n.Name.EndsWith("home_btn", StringComparison.Ordinal) || IsPageTitle(n);

    /// <summary>The app's own page title: <c>page_title_label</c>, and the Checklist's <c>checklist_title_label</c> in the same place.
    /// Every other <c>*_title_label</c> is a title of the page's own.</summary>
    private static bool IsPageTitle(Bbs757EfbMemory.Node n) => n.Name is "page_title_label" or "checklist_title_label";

    /// <summary>The app's menu word a side button names: its text cleaned of a status glyph ("__   BEFORE START" →
    /// BEFORE START) and snapped to the menu word whose folded form it matches; the cleaned text when it matches none.</summary>
    private static string MenuWord(string app, string text)
    {
        string clean = Clean(text);
        if (!Bbs757EfbVocabulary.Menus.TryGetValue(app, out var words)) return clean;
        string folded = Fold(clean);
        foreach (var w in words) if (Fold(w) == folded) return w;
        return clean;
    }

    /// <summary>Text without a leading glyph run (the checklist's "__" completion mark) and with runs of spaces as one.</summary>
    private static string Clean(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Any(char.IsLetterOrDigit));
        return string.Join(' ', parts);
    }

    /// <summary>A value's letters and digits, upper-cased invariantly: how a side button's words and a menu word are compared.</summary>
    private static string Fold(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));

    /// <summary>A control's text without its trailing state word ("FUEL TRUCK OFF" → FUEL TRUCK), so <see cref="Bbs757EfbScreen.KeyWords"/> gives the same words.</summary>
    private static string StripState(string text)
    {
        foreach (var w in Bbs757EfbScreen.StateWords)
        {
            if (string.Equals(text, w, StringComparison.Ordinal)) return "";
            if (text.EndsWith(" " + w, StringComparison.Ordinal)) return text[..(text.Length - w.Length - 1)];
        }
        return text;
    }

    private static bool IsValue(Bbs757EfbMemory.Node n) =>
        n.Kind is "label" or "other" && (n.Name.EndsWith("_value", StringComparison.Ordinal)
            || n.Name.EndsWith("_value_label", StringComparison.Ordinal) || n.X >= 1180);

    /// <summary>Text that is not part of the page's reading: an image or asset path the tablet keeps in a label
    /// field, or the app's own page-title label. A real label may hold a slash ("AUTOMATIC GPU/JETWAY", "B/CRS"), so
    /// only a leading path marker or an image file extension counts as a path, never a bare slash. A label of marks alone
    /// ("|") is a separator, dropped where it stands alone (<see cref="EmitText"/>); a value of marks ("---") still reads in its line.</summary>
    private static bool IsNoise(Bbs757EfbMemory.Node n) =>
        IsPageTitle(n)
        || n.Text.StartsWith("./", StringComparison.Ordinal)
        || n.Text.StartsWith("../", StringComparison.Ordinal)
        || n.Text.StartsWith("/", StringComparison.Ordinal)
        || n.Text.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        || n.Text.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every node under the roots, depth first, each root included.</summary>
    private static IEnumerable<Bbs757EfbMemory.Node> Flatten(IReadOnlyList<Bbs757EfbMemory.Node> roots)
    {
        foreach (var r in roots)
        {
            yield return r;
            foreach (var d in Descendants(r)) yield return d;
        }
    }

    /// <summary>Every node under <paramref name="n"/>, depth first; the node itself is not included.</summary>
    private static IEnumerable<Bbs757EfbMemory.Node> Descendants(Bbs757EfbMemory.Node n)
    {
        foreach (var c in n.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static int Count(IReadOnlyList<Bbs757EfbMemory.Node> ns) => ns.Count + ns.Sum(n => Count(n.Children));

    /// <summary>A side-menu entry: a control in the left column of an app that has a side menu (the apps with sections, and the
    /// checklist's section list). In any other app (Systems' zoom toolbar at x 16) a control left of the content is content.</summary>
    private static bool IsMenu(Bbs757EfbMemory.Node n, string app) =>
        (Bbs757EfbPageRules.HasSections(app) || app == "CHECKLIST") && n.X is >= 0 and < MenuRight && n.W <= MenuRight;
    private static string KeyOf(Bbs757EfbMemory.Node n) => n.Name.Length > 0 ? n.Name : "text|" + n.Text;
    private static Rectangle Box(Bbs757EfbMemory.Node n) => new((int)n.X, (int)n.Y, (int)n.W, (int)n.H);
    private static Point Hit(Bbs757EfbMemory.Node n) => new((int)(n.X + n.W / 2), (int)(n.Y + n.H / 2));

    // ---- the page grammar: lines, headings and pictures from BlueBird's names ---

    /// <summary>
    /// What BlueBird's control names say about one page (plan 20, R3, R4 and R10), worked out once before the page is emitted.
    /// A label and a value whose names are alike once their role words are taken out (<see cref="Bbs757EfbPageRules.ReadName"/>)
    /// are one line: the label emits as a <see cref="Bbs757EfbKind.Row"/> and the value is <see cref="Taken"/>. A unit beside a value
    /// (or beside a bare number whose name is the unit's without <c>unit</c>) joins its words. A title with no value is a heading, level 2
    /// or, inside a card already titled, level 3. A picture that shows a state (<see cref="Bbs757EfbPageRules.PictureState"/>) gives it to the
    /// button in its box. Only <c>label</c> controls pair: a long text (<c>other</c>, such as the route) stays a text of its own.
    /// </summary>
    private sealed class Grammar
    {
        private static readonly HashSet<string> GenericParts = new(StringComparer.Ordinal)
            { "btn", "button", "toggle", "status", "image", "img", "service", "box", "label", "bg", "icon", "state" };

        private readonly Dictionary<Bbs757EfbMemory.Node, string> _unitOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, string> _pictureOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, string> _labelOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> _rowNameOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, (string Text, string Value)> _sliderOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> _fieldLabel = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Bbs757EfbMemory.Node> _fieldLabels = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Bbs757EfbMemory.Node> _rowNames = new(ReferenceEqualityComparer.Instance);

        /// <summary>For a label that pairs, its value.</summary>
        public Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> ValueOf { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>The labels another item already holds: the values that paired, the units that joined, the labels that name a control.</summary>
        public HashSet<Bbs757EfbMemory.Node> Taken { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>For a title that is a heading, its level (2 or 3).</summary>
        public Dictionary<Bbs757EfbMemory.Node, int> LevelOf { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>For the name label of an index row (R6) with other labels, those labels: the row's state words.</summary>
        public Dictionary<Bbs757EfbMemory.Node, List<Bbs757EfbMemory.Node>> RowValues { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>For a title that names an input (a text box, a drop-down, or one the reader cannot read), that input.</summary>
        public Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> InputOf { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>The inputs a title names (<see cref="InputOf"/>'s values): each reads where its title is.</summary>
        public HashSet<Bbs757EfbMemory.Node> TitledInputs { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>The side-menu entry BlueBird's selected state marks current (<see cref="TheOneSelected"/>); null when it marks none.</summary>
        public Bbs757EfbMemory.Node? CurrentEntry { get; }
        private readonly Dictionary<Bbs757EfbMemory.Node, string> _inputName = new(ReferenceEqualityComparer.Instance);
        /// <summary>The word-less buttons holding exactly one label: value boxes, read as fields (R7).</summary>
        public HashSet<Bbs757EfbMemory.Node> FieldButtons { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>For a value with no role word whose name is the name of a label that names a control, plus one part
        /// (<c>maintenance_gear_door_nose_percent</c> under <c>maintenance_gear_door_nose_label</c>, which names its door button):
        /// that label, which names the value too ("Nose door: 0%", the final review's I9).</summary>
        public Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> NamedBy { get; } = new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<Bbs757EfbMemory.Node, Bbs757EfbPageRules.NameInfo> _info = new(ReferenceEqualityComparer.Instance);
        /// <summary>The labels R5 gave a control as its name.</summary>
        private readonly HashSet<Bbs757EfbMemory.Node> _namers = new(ReferenceEqualityComparer.Instance);

        private readonly string _app;

        public Grammar(Bbs757EfbMemory.App content)
        {
            _app = content.Class;
            var all = Flatten(content.Controls).ToList();
            var parentOf = new Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance);
            foreach (var n in all) foreach (var c in n.Children) parentOf[c] = n;
            // The checklist's RESET ALL sits in the side column and reads selected (measured 2026-11-07), but is no section.
            CurrentEntry = TheOneSelected(all.Where(n => n.Kind == "button" && !IsChrome(n) && IsMenu(n, _app) && !IsChecklistReset(_app, n)));

            foreach (var b in all)
                if (b.Kind == "button" && b.Text.Length == 0 && !IsChrome(b) && Descendants(b).Count(IsReadable) == 1)
                {
                    FieldButtons.Add(b);
                    var inner = Descendants(b).First(IsReadable);
                    _fieldLabel[b] = inner;
                    _fieldLabels.Add(inner);
                }

            // Only a label the page emits can pair or take a unit: the reader does not open a button with words, a switch, a slider or a
            // settings row. A value box (a word-less button holding one label) is opened for its number and its unit.
            var labels = Reachable(content.Controls).Where(IsReadable).ToList();
            foreach (var l in labels) _info[l] = Bbs757EfbPageRules.ReadName(l.Name);
            var free = labels.Where(l => !_fieldLabels.Contains(l)).ToList();

            _needsLabel.AddRange(Reachable(content.Controls).Where(n => n.Kind == "slider"
                || (n.Kind == "button" && !IsMenu(n, content.Class) && (FieldButtons.Contains(n) || Bbs757EfbPageRules.IsStateOrAction(n.Text)))));
            Rows(all, free);
            Sliders(content.Controls, free);
            Pair(free);
            JoinUnits(labels);
            LabelControls(content.Controls, free, parentOf);
            NameValues(free);
            ColonLabels(free, parentOf);
            Inputs(all, free, parentOf);
            Title(content.Controls, titledAbove: false);
            Pictures(all);
        }

        private static bool IsReadable(Bbs757EfbMemory.Node n) =>
            n.Kind == "label" && n.Text.Length > 0 && n.Children.Count == 0 && !IsNoise(n);

        /// <summary>The nodes <see cref="Emit"/> walks into: not the inside of a button with words, a switch, a slider or a settings row.</summary>
        private static IEnumerable<Bbs757EfbMemory.Node> Reachable(IReadOnlyList<Bbs757EfbMemory.Node> kids)
        {
            foreach (var n in kids)
            {
                if (IsChrome(n)) continue;
                yield return n;
                if (n.Kind is "toggle" or "slider" || (n.Kind == "button" && n.Text.Length > 0)) continue;
                if (n.Kind == "grid" && n.Children.Any(c => c.Kind is "toggle" or "slider")) continue;
                foreach (var d in Reachable(n.Children)) yield return d;
            }
        }

        /// <summary>A line's value with its unit ("1539 Gal"), or the label's own words when it has no unit.</summary>
        public string WithUnit(Bbs757EfbMemory.Node n) => _unitOf.TryGetValue(n, out var unit) ? n.Text + " " + unit : n.Text;

        /// <summary>Whether a label of marks alone ("------", "---") is a value by its name's role words, so it reads where it stands even
        /// when it pairs with nothing (the final review's M11); a mark with no role word, or named a separator, is a separator (R10).</summary>
        public bool IsValueOfMarks(Bbs757EfbMemory.Node n) =>
            _info.TryGetValue(n, out var info) && info.Role == Bbs757EfbPageRules.NameRole.Value && !Bbs757EfbPageRules.IsSeparatorName(n.Name);

        public string? PictureStateOf(Bbs757EfbMemory.Node button) => _pictureOf.GetValueOrDefault(button);

        // ---- R6: index rows -----------------------------------------------------

        /// <summary>Siblings whose names end in the same <c>_N</c> and share the stem before their last part (<c>failure_name_0</c>,
        /// <c>failure_state_0</c>, <c>failure_action_0</c>) are one row when they hold a control and a label: the <c>*_name_N</c> label
        /// (else the first label) names every control, and is the line of a row whose value is each other label.</summary>
        private void Rows(List<Bbs757EfbMemory.Node> all, List<Bbs757EfbMemory.Node> labels)
        {
            var labelSet = new HashSet<Bbs757EfbMemory.Node>(labels, ReferenceEqualityComparer.Instance);
            foreach (var parent in all)
            {
                var groups = parent.Children
                    .Select(k => (Node: k, Parts: Bbs757EfbPageRules.NameParts(k.Name)))
                    .Where(x => x.Parts.Length >= 3 && x.Parts[^1].All(char.IsAsciiDigit))
                    .GroupBy(x => string.Join('_', x.Parts[..^2]) + "#" + x.Parts[^1]);
                foreach (var group in groups)
                {
                    var controls = group.Select(x => x.Node).Where(k => k.Kind is "button" or "slider" or "toggle").ToList();
                    var rowLabels = group.Select(x => x.Node).Where(labelSet.Contains).ToList();
                    if (controls.Count == 0 || rowLabels.Count == 0) continue;
                    var name = rowLabels.FirstOrDefault(l => Bbs757EfbPageRules.NameParts(l.Name).Contains("name")) ?? rowLabels[0];
                    _rowNames.Add(name);
                    foreach (var c in controls) _rowNameOf[c] = name;
                    var others = rowLabels.Where(l => !ReferenceEquals(l, name)).ToList();
                    if (others.Count == 0) continue;
                    RowValues[name] = others;
                    foreach (var o in others) Taken.Add(o);
                }
            }
        }

        // ---- R5: the label that names a control -----------------------------------

        /// <summary>The label that names a control whose own words are none, a state or an action, in order: the one label whose name,
        /// once role words and control words are taken out, equals the control's; the name of the control's index row; the one label
        /// touching the control's rectangle that touches no other control waiting for a name as near or nearer. Null when none, when two
        /// touch, and when the one that touches lies as near another such control (a tie names neither: the final review's M1).</summary>
        private Bbs757EfbMemory.Node? NamingLabel(Bbs757EfbMemory.Node control, IEnumerable<Bbs757EfbMemory.Node> pool)
        {
            var candidates = pool.Where(l => !Taken.Contains(l) || RowValues.ContainsKey(l)).ToList();
            string key = Bbs757EfbPageRules.ControlKey(control.Name);
            if (key.Length > 0)
            {
                var same = candidates.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Label && !ValueOf.ContainsKey(l)
                    && Bbs757EfbPageRules.ControlKey(_info[l]) == key).ToList();
                if (same.Count == 1) return same[0];
            }
            if (_rowNameOf.TryGetValue(control, out var row)) return row;
            var touching = candidates.Where(l => _info[l].Role is Bbs757EfbPageRules.NameRole.None or Bbs757EfbPageRules.NameRole.Label
                && !_info[l].IsTitle && !ValueOf.ContainsKey(l) && TouchGap(control, l) is { } own
                && !_needsLabel.Any(o => !ReferenceEquals(o, control) && TouchGap(o, l) is { } other && other <= own)).ToList();
            return touching.Count == 1 ? touching[0] : null;
        }

        /// <summary>The controls that wait for a name: a label touching two of them belongs to the nearer one, and to neither at a tie.</summary>
        private readonly List<Bbs757EfbMemory.Node> _needsLabel = new();

        /// <summary>
        /// R5's fourth source (the final review's I8): the label of the settings row a button sits in, its nearest grid when that is a
        /// row (<see cref="Bbs757EfbPageRules.IsRowGridName"/>): the row's first label that is no value ("SIMBRIEF ID NUMBER" names the
        /// row's LOGOUT, "LATEST CYCLE:" its REMOVE). Null for a button whose nearest grid is no row, or a row with no such label.
        /// </summary>
        private Bbs757EfbMemory.Node? RowGridLabel(Bbs757EfbMemory.Node button, Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> parentOf)
        {
            for (var p = parentOf.GetValueOrDefault(button); p != null; p = parentOf.GetValueOrDefault(p))
            {
                if (p.Kind != "grid") continue;
                if (!Bbs757EfbPageRules.IsRowGridName(_app, p.Name)) return null;
                return Descendants(p).FirstOrDefault(l => !ReferenceEquals(l, button) && _info.TryGetValue(l, out var info)
                    && info.Role is Bbs757EfbPageRules.NameRole.Label or Bbs757EfbPageRules.NameRole.None && !IsValue(l)
                    && l.Text.Any(char.IsLetterOrDigit));
            }
            return null;
        }

        /// <summary>How far a label lies below, above or left of a control (at most 12 px, overlapping it in the other axis); null when it
        /// does not touch it.</summary>
        private static float? TouchGap(Bbs757EfbMemory.Node c, Bbs757EfbMemory.Node l)
        {
            const float gap = 12, slack = -0.5f;
            bool overlapX = l.X < c.X + c.W && l.X + l.W > c.X;
            bool overlapY = l.Y < c.Y + c.H && l.Y + l.H > c.Y;
            float below = l.Y - (c.Y + c.H), above = c.Y - (l.Y + l.H), left = c.X - (l.X + l.W);
            float? best = null;
            void Take(bool fits, float d) { if (fits && d >= slack && d <= gap && (best is null || d < best)) best = d; }
            Take(overlapX, below);
            Take(overlapX, above);
            Take(overlapY, left);
            return best;
        }

        /// <summary>A label that names a control is not a line of its own (unless it is a title, which stays a heading, or an index row's name).</summary>
        private void Use(Bbs757EfbMemory.Node label)
        {
            if (!_info[label].IsTitle && !RowValues.ContainsKey(label)) Taken.Add(label);
        }

        /// <summary>Names the buttons R5 names: one whose words are none, a state or an action by <see cref="NamingLabel"/>, else by its
        /// settings row (<see cref="RowGridLabel"/>); any other button in a settings row by its row too ("Sayintentions ACARS key: Fetch
        /// key"). A row's label that is also a line with its value ("SIMBRIEF ID NUMBER: 123456") stays that line.</summary>
        private void LabelControls(IReadOnlyList<Bbs757EfbMemory.Node> roots, List<Bbs757EfbMemory.Node> free,
            Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> parentOf)
        {
            var pool = free.Where(l => !Taken.Contains(l) && !ValueOf.ContainsKey(l) && _info[l].Role != Bbs757EfbPageRules.NameRole.Unit).ToList();
            foreach (var b in Reachable(roots).Where(n => n.Kind == "button" && !IsMenu(n, _app)))
            {
                bool waits = FieldButtons.Contains(b) || Bbs757EfbPageRules.IsStateOrAction(b.Text);
                if (waits && NamingLabel(b, pool) is { } label)
                {
                    _labelOf[b] = label.Text;
                    _namers.Add(label);
                    Use(label);
                }
                else if (RowGridLabel(b, parentOf) is { } rowLabel)
                {
                    _labelOf[b] = rowLabel.Text;
                    if (!ValueOf.ContainsKey(rowLabel)) Use(rowLabel);
                }
            }
        }

        /// <summary>Gives <see cref="NamedBy"/>: each value with no role word, unpaired, whose name less its last part has the core of a
        /// label that names a control (no title: a card's title names its card, not each value in it).</summary>
        private void NameValues(List<Bbs757EfbMemory.Node> free)
        {
            foreach (var v in free)
            {
                if (_info[v].Role != Bbs757EfbPageRules.NameRole.None || Taken.Contains(v) || ValueOf.ContainsKey(v) || _rowNames.Contains(v)
                    || RowValues.ContainsKey(v)) continue;
                var parts = Bbs757EfbPageRules.NameParts(v.Name);
                if (parts.Length < 2) continue;
                string stem = string.Join(' ', parts[..^1].OrderBy(p => p, StringComparer.Ordinal));
                var namer = _namers.FirstOrDefault(l => !_info[l].IsTitle && _info[l].CoreKey == stem);
                if (namer is not null) NamedBy[v] = namer;
            }
        }

        /// <summary>A button as an item: its own words, with the label that names it when its words are a state or an action; a button
        /// with no words is named by its label, else by its control name (R11), else left out (null).</summary>
        public Bbs757EfbItem? ButtonItem(Bbs757EfbMemory.Node n)
        {
            _labelOf.TryGetValue(n, out var label);
            // A button whose text is only marks ("+", "-") has no words either.
            bool wordless = !n.Text.Any(char.IsLetterOrDigit);
            if (wordless)
            {
                // A background box of the page (chocks_service_box) is no control the pilot presses.
                if (n.Text.Length == 0 && Bbs757EfbPageRules.NameParts(n.Name).Contains("box")) return null;
                string? name = label ?? Bbs757EfbPageRules.NameFromParts(n.Name);
                if (name is not null) return Tile(n, null, name);
                return n.Text.Length == 0 ? null : Tile(n, PictureStateOf(n), label: label ?? "");
            }
            return Tile(n, PictureStateOf(n), label: label ?? "");
        }

        /// <summary>A value box as a field: named by the page rules' table, else its label, else its control name; its value is the
        /// number it holds with its unit. Marked <see cref="Bbs757EfbItem.ValueBox"/>: it is BlueBird's button, whose tap opens its
        /// dialog (plan 22).</summary>
        public Bbs757EfbItem FieldItem(Bbs757EfbMemory.Node box)
        {
            var inner = _fieldLabel[box];
            _labelOf.TryGetValue(box, out var label);
            string name = Bbs757EfbPageRules.FieldName(box.Name) ?? label ?? Bbs757EfbPageRules.NameFromParts(box.Name) ?? box.Name;
            return new Bbs757EfbItem(Bbs757EfbKind.Field, name, Box(box), Hit(box), true, box.Name, Value: WithUnit(inner), Name: box.Name,
                ValueBox: true);
        }

        // ---- R8: sliders ----------------------------------------------------------

        private void Sliders(IReadOnlyList<Bbs757EfbMemory.Node> roots, List<Bbs757EfbMemory.Node> free)
        {
            foreach (var s in Reachable(roots).Where(n => n.Kind == "slider"))
            {
                var label = NamingLabel(s, free);
                string key = Bbs757EfbPageRules.ControlKey(s.Name);
                var value = key.Length == 0 ? null : free.FirstOrDefault(l => !Taken.Contains(l) && _info[l].Role == Bbs757EfbPageRules.NameRole.Value
                    && _info[l].CoreKey == key);
                if (value is null && Bbs757EfbPageRules.SliderValueName(s.Name) is { } valueName)
                    value = free.FirstOrDefault(l => l.Name == valueName && !Taken.Contains(l));
                if (value is not null) Taken.Add(value);
                if (label is not null)
                {
                    Use(label);
                    // A title that names a slider is the slider's name, not a heading too: the slider says it (Weight & balance).
                    if (_info[label].IsTitle) Taken.Add(label);
                }
                string text = label?.Text ?? OwnName(s.Name), shown = value?.Text ?? "";
                // A title that also shows the slider's count ("BUSINESS PAX - 16/16") gives the slider its name and its value: the
                // count is where its knob is.
                if (shown.Length == 0 && label is not null && TitleCount.Match(label.Text) is { Success: true } count)
                    (text, shown) = (count.Groups[1].Value, count.Groups[2].Value);
                _sliderOf[s] = (text, shown);
            }
        }

        /// <summary>A title and the count it shows after a dash: "BUSINESS PAX - 16/16" is BUSINESS PAX, 16/16.</summary>
        private static readonly System.Text.RegularExpressions.Regex TitleCount =
            new(@"^(.*\S)\s+-\s+(\d+\s*/\s*\d+)\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>A slider outside a settings row as an item: named by R5, valued by its partner, its track from its rectangle.</summary>
        public Bbs757EfbItem SliderItem(Bbs757EfbMemory.Node s)
        {
            var (text, value) = _sliderOf.GetValueOrDefault(s, (s.Name, ""));
            return new Bbs757EfbItem(Bbs757EfbKind.Slider, text, Box(s), Hit(s), true, s.Name, Value: value, Slider: SliderOf(s, value), Name: s.Name);
        }

        // ---- R4: lines and units ----------------------------------------------------

        private void Pair(List<Bbs757EfbMemory.Node> labels)
        {
            var open = labels.Where(l => !Taken.Contains(l) && !_rowNames.Contains(l)).ToList();
            var waiting = new Dictionary<string, Queue<Bbs757EfbMemory.Node>>(StringComparer.Ordinal);
            foreach (var v in open.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Value && _info[l].Core.Count > 0))
            {
                if (!waiting.TryGetValue(_info[v].CoreKey, out var q)) waiting[_info[v].CoreKey] = q = new Queue<Bbs757EfbMemory.Node>();
                q.Enqueue(v);
            }
            foreach (var l in open.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Label && _info[l].Core.Count > 0))
            {
                if (!waiting.TryGetValue(_info[l].CoreKey, out var q) || q.Count == 0) continue;
                var v = q.Dequeue();
                ValueOf[l] = v;
                Taken.Add(v);
            }
        }

        /// <summary>A unit joins the value (or the bare number) whose core is its own, with or without <c>qty</c>; a unit named for a
        /// group alone (<c>wb_pax_business_unit</c>) joins the number of that group (<c>wb_pax_business_weight</c>).</summary>
        private void JoinUnits(List<Bbs757EfbMemory.Node> labels)
        {
            foreach (var unit in labels.Where(l => _info[l].Role == Bbs757EfbPageRules.NameRole.Unit))
            {
                var want = new[] { _info[unit].CoreKey, string.Join(' ', _info[unit].Core.Where(p => p != "qty").OrderBy(p => p, StringComparer.Ordinal)) }
                    .Where(k => k.Length > 0).Distinct().ToList();
                bool plain = !_info[unit].Core.Contains("qty") && !_info[unit].Core.Contains("weight");
                string Bare(Bbs757EfbMemory.Node l) =>
                    string.Join(' ', _info[l].Core.Where(p => p is not ("qty" or "weight")).OrderBy(p => p, StringComparer.Ordinal));
                var partner = labels.FirstOrDefault(l => !ReferenceEquals(l, unit) && !_unitOf.ContainsKey(l)
                    && _info[l].Role is Bbs757EfbPageRules.NameRole.Value or Bbs757EfbPageRules.NameRole.None
                    && (want.Contains(_info[l].CoreKey) || (plain && want.Contains(Bare(l)))));
                if (partner is null) continue;
                _unitOf[partner] = unit.Text;
                Taken.Add(unit);
            }
        }

        // ---- a title ending in a colon ----------------------------------------------------

        /// <summary>A title whose text ends in ":" is a label, never a heading: it names the one value or control beside it. Beside
        /// means a sibling in its parent; when a sibling box (<c>*_box</c>) holds the title, only the siblings inside that box. One
        /// other readable label there is its value (the pair is a line); one button with words there takes the title as its label.
        /// With none or several the title stays what it was.</summary>
        private void ColonLabels(List<Bbs757EfbMemory.Node> free, Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> parentOf)
        {
            foreach (var title in free.Where(l => _info[l].IsTitle && l.Text.TrimEnd().EndsWith(':') && !Taken.Contains(l)
                && !ValueOf.ContainsKey(l) && !RowValues.ContainsKey(l)).ToList())
            {
                if (!parentOf.TryGetValue(title, out var parent)) continue;
                var siblings = parent.Children.Where(s => !ReferenceEquals(s, title)).ToList();
                var box = siblings.FirstOrDefault(s => s.Text.Length == 0 && s.Children.Count == 0
                    && Bbs757EfbPageRules.NameParts(s.Name).Contains("box") && Inside(title, s));
                var beside = box is null ? siblings : siblings.Where(s => !ReferenceEquals(s, box) && Inside(s, box)).ToList();
                var values = beside.Where(s => IsReadable(s) && !Taken.Contains(s) && !ValueOf.ContainsKey(s)).ToList();
                var controls = beside.Where(s => s.Kind == "button" && s.Text.Any(char.IsLetterOrDigit)).ToList();
                if (controls.Count == 1 && values.Count == 0 && !_labelOf.ContainsKey(controls[0]))
                {
                    _labelOf[controls[0]] = title.Text;
                    Taken.Add(title);
                }
                else if (values.Count == 1 && controls.Count == 0)
                {
                    ValueOf[title] = values[0];
                    Taken.Add(values[0]);
                }
            }
        }

        /// <summary>Whether the middle of <paramref name="n"/> lies inside <paramref name="box"/>.</summary>
        private static bool Inside(Bbs757EfbMemory.Node n, Bbs757EfbMemory.Node box)
        {
            float cx = n.X + n.W / 2, cy = n.Y + n.H / 2;
            return cx >= box.X && cx <= box.X + box.W && cy >= box.Y && cy <= box.Y + box.H;
        }

        // ---- inputs: text boxes, drop-downs, and the ones the reader cannot read -------------

        /// <summary>Whether a control is one of BlueBird's text boxes or drop-downs, read by its class (plan 21).</summary>
        private static bool IsReadInput(Bbs757EfbMemory.Node n) => n.Kind is "textbox" or "pulldown";

        /// <summary>
        /// A title that names an input (<c>planning_perf_calc_flap_field_title</c> and <c>..._flap_dropdown</c>, else the one input
        /// in a container that holds the two alone, as each of PERF calc's field canvases does) is that input's name: it is no heading,
        /// and the input reads where the title is. A title among other controls (Systems' <c>selected_page_title_label</c>, the system
        /// shown, beside the home button, the app's title and the page menu) names none and stays a heading. An input is a text box or
        /// a drop-down (plan 21), or, in a tree captured before plan 21 read their classes, a control named as one with no text
        /// (<see cref="Bbs757EfbPageRules.IsUnreadInputName"/>), a field with no value read.
        /// <para>A text box or drop-down no title names takes no label by R5 (<see cref="NamingLabel"/>): R5 names a control whose own
        /// words are none, a state or an action, and these show words of their own (a text, a face), so a label beside one is a line of
        /// its own (Charts' KINGMAN, the airport's name beside its request box). It is named by its settings row's label (the AIRAC
        /// page's code boxes), else, for a drop-down whose face shows none of its entries, by the face's words (Systems' SELECT: the face
        /// is a prompt), else by its own name's words (<see cref="OwnName"/>: Charts' request box, Map's provider), and reads where it
        /// is.</para>
        /// </summary>
        private void Inputs(List<Bbs757EfbMemory.Node> all, List<Bbs757EfbMemory.Node> free, Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node> parentOf)
        {
            var inputs = all.Where(n => IsReadInput(n) || (n.Kind == "other" && n.Text.Length == 0 && Bbs757EfbPageRules.IsUnreadInputName(n.Name))).ToList();
            if (inputs.Count == 0) return;
            var used = TitledInputs;
            var titles = free.Where(l => _info[l].IsTitle && !Taken.Contains(l) && !ValueOf.ContainsKey(l) && l.Text.Any(char.IsLetterOrDigit)).ToList();
            foreach (var t in titles)
            {
                string key = Bbs757EfbPageRules.ControlKey(_info[t]);
                var hit = key.Length == 0 ? null : inputs.FirstOrDefault(i => !used.Contains(i) && Bbs757EfbPageRules.ControlKey(i.Name) == key);
                if (hit is null) continue;
                InputOf[t] = hit;
                used.Add(hit);
            }
            foreach (var t in titles.Where(t => !InputOf.ContainsKey(t)))
            {
                if (!parentOf.TryGetValue(t, out var parent) || parent.Children.Count != 2) continue;
                var beside = parent.Children.Where(i => inputs.Contains(i) && !used.Contains(i)).ToList();
                if (beside.Count != 1) continue;
                InputOf[t] = beside[0];
                used.Add(beside[0]);
            }
            foreach (var input in inputs.Where(i => IsReadInput(i) && !used.Contains(i)))
            {
                if (RowGridLabel(input, parentOf) is { } rowLabel)
                {
                    _inputName[input] = rowLabel.Text;
                    if (!ValueOf.ContainsKey(rowLabel)) Use(rowLabel);
                }
                // A face is a prompt only beside entries it matches none of: with none read (a torn vector, a list BlueBird has not
                // filled yet) it is a value (Task 5's review, M2).
                else if (input.Kind == "pulldown" && input.Text.Any(char.IsLetterOrDigit) && input.Entries is { Count: > 0 } entries
                    && !entries.Contains(input.Text))
                    _inputName[input] = input.Text;
                else _inputName[input] = OwnName(input.Name);
            }
        }

        /// <summary>The name of a text box or drop-down no title names (<see cref="Inputs"/>).</summary>
        public string InputNameOf(Bbs757EfbMemory.Node input) => _inputName.GetValueOrDefault(input) ?? OwnName(input.Name);

        /// <summary>An input as an item named <paramref name="name"/>: a drop-down a <see cref="Bbs757EfbKind.Choice"/> of its entries,
        /// valued by the one it shows (by its face's words when no entry was read: the face is then a value, not a prompt), open or not,
        /// with its open list as drawn (<see cref="OpenListOf"/>); a text box a <see cref="Bbs757EfbKind.Field"/> holding its text, with
        /// its caret, maximum length and keypad flag when the walk read them (<see cref="TextStateOf"/>); an input the reader cannot read a
        /// field with no value. Keyed by its control name.</summary>
        public Bbs757EfbItem InputItem(Bbs757EfbMemory.Node input, string name) => input.Kind switch
        {
            "pulldown" => new Bbs757EfbItem(Bbs757EfbKind.Choice, name, Box(input), Hit(input), true, input.Name,
                Value: input.Entries is { Count: > 0 } ? input.Value : input.Text, Name: input.Name,
                Entries: input.Entries ?? Array.Empty<string>(), Open: input.Open, List: OpenListOf(input)),
            "textbox" => new Bbs757EfbItem(Bbs757EfbKind.Field, name, Box(input), Hit(input), true, input.Name, Value: input.Text, Name: input.Name,
                TextState: TextStateOf(input)),
            _ => new Bbs757EfbItem(Bbs757EfbKind.Field, name, Box(input), Hit(input), true, input.Name, Name: input.Name),
        };

        /// <summary>A drop-down's open list as the walk drew it (<see cref="Bbs757EfbMemory.Node.OpenList"/>, <see cref="Bbs757EfbMemory.Node.Rows"/>):
        /// its scrollbox's rectangle and its entry buttons, each with its words; null while the list is closed, or open with its scrollbox not
        /// read (a torn list: nothing to tap in it).</summary>
        private static Bbs757EfbOpenList? OpenListOf(Bbs757EfbMemory.Node input) =>
            input.Open && input.OpenList is { } list
                ? new Bbs757EfbOpenList(Box(list), (input.Rows ?? Array.Empty<Bbs757EfbMemory.Node>()).Select(r => new Bbs757EfbListRow(r.Text, Box(r))).ToList())
                : null;

        private bool IsHeadingTitle(Bbs757EfbMemory.Node n) =>
            _info.TryGetValue(n, out var info) && (info.IsTitle || Bbs757EfbPageRules.IsHeadingName(_app, n.Name)) && !ValueOf.ContainsKey(n)
            && !Taken.Contains(n) && !InputOf.ContainsKey(n) && n.Text.Any(char.IsLetterOrDigit);

        /// <summary>Gives each heading title its level: 2, or 3 when a container above it already holds a title of its own.</summary>
        private void Title(IReadOnlyList<Bbs757EfbMemory.Node> kids, bool titledAbove)
        {
            bool holds = kids.Any(IsHeadingTitle);
            foreach (var k in kids.Where(IsHeadingTitle)) LevelOf[k] = titledAbove ? 3 : 2;
            foreach (var k in kids) Title(k.Children, titledAbove || holds);
        }

        private void Pictures(List<Bbs757EfbMemory.Node> all)
        {
            var parentOf = new Dictionary<Bbs757EfbMemory.Node, Bbs757EfbMemory.Node>(ReferenceEqualityComparer.Instance);
            foreach (var n in all) foreach (var c in n.Children) parentOf[c] = n;
            foreach (var picture in all.Where(n => n.Kind != "button" && n.Text.Length > 0))
            {
                if (Bbs757EfbPageRules.PictureState(picture.Text) is not { } state || !parentOf.TryGetValue(picture, out var box)) continue;
                var buttons = Descendants(box).Where(b => b.Kind == "button" && b.Text.Length > 0).ToList();
                var own = Bbs757EfbPageRules.NameParts(picture.Name).Where(p => !GenericParts.Contains(p)).ToHashSet(StringComparer.Ordinal);
                var named = buttons.Where(b => Bbs757EfbPageRules.NameParts(b.Name).Any(own.Contains)).ToList();
                var direct = box.Children.Where(b => b.Kind == "button" && b.Text.Length > 0).ToList();
                var tile = named.Count == 1 ? named[0] : named.Count == 0 && direct.Count == 1 ? direct[0] : null;
                if (tile is not null) _pictureOf[tile] = state;
            }
        }
    }
}
