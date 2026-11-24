using System.Drawing;
using System.Globalization;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// An element the app adds to the window on top of the tablet's own (plan 18): a button of the app's, such as the Navigraph sign-in,
/// or a line of text, such as the code to enter at navigraph.com. The bridge decides what is offered on which page.
/// <see cref="Key"/> is stable while the words change, so the window keeps the screen reader's focus on the element, and it names the
/// element when it is pressed (<see cref="Bbs757EfbElements.Action.ExtraKey"/>).
/// </summary>
public sealed record Bbs757EfbExtraElement(string Key, string Text, bool IsButton);

/// <summary>
/// A read tablet page (<see cref="Bbs757EfbScreenModel"/>) as the shared EFB window's elements, and what pressing each does. Pure:
/// tested. The window (FbwEfbForm) renders headings, buttons, check boxes, choice lists and sliders from the push's "items.N.*" keys
/// (the PMDG client's format) and posts back click_display_element / set_element_value with the element's aidx. A drop-down is a choice
/// list of its entries (plan 21), whose set the press runner chooses on the tablet (<see cref="Bbs757EfbPressRunner.ChooseAsync"/>,
/// B757-27); a text box whose caret was read is an Enter-only edit box (plan 22: its set, sent on Enter alone, is typed on the tablet's
/// keyboard by <see cref="Bbs757EfbPressRunner.TypeAsync"/>), a value box a button that taps it (BlueBird opens its dialog), and any
/// other field (a text box whose caret was not read) a line with what it holds.
///
/// Every control the reader offers can be pressed (the owner's ruling of 2026-10-29): a button, tile, switch, check box, slider and tab
/// is clickable whatever the reader's <see cref="Bbs757EfbItem.Clickable"/> says, and nothing here refuses a press. The one exception
/// is the tablet's own: while it shows a question over its page, the page is dimmed and takes no tap, so its controls are text until the
/// question is answered. The question's answers press, and so does the top bar, which stays lit above the dimming. One row is never
/// offered as a control at all, by the owner's ruling of 2026-10-30: the EFB colors theme, whose place holds a note
/// (<see cref="ThemeNoteFor"/>, B757-21).
///
/// The order the pilot meets (plan 20, D1): the alert; the tablet's question (its words as text, its text boxes, then its answers); the
/// page; the app's extras; "Status bar" with the top bar, then BlueBird's notifications, newest first, under a heading;
/// "Sections" with the side menu; and Home
/// last. The window speaks only its first alert and remembers one text, so the question is no alert here: the bridge says a
/// question once, through its own alert, when a read first shows it (plan 18, Task 8), and the page keeps its words first. The window's
/// F5 reads the tablet again (get_display_elements), so no element does, and the wheel is no element's either. Names and headings are
/// sentence case (<see cref="Bbs757EfbVocabulary.Pretty"/>); values, field values, long text and the extras are as written.
///
/// No element asks the window to say its new words (the shell's <c>announceChange</c>): the window could say them only for a
/// control that kept its key, and a pressed control's new words are the press's to say, once, whatever its key
/// (<see cref="Bbs757EfbPress.ButtonOutcome"/>).
/// </summary>
public static class Bbs757EfbElements
{
    /// <summary>What pressing an element does.</summary>
    public enum ActionKind { Tap, Extra }

    /// <summary>One element's action: the tablet control it taps (Tap, and the control a set changes), or the key of the app's extra it
    /// stands for (Extra).</summary>
    public sealed record Action(ActionKind Kind, Bbs757EfbItem? Item = null, string? ExtraKey = null);

    /// <summary>A build: the push's data and each aidx's action.</summary>
    public sealed record Built(Dictionary<string, string> Data, IReadOnlyDictionary<int, Action> Actions, int Count);

    private const string NotificationsHeading = "Notifications";
    private const string StatusBarHeading = "Status bar";
    private const string SectionsHeading = "Sections";
    private const char OptionSeparator = (char)0x1f;

    /// <param name="model">The page read, or null when none is shown (a status in its place).</param>
    /// <param name="alert">A word the window speaks at once and shows first (a status, or the outcome of a press); null for none.</param>
    /// <param name="extras">What the app adds to the page, in order (the bridge decides, by page).</param>
    /// <param name="numberFor">The number (aidx) of an element, from its identity on the page: its key, else its words, and which of
    /// the page's elements with that identity it is ("btn:HOME#1"); a question's answer also carries its question's identity, its words
    /// (<see cref="Bbs757EfbPress.QuestionIdentity"/>), as every question's answers are keyed alike. The bridge keeps an element's number
    /// while the element stays and never gives one number to two elements (<see cref="Bbs757EfbPushes"/>), so a press the window
    /// sends names the element it was made on, even after a later push. Null numbers the elements 1, 2, 3 and so on.</param>
    public static Built Build(Bbs757EfbScreenModel? model, string? alert, IReadOnlyList<Bbs757EfbExtraElement> extras,
        Func<string, int>? numberFor = null)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { ["page"] = model?.PageName ?? "BlueBird tablet" };
        var actions = new Dictionary<int, Action>();
        var identities = new Dictionary<string, int>(StringComparer.Ordinal);
        int n = 0;

        void Add(string text, string kind = "", string controlType = "", bool clickable = false, int level = 0,
            string value = "", string key = "", IReadOnlyList<string>? options = null,
            string live = "", double? min = null, double? max = null, double? step = null, Action? action = null,
            string? of = null, string commit = "")
        {
            int i = n++;
            string identity = (key.Length > 0 ? key : "text|" + text) + (of != null ? "|" + of : "");
            int occurrence = identities[identity] = identities.GetValueOrDefault(identity) + 1;
            int aidx = numberFor?.Invoke(identity + "#" + occurrence.ToString(CultureInfo.InvariantCulture)) ?? i + 1;
            data[$"items.{i}.aidx"] = aidx.ToString(CultureInfo.InvariantCulture);
            data[$"items.{i}.text"] = text;
            data[$"items.{i}.tag"] = "";
            data[$"items.{i}.role"] = "";
            data[$"items.{i}.value"] = value;
            data[$"items.{i}.type"] = controlType;
            data[$"items.{i}.clickable"] = clickable ? "true" : "false";
            data[$"items.{i}.kind"] = kind;
            data[$"items.{i}.level"] = level.ToString(CultureInfo.InvariantCulture);
            data[$"items.{i}.live"] = live;
            data[$"items.{i}.disabled"] = "false";
            if (key.Length > 0) data[$"items.{i}.key"] = key;
            if (options != null) data[$"items.{i}.options"] = string.Join(OptionSeparator, options);
            if (min is double mn) data[$"items.{i}.min"] = mn.ToString(CultureInfo.InvariantCulture);
            if (max is double mx) data[$"items.{i}.max"] = mx.ToString(CultureInfo.InvariantCulture);
            if (step is double sp) data[$"items.{i}.step"] = sp.ToString(CultureInfo.InvariantCulture);
            if (commit.Length > 0) data[$"items.{i}.commit"] = commit;
            if (action != null) actions[aidx] = action;
        }

        // The page name is the window's own title line and page announcement ("page" above), so it is
        // not repeated as a list item.
        if (alert != null) Add(alert, kind: "alert", live: "assertive", key: "alert");

        if (model != null)
        {
            // Under a question the tablet dims its page and takes no tap on it.
            bool dimmed = model.Question != null;
            if (model.Question is { } question)
            {
                // Text: an alert here would be said again whenever another alert went (see the class summary).
                if (question.Text.Length > 0) Add(question.Text, key: "question");
                // A text box in it (the fuel input dialog's quantity): an edit box, its unit after its name ("Fuel (Gal)"); the dialog's
                // answers stay its own (INSERT is the pilot's to press once it is typed).
                foreach (var row in question.Rows)
                    AddField(row);
                // Each answer is its question's: the next question's YES is another element, under the same key.
                string asked = Bbs757EfbPress.QuestionIdentity(question);
                foreach (var answer in question.Buttons)
                    Add(answer.Text, kind: "button", clickable: true, key: answer.Key, action: new Action(ActionKind.Tap, answer), of: asked);
            }

            foreach (var it in model.Items)
                AddItem(it, model.PageName, dimmed);
        }

        foreach (var extra in extras)
        {
            if (extra.IsButton)
                Add(extra.Text, kind: "button", clickable: true, key: extra.Key, action: new Action(ActionKind.Extra, ExtraKey: extra.Key));
            else
                Add(extra.Text, key: extra.Key);
        }

        if (model != null)
        {
            bool dimmed = model.Question != null;
            if (model.TopBar.Count > 0 || model.Notices.Count > 0)
            {
                Add(StatusBarHeading, level: 2, key: "h:status");
                foreach (var barItem in model.TopBar)
                    AddTopBar(barItem);
                if (model.Notices.Count > 0)
                {
                    Add(NotificationsHeading, level: 2, key: "h:notifications");
                    // Keyed by BlueBird's id: a new one above it never moves another's identity (the same words twice are two).
                    foreach (var notice in model.Notices)
                        Add(NoticeText(notice), key: "notice:" + notice.Id.ToString(CultureInfo.InvariantCulture));
                }
            }

            bool homeShown = model.App != "HOME";
            if (model.Menu.Count > 0 || homeShown)
                Add(SectionsHeading, level: 2, key: "h:sections");
            foreach (var entry in model.Menu)
                AddItem(entry, model.PageName, dimmed);
            if (homeShown)
            {
                if (dimmed)
                    Add("Home", key: "btn:HOME");
                else
                {
                    var home = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", Rectangle.Empty, Bbs757EfbScreen.HomeButton, true, "btn:HOME");
                    Add("Home", kind: "button", clickable: true, key: "btn:HOME", action: new Action(ActionKind.Tap, home));
                }
            }
        }
        return new Built(data, actions, n);

        // A field. A text box whose caret was read (plan 22): an Enter-only edit box holding the box's own text exactly as the tablet shows it
        // (D2: a credential too), named as the window names it (NameOf), its set typed on the tablet's keyboard or keypad
        // (Bbs757EfbPressRunner.TypeAsync); the shared window posts it on Enter alone and says "<name> not changed" when the pilot leaves it
        // with other text (Task 4). A value box (BlueBird's button holding a number, Bbs757EfbItem.ValueBox): a button in its line's words
        // ("Quantity: 1539 Gal"), tapped on the tablet as any button, which opens BlueBird's dialog for it (the fuel input dialog, whose
        // box is then the edit box). Any other field (a text box whose caret was not read, an input not read): a line of its name and
        // what it holds ("Airport: KPHX"); an empty one its name alone.
        void AddField(Bbs757EfbItem it)
        {
            if (it.TextState != null)
                Add(NameOf(it), controlType: "text", clickable: true, value: it.Value, key: it.Key, commit: "enter",
                    action: new Action(ActionKind.Tap, it));
            else if (it.ValueBox)
                Add(FieldLine(it), kind: "button", clickable: true, key: it.Key, action: new Action(ActionKind.Tap, it));
            else
                Add(FieldLine(it), key: it.Key);
        }

        // The top bar stays lit above a question's dimming: its buttons press whatever the page does.
        void AddTopBar(Bbs757EfbItem it)
        {
            if (it.Kind != Bbs757EfbKind.Button)
            {
                Add(it.Text, key: it.Key);
                return;
            }
            Add(it.Text, kind: "button", clickable: true, key: it.Key, action: new Action(ActionKind.Tap, it));
        }

        void AddItem(Bbs757EfbItem it, string pageName, bool dimmed)
        {
            // The EFB colors theme is never a control (B757-21): its row is the note, whatever the reader made of it.
            if (ThemeNoteFor(it, pageName) is { } note)
            {
                Add(note, key: it.Key);
                return;
            }
            if (dimmed && AddDimmed(it, pageName)) return;
            switch (it.Kind)
            {
                case Bbs757EfbKind.Heading:
                    Add(Bbs757EfbVocabulary.Pretty(it.Text), level: it.Level == 0 ? 2 : it.Level, key: it.Key);
                    break;
                case Bbs757EfbKind.Text:
                    Add(it.Text, key: it.Key);
                    break;
                case Bbs757EfbKind.Row:
                    Add(Named(Bbs757EfbVocabulary.Pretty(it.Text), it.Value), key: it.Key);
                    break;
                case Bbs757EfbKind.Field:
                    AddField(it);
                    break;
                case Bbs757EfbKind.Choice:
                    // A drop-down: a choice list of its entries valued by the one it shows, chosen from by tapping the tablet
                    // (Bbs757EfbPressRunner.ChooseAsync, B757-27). One that shows none of them (Systems' SELECT) starts with an empty
                    // choice, so the list never reads an entry as chosen.
                    Add(Bbs757EfbVocabulary.Pretty(it.Text), controlType: "select", clickable: true, value: it.Value, key: it.Key,
                        options: ChoiceOptions(it), action: new Action(ActionKind.Tap, it));
                    break;
                case Bbs757EfbKind.LongText:
                    Add(it.Text, controlType: "pre", key: it.Key);
                    break;
                case Bbs757EfbKind.Menu:
                    Add(MenuText(it), kind: "button", clickable: true, key: it.Key, action: new Action(ActionKind.Tap, it));
                    break;
                case Bbs757EfbKind.Tab:
                case Bbs757EfbKind.Button:
                    Add(Labelled(it), kind: "button", clickable: true, key: it.Key, action: new Action(ActionKind.Tap, it));
                    break;
                case Bbs757EfbKind.Toggle:
                {
                    // A two-choice switch ("Nosewheel steering": Realistic or Rudder) is a choice list of its two words (D4).
                    if (ShowsAsChoiceList(it, pageName))
                    {
                        var pair = TwoChoicesFor(it, pageName)!;
                        int chosen = Bbs757EfbSettingsMatch.ChoiceIndex(Bbs757EfbSettingsMatch.SettingFor(pageName, it)!, it.Value);
                        Add(Bbs757EfbVocabulary.Pretty(it.Text), controlType: "select", clickable: true, value: pair[chosen], key: it.Key,
                            options: pair, action: new Action(ActionKind.Tap, it));
                        break;
                    }
                    string word = it.Value;
                    bool plain = word.Length == 0 || IsOnOff(word);
                    string toggleName = Bbs757EfbVocabulary.Pretty(it.Text);
                    Add(plain ? toggleName : toggleName + ": " + word, controlType: "checkbox", clickable: true,
                        value: it.On == true ? "true" : "false", key: it.Key, action: new Action(ActionKind.Tap, it));
                    break;
                }
                case Bbs757EfbKind.Checkbox:
                    Add(Bbs757EfbVocabulary.Pretty(it.Text), controlType: "checkbox", clickable: true, value: it.On == true ? "true" : "false",
                        key: it.Key, action: new Action(ActionKind.Tap, it));
                    break;
                case Bbs757EfbKind.Slider:
                {
                    var options = OptionsFor(it, pageName);
                    int at = options == null ? -1 : CurrentChoice(it, pageName, options);
                    string sliderName = SliderName(it);
                    if (options != null && at >= 0)
                        Add(sliderName, controlType: "select", clickable: true, value: options[at], key: it.Key, options: options,
                            action: new Action(ActionKind.Tap, it));
                    else if (it.Slider is { } sl)
                    {
                        // No known choices (the cabin lights), or the one set was not read: a 0-100 slider from the knob's place.
                        string label = it.Value.Length > 0 ? sliderName + " (" + it.Value + ")" : sliderName;
                        Add(label, controlType: "range", clickable: true, value: KnobPercent(sl).ToString(CultureInfo.InvariantCulture),
                            key: it.Key, min: 0, max: 100, step: 10, action: new Action(ActionKind.Tap, it));
                    }
                    break;
                }
            }
        }

        // A control as text, for the page behind a question: its words and the state read for it. False for the kinds that render the
        // same either way (headings, text, rows).
        bool AddDimmed(Bbs757EfbItem it, string pageName)
        {
            switch (it.Kind)
            {
                case Bbs757EfbKind.Menu:
                    Add(MenuText(it), key: it.Key);
                    return true;
                case Bbs757EfbKind.Tab:
                case Bbs757EfbKind.Button:
                    Add(Labelled(it), key: it.Key);
                    return true;
                case Bbs757EfbKind.Toggle:
                case Bbs757EfbKind.Checkbox:
                    Add(Bbs757EfbVocabulary.Pretty(it.Text) + ": " + StateWord(it), key: it.Key);
                    return true;
                case Bbs757EfbKind.Slider:
                    Add(SliderStateText(it, pageName), key: it.Key);
                    return true;
                case Bbs757EfbKind.Choice:
                    Add(Named(Bbs757EfbVocabulary.Pretty(it.Text), it.Value), key: it.Key);
                    return true;
                case Bbs757EfbKind.Field:
                    Add(FieldLine(it), key: it.Key);
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// The text that stands in the place of the EFB colors theme's row (<see cref="Bbs757EfbSettings.ThemeNote"/>), or null for any
    /// other element. Neither 757 EFB window offers the theme as a choice (the owner's ruling of 2026-10-30, B757-21): the reader
    /// reads only the default theme, and under a light one it found no switch, slider or choice (measured under Light blue: every
    /// line of Settings, General a row), so a pilot who picked one could not get back here (the settings window's Set to Dark blue
    /// is the way back, with the simulator closed). The row is the one
    /// <see cref="Bbs757EfbSettingsMatch.SettingFor"/> matches to the catalog's theme, whatever kind the reader gave it (a slider
    /// under the default theme, a row under a light one); its choice is the catalog's word for the one read, or none when the
    /// reader read none of them. On a Settings read that names no section (<see cref="SectionlessSettings"/>: no side-menu entry read
    /// selected, or two, as a read mid-update may give: B757-22) the row is matched by its label to the theme's all the same, since the
    /// section rests on the selected state alone and such a read would otherwise offer the theme as a percent slider (the final review
    /// of plan 21, M1). Internal for the tests.
    /// </summary>
    internal static string? ThemeNoteFor(Bbs757EfbItem it, string pageName)
    {
        if (ThemeSettingOf(it, pageName) is not { } theme) return null;
        int read = Bbs757EfbSettingsMatch.ChoiceIndex(theme, it.Value);
        return Bbs757EfbSettings.ThemeNote(read >= 0 ? theme.Choices[read].Word : null);
    }

    /// <summary>The catalog's EFB colors theme when <paramref name="it"/> is its row: the setting <see cref="Bbs757EfbSettingsMatch.SettingFor"/>
    /// matches on the page, or, on a Settings read that names no section, the one its label matches on Settings, General, the theme's own
    /// page; null for any other row.</summary>
    private static Bbs757Setting? ThemeSettingOf(Bbs757EfbItem it, string pageName)
    {
        if (Bbs757EfbSettingsMatch.SettingFor(pageName, it) is { Path: Bbs757EfbSettings.ThemePath } theme) return theme;
        return string.Equals(pageName, SectionlessSettings, StringComparison.Ordinal)
            && Bbs757EfbSettingsMatch.SettingFor(ThemePage, it) is { Path: Bbs757EfbSettings.ThemePath } byLabel
            ? byLabel
            : null;
    }

    /// <summary>The name of a Settings page whose section was not read (<see cref="Bbs757EfbScreenModel.PageName"/>: the app alone).</summary>
    private static readonly string SectionlessSettings = Bbs757EfbVocabulary.Pretty("SETTINGS");

    /// <summary>The page the EFB colors theme is on, as <see cref="Bbs757EfbSettingsMatch.SectionFor"/> names it.</summary>
    private const string ThemePage = "Settings, General";

    /// <summary>A name and its value as one line with one colon: a name the tablet writes with its own colon ("Airport:") gets no
    /// second, and one it ends with a sentence's period loses that one period before the colon (signed out of Navigraph, the AIRAC
    /// page's "Sign in to Navigraph." names its SIGN IN: "Sign in to Navigraph: Sign in", measured 2026-11-07); two dots or more are
    /// an ellipsis and stay. No value is the name alone, its period kept.</summary>
    private static string Named(string name, string value)
    {
        if (value.Length == 0) return name;
        if (name.EndsWith(':')) return name + " " + value;
        if (name.Length > 1 && name.EndsWith('.') && !name.EndsWith("..", StringComparison.Ordinal)) name = name[..^1];
        return name + ": " + value;
    }

    /// <summary>A control's words, with the label that names it before them when the page grammar gave one ("Left FWD: Closed"); a
    /// tab says "(current)" after its words when it is the one showing. Both parts are names: sentence case.</summary>
    private static string Labelled(Bbs757EfbItem it)
    {
        string text = NameOf(it);
        return it.Kind == Bbs757EfbKind.Tab && it.Selected ? text + " (current)" : text;
    }

    /// <summary>
    /// The name the window gives a page control, in sentence case as it says it, without the "(current)" of a tab: a button's or tab's
    /// words after the label that names it ("Left FWD: Open", "Latest cycle: Remove"), a slider's label or words, a field with a unit
    /// beside it its words and the unit as BlueBird writes it in parentheses ("Fuel (Gal)", plan 22), any other control's words. What
    /// the press runner says of a control names it so too (<see cref="Bbs757EfbPress.SpokenName"/>).
    /// </summary>
    internal static string NameOf(Bbs757EfbItem it) => it.Kind switch
    {
        Bbs757EfbKind.Slider => SliderName(it),
        Bbs757EfbKind.Button or Bbs757EfbKind.Tab when it.Label.Length > 0 =>
            Named(Bbs757EfbVocabulary.Pretty(it.Label), Bbs757EfbVocabulary.Pretty(it.Text)),
        Bbs757EfbKind.Field when it.Unit.Length > 0 => Bbs757EfbVocabulary.Pretty(it.Text) + " (" + it.Unit + ")",
        _ => Bbs757EfbVocabulary.Pretty(it.Text),
    };

    /// <summary>A field as a line: its name and what it holds, a dialog box's unit after its text as the tablet draws it ("Fuel: 752
    /// Gal"); an empty one is its name alone.</summary>
    private static string FieldLine(Bbs757EfbItem it) =>
        Named(Bbs757EfbVocabulary.Pretty(it.Text), it.Unit.Length > 0 && it.Value.Length > 0 ? it.Value + " " + it.Unit : it.Value);

    /// <summary>A slider's name: its label when the page grammar gave one, else its words, in sentence case.</summary>
    private static string SliderName(Bbs757EfbItem it) => Bbs757EfbVocabulary.Pretty(it.Label.Length > 0 ? it.Label : it.Text);

    /// <summary>The two words of a switch that is a choice between two things, for a setting whose catalog gives exactly two
    /// choices other than Off and On; null for any other switch.</summary>
    internal static IReadOnlyList<string>? TwoChoicesFor(Bbs757EfbItem it, string pageName)
    {
        if (Bbs757EfbSettingsMatch.SettingFor(pageName, it) is not { } setting || Bbs757EfbSettingsMatch.ChoicesFor(setting) is not { Count: 2 } words)
            return null;
        return words.Any(IsOnOff) ? null : words;
    }

    /// <summary>Whether the window shows a switch as a choice list of its two words (D4): a two-choice setting
    /// (<see cref="TwoChoicesFor"/>) whose word was read; any other switch is a check box.</summary>
    internal static bool ShowsAsChoiceList(Bbs757EfbItem it, string pageName) =>
        it.Kind == Bbs757EfbKind.Toggle && TwoChoicesFor(it, pageName) != null
        && Bbs757EfbSettingsMatch.ChoiceIndex(Bbs757EfbSettingsMatch.SettingFor(pageName, it)!, it.Value) >= 0;

    /// <summary>A drop-down's choices: its entries in BlueBird's order, after an empty one when it shows none of them, or after the
    /// words it shows when they are no entry read (a read with no entries: the face is its value, Task 5's review M2), so the list's
    /// value is always one of its choices.</summary>
    private static IReadOnlyList<string> ChoiceOptions(Bbs757EfbItem it)
    {
        var entries = it.Entries ?? Array.Empty<string>();
        return entries.Contains(it.Value) ? entries : new[] { it.Value }.Concat(entries).ToList();
    }

    /// <summary>A side-menu entry's words, "(current page)" after them on the entry of the page shown (a tab says "(current)",
    /// <see cref="Labelled"/>).</summary>
    private static string MenuText(Bbs757EfbItem it)
    {
        string words = Bbs757EfbVocabulary.Pretty(it.Text);
        return it.Selected ? words + " (current page)" : words;
    }

    /// <summary>A notification as a line of text: the time the tablet shows over its words, when it was read, then the words.</summary>
    private static string NoticeText(Bbs757EfbNotice notice) =>
        notice.Time is { Length: > 0 } time ? time + " " + notice.Text : notice.Text;

    private static bool IsOnOff(string word) =>
        word.Equals("On", StringComparison.OrdinalIgnoreCase) || word.Equals("Off", StringComparison.OrdinalIgnoreCase);

    private static string StateWord(Bbs757EfbItem it) =>
        it.Value.Length > 0 && !IsOnOff(it.Value) ? it.Value : it.On switch { true => "On", false => "Off", _ => "state not read" };

    private static string SliderStateText(Bbs757EfbItem it, string pageName)
    {
        string value = it.Value;
        var options = OptionsFor(it, pageName);
        int at = options == null ? -1 : CurrentChoice(it, pageName, options);
        if (options != null && at >= 0)
            value = options[at];
        else if (value.Length == 0 && it.Slider is { } slider && slider.TrackRight != slider.TrackLeft)
            value = KnobPercent(slider).ToString(CultureInfo.InvariantCulture) + " percent";
        string name = SliderName(it);
        return value.Length > 0 ? name + ": " + value : name;
    }

    /// <summary>Where a slider's knob is along its track, in whole percent (from the bottom of a vertical one).</summary>
    private static double KnobPercent(Bbs757EfbSlider slider) => Math.Round(slider.PercentOf(slider.KnobX));

    /// <summary>
    /// A slider's choices, left to right, as the tablet offers them; null when the slider is a number. A row of the Settings pages that is
    /// one of BlueBird's settings (<see cref="Bbs757EfbSettingsMatch.SettingFor"/>, by the page and the row's label) offers the settings
    /// catalog's words in its order (<see cref="Bbs757EfbSettingsMatch.ChoicesFor"/>), which is the tablet's from left to right; a setting
    /// with no choices (a continuous slider) offers none. A row that matches no setting (a setting this BlueBird version does not know, a slider on another page)
    /// offers none either, so it is a number, and a press on it is judged by the page alone.
    /// </summary>
    public static IReadOnlyList<string>? OptionsFor(Bbs757EfbItem it, string pageName) =>
        Bbs757EfbSettingsMatch.SettingFor(pageName, it) is { } setting ? Bbs757EfbSettingsMatch.ChoicesFor(setting) : null;

    /// <summary>
    /// The index among <paramref name="options"/> (the list <see cref="OptionsFor"/> gave for the row the pilot pressed) of the choice
    /// the row reads as; -1 when the reader gave none or it is none of them. For a catalog setting the reader's words are matched with
    /// case, spaces and punctuation ignored ("FULL(7 MIN+)" is "Full (7 min+)": <see cref="Bbs757EfbSettingsMatch.ChoiceIndex"/>). A
    /// row that matches no setting is matched with case ignored: the press runner meets one when it finds the pressed slider again by
    /// its box under a label read otherwise (<see cref="Bbs757EfbPress.Relocate"/>), its choices still the pressed row's.
    /// </summary>
    public static int CurrentChoice(Bbs757EfbItem it, string pageName, IReadOnlyList<string> options)
    {
        if (Bbs757EfbSettingsMatch.SettingFor(pageName, it) is { } setting)
            return Bbs757EfbSettingsMatch.ChoiceIndex(setting, it.Value);
        for (int index = 0; index < options.Count; index++)
            if (string.Equals(options[index], it.Value, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
}
