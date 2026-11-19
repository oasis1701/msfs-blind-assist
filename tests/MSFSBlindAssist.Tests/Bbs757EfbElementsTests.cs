using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 tablet window's elements (plan 18, Task 7): a read page as the shared EFB window's push
/// (<see cref="Bbs757EfbElements.Build"/>), in the order the pilot meets it, with every control the reader offers pressable and
/// nothing refused (the owner's ruling of 2026-10-29). Pages are the 2026-10-29 captures (tests/MSFSBlindAssist.Tests/Fixtures/
/// bbs757-efb); a few controls are built in memory where no capture holds them. A refresh of an unchanged tablet says the same
/// thing in the same order (the reading-order tests).
/// </summary>
public class Bbs757EfbElementsTests
{
    // ---- helpers -------------------------------------------------------------------------------------------------------

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static readonly IReadOnlyList<Bbs757EfbExtraElement> NoExtras = Array.Empty<Bbs757EfbExtraElement>();

    private const char OptionSeparator = (char)0x1f;

    private static Bbs757EfbElements.Built BuildOf(string set, string name, string? alert = null) =>
        Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load(set, name), alert, NoExtras);

    private static string Field(Bbs757EfbElements.Built b, int index, string field) =>
        b.Data.TryGetValue($"items.{index}.{field}", out var v) ? v : "";

    private static List<string> Texts(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count).Select(i => Field(b, i, "text")).ToList();

    private static List<string> Keys(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count).Select(i => Field(b, i, "key")).ToList();

    private static int IndexOf(Bbs757EfbElements.Built b, string text)
    {
        int index = Texts(b).IndexOf(text);
        Assert.True(index >= 0, $"No element '{text}': " + string.Join(" | ", Texts(b)));
        return index;
    }

    private static Bbs757EfbElements.Action? ActionAt(Bbs757EfbElements.Built b, int index) =>
        b.Actions.TryGetValue(int.Parse(Field(b, index, "aidx")), out var action) ? action : null;

    private static string[] OptionsAt(Bbs757EfbElements.Built b, int index) =>
        Field(b, index, "options").Split(OptionSeparator);

    // A control the pilot can press: clickable, never dimmed, and a tap on the tablet control it was read from.
    private static Bbs757EfbElements.Action AssertPressable(Bbs757EfbElements.Built b, string text)
    {
        int index = IndexOf(b, text);
        Assert.Equal("true", Field(b, index, "clickable"));
        Assert.Equal("false", Field(b, index, "disabled"));
        var action = ActionAt(b, index);
        Assert.NotNull(action);
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, action!.Kind);
        Assert.NotNull(action.Item);
        return action;
    }

    // Text on the window: no control behind it, nothing to press.
    private static void AssertText(Bbs757EfbElements.Built b, string text)
    {
        int index = IndexOf(b, text);
        Assert.Equal("false", Field(b, index, "clickable"));
        Assert.Equal("", Field(b, index, "kind"));
        Assert.Equal("", Field(b, index, "type"));
        Assert.Null(ActionAt(b, index));
    }

    private static Bbs757EfbItem Control(Bbs757EfbKind kind, string text, string key, Rectangle box, bool clickable,
        bool? on = null, string value = "", Bbs757EfbSlider? slider = null) =>
        new(kind, text, box, new Point(box.Left + box.Width / 2, box.Top + box.Height / 2), clickable, key, Bbs757EfbTint.Blue,
            on, Value: value, Slider: slider);

    private static Bbs757EfbItem Answer(string word, string key = "") =>
        Control(Bbs757EfbKind.Button, word, key.Length > 0 ? key : "q:" + word.ToUpperInvariant(), new Rectangle(336, 591, 177, 57), true);

    // ---- every control is pressable ------------------------------------------------------------------------------------

    [Fact]
    public void Every_control_is_pressable()
    {
        // Ground services, REMOVE ALL among them (it used to be refused with external power on line).
        var ground = BuildOf("bright", "13-aircraft-ground-services");
        foreach (var tile in new[] { "Remove all", "Jetway removed", "GPU removed", "Chocks, on", "Fuel truck off", "Ground A/C", "GND SRVC Mode: BBS" })
            AssertPressable(ground, tile);

        // Presets (they used to wait for the ground or the air) and the custom panel states' SAVE and LOAD.
        var presets = BuildOf("bright", "10-aircraft-presets");
        foreach (var tile in new[] { "Cold & dark", "Ready for takeoff", "After takeoff", "Save user config", "Load user config" })
            AssertPressable(presets, tile);

        // Settings: the switches, with the word of their state, and the slider.
        var settings = BuildOf("bright", "51-settings-aircraft");
        foreach (var row in new[] { "Sync barometers", "Automatic boarding", "Nosewheel steering", "IRS alignment time" })
            AssertPressable(settings, row);

        // Callouts: Rotate and Positive rate sit where the AIRAC page's UPDATE and REMOVE are drawn (x 1191-1391, y 297-377).
        var callouts = BuildOf("bright", "53-settings-callouts");
        foreach (var row in new[] { "80 KTS", "Rotate", "Positive rate" })
            AssertPressable(callouts, row);

        // The checklist: a line's box, and the side menu's own switch and button.
        var checklist = BuildOf("bright", "40-checklist");
        foreach (var row in new[] { "Reset all", "Interactive", "Parking brake: SET", "Reset" })
            AssertPressable(checklist, row);
    }

    [Fact]
    public void The_navigation_data_buttons_are_pressable_where_the_airac_page_draws_them()
    {
        // The AIRAC page's UPDATE or REMOVE (one spot, about x 1291, y 337) and a control beside it.
        var update = Control(Bbs757EfbKind.Button, "Update", "btn:|UPDATE@64,15", new Rectangle(1221, 307, 140, 40), true);
        var remove = Control(Bbs757EfbKind.Button, "Remove", "btn:|REMOVE@64,15", new Rectangle(1221, 307, 140, 40), true);
        var nearby = Control(Bbs757EfbKind.Toggle, "Some switch", "tgl:SOME SWITCH", new Rectangle(1211, 347, 100, 30), true, on: true);
        var model = new Bbs757EfbScreenModel("SETTINGS", "AIRAC DATA", Array.Empty<Bbs757EfbItem>(), new[] { update, remove, nearby });

        var b = Bbs757EfbElements.Build(model, null, NoExtras);

        Assert.Equal("Settings, AIRAC data", b.Data["page"]);
        Assert.Same(update, AssertPressable(b, "Update").Item);
        Assert.Same(remove, AssertPressable(b, "Remove").Item);
        Assert.Same(nearby, AssertPressable(b, "Some switch").Item);
        Assert.Equal("checkbox", Field(b, IndexOf(b, "Some switch"), "type"));
        Assert.Equal("true", Field(b, IndexOf(b, "Some switch"), "value"));
    }

    [Fact]
    public void The_airac_pages_captured_buttons_press_and_its_rows_are_text()
    {
        // bright/55 (the SimBrief ID painted out of the capture): UPDATE and the two LOGOUTs press, each tapping its own box; the
        // identifier rows, the cycles and the sign-in's words are text.
        var page = Bbs757EfbScreenTests.Load("bright", "55-settings-airac-data");
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        foreach (var (button, key) in new[]
        {
            ("Logout, SIMBRIEF ID number", "btn:SIMBRIEF ID NUMBER|LOGOUT@59,4"),
            ("Update, Latest cycle", "btn:LATEST CYCLE|UPDATE@59,14"),
            ("Logout, Successfully signed in to navigraph!", "btn:SUCCESSFULLY SIGNED IN TO NAVIGRAPH!|LOGOUT@59,18"),
        })
        {
            Assert.Equal(key, AssertPressable(b, button).Item!.Key);
            Assert.Equal("button", Field(b, IndexOf(b, button), "kind"));
        }
        foreach (var text in new[]
        {
            "SIMBRIEF ID number: not set", "Hoppie logon code: not set", "Sayintentions ACARS key: not set",
            "Latest cycle: 2611, Update available!", "Installed cycle: 2610 | exp: 2026-10-28", "Successfully signed in to navigraph!",
        })
            AssertText(b, text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_readers_clickable_flag_decides_nothing(bool flag)
    {
        // A button, a switch, a check box, a slider and a tab press whatever the flag says; a row, a text and a heading never do.
        var track = new Bbs757EfbSlider(895, 1190, 1000);
        Bbs757EfbItem Make(Bbs757EfbKind kind, string text, Bbs757EfbSlider? slider = null) =>
            Control(kind, text, "k:" + text, new Rectangle(300, 200, 120, 30), flag, slider: slider);
        var menu = Make(Bbs757EfbKind.Menu, "Side entry");
        var tab = Control(Bbs757EfbKind.Menu, "Landing gear", "tab:|LANDING GEAR@10,10", new Rectangle(500, 100, 120, 30), flag);
        var items = new[]
        {
            Make(Bbs757EfbKind.Button, "A tile"), Make(Bbs757EfbKind.Toggle, "A switch"), Make(Bbs757EfbKind.Checkbox, "A check box"),
            Make(Bbs757EfbKind.Slider, "A slider", track), tab,
            Make(Bbs757EfbKind.Row, "A row"), Make(Bbs757EfbKind.Text, "A text"), Make(Bbs757EfbKind.Heading, "A heading"),
        };
        var model = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { menu }, items);

        var b = Bbs757EfbElements.Build(model, null, NoExtras);

        foreach (var text in new[] { "Side entry", "A tile", "A switch", "A check box", "A slider", "Landing gear" })
            AssertPressable(b, text);
        foreach (var text in new[] { "A row", "A text", "A heading" })
        {
            int index = IndexOf(b, text);
            Assert.Equal("false", Field(b, index, "clickable"));
            Assert.Null(ActionAt(b, index));
        }
    }

    [Fact]
    public void No_element_asks_the_window_to_say_its_new_words()
    {
        // One speaker: a pressed control's new words are the press's to say, once (Bbs757EfbPress.ButtonOutcome), whatever its key.
        // The window's own announceChange would speak only a node it kept (the same key), so a tile relabeled under a new key
        // ("Req fuel truck" to "Requesting...") was silent while one under its key would be said twice.
        foreach (var (set, name) in new[]
        {
            ("bright", "13-aircraft-ground-services"), ("bright", "18-aircraft-ground-services-notification"), ("bright", "19-notification-list"),
            ("bright", "14-aircraft-cabin-doors"), ("bright", "21-defuel-question"), ("dim", "15-aircraft-maintenance"),
        })
        {
            var b = BuildOf(set, name);
            Assert.DoesNotContain(b.Data.Keys, key => key.EndsWith(".announceChange", StringComparison.Ordinal));
        }
        // The tiles whose words carry their state still press.
        var ground = BuildOf("bright", "13-aircraft-ground-services");
        AssertPressable(ground, "Fuel truck off");
        AssertPressable(ground, "GND SRVC Mode: BBS");
    }

    // ---- the order -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_question_comes_first_as_text()
    {
        // The window speaks only its first alert and remembers one text: a question kept as an alert would be said again whenever
        // another alert went. The bridge says a new question through its own alert, once (plan 18, Task 8); here it is text.
        var page = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        Assert.Equal(page.Question!.Text, Field(b, 0, "text"));
        Assert.Equal("question", Field(b, 0, "key"));
        AssertText(b, page.Question.Text);
        Assert.Equal("", Field(b, 0, "live"));
        Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "kind") == "alert");
        // Its answers follow at once, left to right, each pressing its own control on the tablet.
        Assert.Equal(new[] { "Yes", "No" }, Texts(b).Skip(1).Take(2));
        for (int answer = 0; answer < 2; answer++)
        {
            Assert.Equal("button", Field(b, 1 + answer, "kind"));
            Assert.Equal("true", Field(b, 1 + answer, "clickable"));
            var action = ActionAt(b, 1 + answer)!;
            Assert.Equal(Bbs757EfbElements.ActionKind.Tap, action.Kind);
            Assert.Same(page.Question.Buttons[answer], action.Item);
        }
        Assert.Equal(new[] { "q:YES", "q:NO" }, Keys(b).Skip(1).Take(2));
    }

    [Fact]
    public void A_question_with_one_answer_has_one_button()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "22-fuel-adjustment-complete");
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        Assert.Equal("Fuel adjustment is complete!", Field(b, 0, "text"));
        AssertText(b, "Fuel adjustment is complete!");
        Assert.Same(Assert.Single(page.Question!.Buttons), AssertPressable(b, "OK").Item);
    }

    [Fact]
    public void An_alert_is_ahead_of_the_question_and_is_the_only_alert()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");
        string alert = Bbs757EfbPressRunner.NotShownText("Fuel");
        var b = Bbs757EfbElements.Build(page, alert, NoExtras);

        Assert.Equal(alert, Field(b, 0, "text"));
        Assert.Equal("alert", Field(b, 0, "kind"));
        Assert.Equal("assertive", Field(b, 0, "live"));
        Assert.Equal(page.Question!.Text, Field(b, 1, "text"));
        Assert.Equal("", Field(b, 1, "kind"));
        Assert.Single(Enumerable.Range(0, b.Count), i => Field(b, i, "kind") == "alert");
        Assert.Equal(new[] { "Yes", "No" }, Texts(b).Skip(2).Take(2));
    }

    [Fact]
    public void Each_element_is_numbered_by_its_identity_on_the_page()
    {
        // The bridge numbers an element by what it is (its key, else its words, and which of the page's elements with that identity
        // it is), so it can keep the number while the element stays and never give one number to two elements.
        var page = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var identities = new List<string>();
        var numbered = Bbs757EfbElements.Build(page, "Alert", NoExtras, identity =>
        {
            identities.Add(identity);
            return 1000 + identities.Count;
        });

        Assert.Equal(numbered.Count, identities.Count);
        Assert.Equal(identities.Count, identities.Distinct().Count());
        Assert.Equal(Enumerable.Range(1001, numbered.Count).Select(n => n.ToString()), Enumerable.Range(0, numbered.Count).Select(i => Field(numbered, i, "aidx")));
        Assert.Equal("alert#1", identities[0]);
        Assert.Contains("btn:HOME#1", identities);
        Assert.Equal("btn:HOME", numbered.Actions[1001 + identities.IndexOf("btn:HOME#1")].Item!.Key);

        // Without a numbering, 1, 2, 3...
        var plain = Bbs757EfbElements.Build(page, null, NoExtras);
        Assert.Equal(Enumerable.Range(1, plain.Count).Select(n => n.ToString()), Enumerable.Range(0, plain.Count).Select(i => Field(plain, i, "aidx")));
    }

    [Fact]
    public void An_answers_identity_carries_its_question()
    {
        // Every question's answers are keyed q:YES, q:NO: their identity also holds the question's words (case, spaces and
        // punctuation left out), so two questions' YES are two elements; the key the window reconciles by stays q:YES.
        var page = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");
        var identities = new List<string>();
        var b = Bbs757EfbElements.Build(page, null, NoExtras, identity =>
        {
            identities.Add(identity);
            return identities.Count;
        });

        string yes = identities[IndexOf(b, "Yes")];
        Assert.StartsWith("q:YES|", yes, StringComparison.Ordinal);
        Assert.Contains(Bbs757EfbPress.QuestionWords(page.Question!.Text), yes, StringComparison.Ordinal);
        Assert.Equal("q:YES", Field(b, IndexOf(b, "Yes"), "key"));
        Assert.DoesNotContain(" ", Bbs757EfbPress.QuestionWords(page.Question.Text));
    }

    [Fact]
    public void The_page_behind_a_question_is_text_while_the_answers_and_the_top_bar_press()
    {
        // The tablet dims its page under a question and takes no tap on it: the window offers none either (this mirrors the tablet).
        var page = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        // Fuel's band (gap 11.2 from the median, the next 0.3) was missed by the old fixed threshold of 12: it is the current section. The three expectations below ("Fuel (current page)", "Import from SIMBRIEF", "Center tank: 14%") follow from the page now reading as the Fuel page.
        foreach (var behind in new[] { "Home", "Cockpit presets", "Fuel (current page)", "Ground services", "Import from SIMBRIEF" })
            AssertText(b, behind);
        AssertPressable(b, "Yes");
        AssertPressable(b, "No");
        AssertPressable(b, "Start boarding");
        AssertPressable(b, "Notifications");

        var taps = b.Actions.OrderBy(a => a.Key).Where(a => a.Value.Kind == Bbs757EfbElements.ActionKind.Tap).Select(a => a.Value.Item!.Key);
        Assert.Equal(new[] { "q:YES", "q:NO", "top:boarding", "top:notifications" }, taps);
        // The page's read-outs are still there to read.
        Assert.Contains("Center tank: 14%", Texts(b));
    }

    [Fact]
    public void A_switch_or_slider_behind_a_question_is_text_with_its_state()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft")
            with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Answer("Yes") }) };
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        var texts = Texts(b);
        Assert.Contains("Sync barometers: Off", texts);
        Assert.Contains("Nosewheel steering: Realistic", texts);
        Assert.Contains("IRS alignment time: Full (7 min+)", texts);
        Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "type") is "checkbox" or "select" or "range");
        AssertPressable(b, "Yes");
    }

    [Fact]
    public void Top_bar_items_follow_the_question_and_keep_their_keys()
    {
        var b = BuildOf("bright", "21-defuel-question");

        // The question and its answers lead; the top bar, left to right, sits under "Status bar" after the page.
        var keys = Keys(b);
        Assert.Equal(new[] { "question", "q:YES", "q:NO" }, keys.Take(3));
        int status = keys.IndexOf("h:status");
        Assert.True(status > 3);
        Assert.Equal(new[] { "h:status", "top:dep", "top:boarding", "top:notifications" }, keys.Skip(status).Take(4));
        Assert.Equal(new[] { "Status bar", "DEP -03:19", "Start boarding", "Notifications" }, Texts(b).Skip(status).Take(4));
        // The countdown is a read-out; the boarding control and the notifications are buttons on the tablet's own controls.
        AssertText(b, "DEP -03:19");
        Assert.Equal("top:boarding", AssertPressable(b, "Start boarding").Item!.Key);
        Assert.Equal("top:notifications", AssertPressable(b, "Notifications").Item!.Key);
        Assert.Equal("button", Field(b, status + 2, "kind"));
        Assert.Equal("button", Field(b, status + 3, "kind"));
    }

    [Fact]
    public void The_top_bar_follows_the_page_under_a_status_bar_heading()
    {
        var b = BuildOf("bright", "13-aircraft-ground-services");

        int status = Keys(b).IndexOf("h:status");
        Assert.True(status > 0);
        Assert.Equal("2", Field(b, status, "level"));
        Assert.Equal("top:notifications", Field(b, status + 1, "key"));
        Assert.Equal("btn:HOME", Keys(b)[^1]);
        AssertPressable(b, "Home");
    }

    [Fact]
    public void The_boarding_control_keeps_its_key_as_its_words_change()
    {
        // START BOARDING is a button; while boarding runs the same place reads "Boarding… 10 min", text, under the same key.
        var starting = BuildOf("bright", "20-fuel-start-boarding");
        var running = BuildOf("bright", "12-aircraft-fuel-boarding");

        Assert.Equal("top:boarding", AssertPressable(starting, "Start boarding").Item!.Key);
        Assert.Equal("top:boarding", Field(starting, IndexOf(starting, "Start boarding"), "key"));
        int index = IndexOf(running, "Boarding… 10 min");
        Assert.Equal("top:boarding", Field(running, index, "key"));
        AssertText(running, "Boarding… 10 min");
    }

    [Fact]
    public void The_notification_button_presses_whatever_it_says()
    {
        // Pressing it opens or closes BlueBird's list, and its words become "Close notifications" or back: the press says which
        // (Bbs757EfbPress.ButtonOutcome), under the key the button keeps.
        foreach (var (set, name, text) in new[]
        {
            ("bright", "13-aircraft-ground-services", "Notifications"),
            ("bright", "18-aircraft-ground-services-notification", "Notifications: 55"),
            ("bright", "19-notification-list", "Close notifications"),
        })
        {
            var b = BuildOf(set, name);
            Assert.Equal(Bbs757EfbTopBar.NotificationsKey, AssertPressable(b, text).Item!.Key);
            Assert.Equal(Bbs757EfbTopBar.NotificationsKey, Field(b, IndexOf(b, text), "key"));
        }
    }

    [Fact]
    public void Notifications_are_text_under_their_heading()
    {
        var b = BuildOf("bright", "19-notification-list");

        int heading = IndexOf(b, "Notifications");
        Assert.Equal("2", Field(b, heading, "level"));
        // After the top bar (its button reads Close notifications while the list is open), under the status bar.
        Assert.Equal("Close notifications", Field(b, heading - 1, "text"));
        Assert.Equal(new[]
        {
            "14:17Z GPU disconnected.", "14:17Z GPU connected.", "12:44Z R3 door closed.", "12:44Z R3 door opened.", "12:44Z L3 door closed.",
        }, Texts(b).Skip(heading + 1).Take(5));
        for (int row = heading + 1; row <= heading + 5; row++)
        {
            Assert.Equal("false", Field(b, row, "clickable"));
            Assert.Equal("", Field(b, row, "kind"));
            Assert.Equal("0", Field(b, row, "level"));
            Assert.Null(ActionAt(b, row));
        }
        Assert.Equal("Sections", Field(b, heading + 6, "text"));
    }

    [Fact]
    public void A_notification_without_its_time_is_its_words_alone()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services")
            with { Notices = new[] { new Bbs757EfbNotice(2, "Chocks attached.", null), new Bbs757EfbNotice(1, "GPU connected.", "14:17Z") } };
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        int heading = Enumerable.Range(0, b.Count).First(i => Field(b, i, "text") == "Notifications" && Field(b, i, "level") == "2");
        Assert.Equal(new[] { "Chocks attached.", "14:17Z GPU connected." }, Texts(b).Skip(heading + 1).Take(2));
    }

    [Fact]
    public void No_notifications_no_heading()
    {
        var b = BuildOf("bright", "13-aircraft-ground-services");

        Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "text") == "Notifications" && Field(b, i, "level") == "2");
    }

    [Fact]
    public void Extras_come_after_the_page_and_act_as_extras()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "53-settings-callouts");   // it scrolls: its own buttons end the window
        var extras = new[]
        {
            new Bbs757EfbExtraElement("extra:signin", Bbs757EfbExtras.SignInText, true),
            new Bbs757EfbExtraElement("extra:code", "Navigraph code: ABCD-1234", false),
        };

        var without = Texts(Bbs757EfbElements.Build(page, null, NoExtras));
        var b = Bbs757EfbElements.Build(page, null, extras);

        // The page's own content, then the extras, then the status bar, Sections and Home (the page's tail).
        int tail = without.IndexOf("Sections");
        if (without.Contains("Status bar")) tail = Math.Min(tail, without.IndexOf("Status bar"));
        Assert.True(tail > 0);
        Assert.Equal(without.Take(tail).Concat(new[] { "Sign in to Navigraph", "Navigraph code: ABCD-1234" }).Concat(without.Skip(tail)),
            Texts(b));

        int signIn = IndexOf(b, "Sign in to Navigraph");
        Assert.Equal("button", Field(b, signIn, "kind"));
        Assert.Equal("true", Field(b, signIn, "clickable"));
        Assert.Equal("extra:signin", Field(b, signIn, "key"));
        var action = ActionAt(b, signIn)!;
        Assert.Equal(Bbs757EfbElements.ActionKind.Extra, action.Kind);
        Assert.Equal("extra:signin", action.ExtraKey);
        Assert.Null(action.Item);

        int code = IndexOf(b, "Navigraph code: ABCD-1234");
        Assert.Equal("extra:code", Field(b, code, "key"));
        AssertText(b, "Navigraph code: ABCD-1234");
    }

    [Fact]
    public void Extras_are_offered_even_when_no_page_is_shown()
    {
        var b = Bbs757EfbElements.Build(null, "The tablet reader could not open.", new[] { new Bbs757EfbExtraElement("extra:signin", "Sign in to Navigraph", true) });

        Assert.Equal(new[] { "The tablet reader could not open.", "Sign in to Navigraph" }, Texts(b));
        Assert.Equal(Bbs757EfbElements.ActionKind.Extra, ActionAt(b, 1)!.Kind);
    }

    [Fact]
    public void No_classic_window_or_read_only_note()
    {
        // The window presses what the pilot presses (rule B757-19, plan 18): no hand-over to another window, no note that it only reads.
        Assert.Equal(new[] { "Tap", "Extra" }, Enum.GetNames<Bbs757EfbElements.ActionKind>());

        foreach (var (set, name) in new[]
        {
            ("bright", "13-aircraft-ground-services"), ("bright", "10-aircraft-presets"), ("bright", "51-settings-aircraft"),
            ("bright", "53-settings-callouts"), ("bright", "21-defuel-question"), ("dim", "01-home"),
        })
        {
            var b = BuildOf(set, name);
            var texts = Texts(b);

            Assert.DoesNotContain(texts, t => t.Contains("classic", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(texts, t => t.Contains("only reads", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(texts, t => t is "More" or "Read the tablet again" or "Scroll down" or "Scroll up");
            Assert.DoesNotContain(b.Actions.Values, a => a.Kind != Bbs757EfbElements.ActionKind.Tap && a.Kind != Bbs757EfbElements.ActionKind.Extra);
        }

        // Nothing to show: the alert alone.
        var none = Bbs757EfbElements.Build(null, "The BlueBird tablet cannot be reached. Is the 757 loaded?", NoExtras);
        Assert.Equal("BlueBird tablet", none.Data["page"]);
        Assert.Equal(new[] { "The BlueBird tablet cannot be reached. Is the 757 loaded?" }, Texts(none));
    }

    // ---- a Settings row's slider is a choice list in the catalog's order -----------------------------------------------

    [Fact]
    public void A_settings_slider_is_a_choice_list_in_the_catalogs_order()
    {
        var aircraft = BuildOf("bright", "51-settings-aircraft");
        int irs = IndexOf(aircraft, "IRS alignment time");
        Assert.Equal("select", Field(aircraft, irs, "type"));
        Assert.Equal(new[] { "Instant", "1 min", "2 min", "5 min", "Full (7 min+)" }, OptionsAt(aircraft, irs));
        Assert.Equal("Full (7 min+)", Field(aircraft, irs, "value"));   // the reader wrote FULL (7 MIN+): the choice it is
        int rate = IndexOf(aircraft, "Boarding & load rate");
        Assert.Equal(new[] { "Realistic", "Standard", "Quick", "Instant" }, OptionsAt(aircraft, rate));
        Assert.Equal("Realistic", Field(aircraft, rate, "value"));

        var general = BuildOf("dim", "50-settings-general");
        int timer = IndexOf(general, "Notifications fade-out timer");
        var seconds = OptionsAt(general, timer);
        Assert.Equal(29, seconds.Length);
        Assert.Equal("3 sec", seconds[0]);
        Assert.Equal("30 sec", seconds[^2]);
        Assert.Equal("Never", seconds[^1]);
        Assert.Equal("10 sec", Field(general, timer, "value"));
        int background = IndexOf(general, "EFB background");
        Assert.Equal(new[] { "Default", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, OptionsAt(general, background));
        Assert.Equal("Default", Field(general, background, "value"));
    }

    // ---- the EFB colors theme is a note (B757-21) ------------------------------------------------------------------------

    private const string ThemeNoteDarkBlue = "EFB colors theme: Dark blue. Non-default color schemes are not supported.";

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_colors_theme_row_is_a_note_with_no_action_read_as_a_slider_or_a_row(string culture)
    {
        // The owner's ruling of 2026-10-30: the reader reads only the default theme, and under a light one it found no switch,
        // slider or choice (measured under Light blue: every line of Settings, General a row). So the theme is offered as no
        // control: its row says the theme read and that other schemes are not supported, as a slider (dim/50) or a row.
        var general = Bbs757EfbScreenTests.Load("dim", "50-settings-general");
        var slider = general.Items.Single(i => i.Key == "sld:EFB COLORS THEME");
        var asRow = slider with { Kind = Bbs757EfbKind.Row, Key = "row:EFB COLORS THEME", Value = "Light blue", Slider = null };
        var lightPage = general with { Items = general.Items.Select(i => i.Key == slider.Key ? asRow : i).ToList() };

        var dark = TestCulture.Run(culture, () => Bbs757EfbElements.Build(general, null, NoExtras));
        var light = TestCulture.Run(culture, () => Bbs757EfbElements.Build(lightPage, null, NoExtras));

        AssertText(dark, ThemeNoteDarkBlue);
        Assert.Equal("sld:EFB COLORS THEME", Field(dark, IndexOf(dark, ThemeNoteDarkBlue), "key"));
        const string lightNote = "EFB colors theme: Light blue. Non-default color schemes are not supported.";
        AssertText(light, lightNote);
        Assert.Equal("row:EFB COLORS THEME", Field(light, IndexOf(light, lightNote), "key"));
        foreach (var b in new[] { dark, light })
        {
            Assert.DoesNotContain(Texts(b), t => t == "EFB colors theme" || t.StartsWith("EFB colors theme (", StringComparison.Ordinal));
            Assert.DoesNotContain(b.Actions.Values, a => a.Item?.Key is "sld:EFB COLORS THEME" or "row:EFB COLORS THEME");
            Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "options").Contains("Light blue", StringComparison.Ordinal));
        }
        // The page's other sliders are still choice lists.
        Assert.Equal("select", Field(dark, IndexOf(dark, "EFB background"), "type"));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_colors_theme_note_names_a_catalog_choice_or_none(string culture)
    {
        var slider = Bbs757EfbScreenTests.Load("dim", "50-settings-general").Items.Single(i => i.Key == "sld:EFB COLORS THEME");
        string? Note(Bbs757EfbItem it, string page = "Settings, General") => TestCulture.Run(culture, () => Bbs757EfbElements.ThemeNoteFor(it, page));

        Assert.Equal(ThemeNoteDarkBlue, Note(slider));                                   // DARK BLUE, as the reader writes it
        Assert.Equal("EFB colors theme: Light grey. Non-default color schemes are not supported.", Note(slider with { Value = "LIGHT GREY" }));
        // None read, or none of the catalog's words: the note alone.
        Assert.Equal("EFB colors theme. Non-default color schemes are not supported.", Note(slider with { Value = "" }));
        Assert.Equal("EFB colors theme. Non-default color schemes are not supported.", Note(slider with { Value = "LIGHT BLU" }));
        // A misread label, the same words on another page, and a heading are no theme row: they stay what they are.
        Assert.Null(Note(slider with { Text = "EFB colors thme" }));
        Assert.Null(Note(slider, "Aircraft, Ground services"));
        Assert.Null(Note(slider with { Kind = Bbs757EfbKind.Heading }));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_colors_theme_row_stays_a_note_on_a_settings_read_with_no_section(string culture)
    {
        // The final review's M1: the section rests on BlueBird's selected state alone (B757-22), and a read that marks no side-menu entry,
        // or two (a mid-update read right after a side-menu tap), names none: the page reads "Settings". The theme row is matched by its
        // label all the same, so it is never offered as a percent slider (B757-21). Settings, General as captured, the selection taken off.
        var stripped = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithoutSelected(Bbs757EfbTreeJson.Load("50-settings-general")))!;
        Assert.Equal("Settings", stripped.PageName);
        var theme = stripped.Items.Single(i => i.Key == "theme_slider");
        Assert.Equal(Bbs757EfbKind.Slider, theme.Kind);

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(stripped, null, NoExtras));

        AssertText(b, ThemeNoteDarkBlue);
        Assert.Equal("theme_slider", Field(b, IndexOf(b, ThemeNoteDarkBlue), "key"));
        Assert.DoesNotContain(b.Actions.Values, a => a.Item?.Key == "theme_slider");
        Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "type") == "range" && Field(b, i, "text").StartsWith("EFB colors theme", StringComparison.Ordinal));

        // The same label on another app's page with no section, or a misread one, is no theme row.
        Assert.Null(Bbs757EfbElements.ThemeNoteFor(theme, "Aircraft"));
        Assert.Null(Bbs757EfbElements.ThemeNoteFor(theme with { Text = "EFB colors thme" }, "Settings"));
    }

    [Fact]
    public void The_colors_theme_note_stays_under_a_question()
    {
        var page = Bbs757EfbScreenTests.Load("dim", "50-settings-general")
            with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Answer("Yes") }) };
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        AssertText(b, ThemeNoteDarkBlue);
        Assert.DoesNotContain("EFB colors theme: Dark blue", Texts(b));
    }

    [Fact]
    public void OptionsFor_is_the_catalogs_for_a_settings_row_and_nothing_for_any_other_slider()
    {
        var general = Bbs757EfbScreenTests.Load("dim", "50-settings-general");
        var theme = general.Items.First(i => i.Key == "sld:EFB COLORS THEME");

        Assert.Equal(new[] { "Dark blue", "Light blue", "Light grey", "Dark grey" }, Bbs757EfbElements.OptionsFor(theme, general.PageName));
        Assert.Equal(1, Bbs757EfbElements.CurrentChoice(theme with { Value = "LIGHT BLUE" }, general.PageName, Bbs757EfbElements.OptionsFor(theme, general.PageName)!));
        // The same words on a page that holds no such setting are no setting.
        Assert.Null(Bbs757EfbElements.OptionsFor(theme, "Aircraft, Ground services"));
        // A label that matches no catalog entry (a misread) offers no list, so it is judged by the page alone and never by another row's key.
        Assert.Null(Bbs757EfbElements.OptionsFor(theme with { Text = "EFB colors thme" }, general.PageName));
        // The cabin lights have no choices: a number.
        var cabin = Bbs757EfbScreenTests.Load("bright", "14-aircraft-cabin-doors");
        foreach (var light in cabin.Items.Where(i => i.Kind == Bbs757EfbKind.Slider))
            Assert.Null(Bbs757EfbElements.OptionsFor(light, cabin.PageName));
    }

    [Fact]
    public void A_slider_with_no_choices_or_no_value_read_is_a_number()
    {
        var track = new Bbs757EfbSlider(795, 1100, 1100);
        var brightness = Control(Bbs757EfbKind.Slider, "EFB brightness", "sld:EFB BRIGHTNESS MODE", new Rectangle(273, 215, 277, 30), true,
            value: "100%", slider: track);
        var unread = Control(Bbs757EfbKind.Slider, "IRS alignment time", "sld:IRS ALIGNMENT TIME", new Rectangle(273, 513, 255, 33), true,
            value: "", slider: new Bbs757EfbSlider(895, 1190, 1000));
        var model = new Bbs757EfbScreenModel("SETTINGS", "GENERAL", Array.Empty<Bbs757EfbItem>(), new[] { brightness });
        var aircraft = new Bbs757EfbScreenModel("SETTINGS", "AIRCRAFT", Array.Empty<Bbs757EfbItem>(), new[] { unread });

        var b = Bbs757EfbElements.Build(model, null, NoExtras);
        int index = IndexOf(b, "EFB brightness (100%)");
        Assert.Equal("range", Field(b, index, "type"));
        Assert.Equal("100", Field(b, index, "value"));
        Assert.Equal("0", Field(b, index, "min"));
        Assert.Equal("100", Field(b, index, "max"));
        Assert.Equal("", Field(b, index, "options"));
        AssertPressable(b, "EFB brightness (100%)");

        // The setting has choices, but the reader could not tell which is set: a number from the knob, as before.
        var b2 = Bbs757EfbElements.Build(aircraft, null, NoExtras);
        Assert.Equal("range", Field(b2, IndexOf(b2, "IRS alignment time"), "type"));
        AssertPressable(b2, "IRS alignment time");
    }

    // ---- a name and its value joined ------------------------------------------------------------------------------------

    [Fact]
    public void A_name_ending_in_a_sentences_period_loses_it_before_its_colon()
    {
        // Measured live, signed out of Navigraph: the AIRAC page's row label "Sign in to Navigraph." names its SIGN IN button. The window
        // read "Sign in to Navigraph.: Sign in"; one trailing period goes before the colon.
        var signIn = Control(Bbs757EfbKind.Button, "SIGN IN", "btn:|SIGN IN@50,30", new Rectangle(900, 590, 200, 45), true)
            with { Label = "Sign in to Navigraph." };
        Assert.Equal("Sign in to Navigraph: Sign in", Bbs757EfbElements.NameOf(signIn));

        var row = Control(Bbs757EfbKind.Row, "Status.", "row:status", new Rectangle(300, 200, 200, 30), false, value: "Connected");
        var built = Bbs757EfbElements.Build(new Bbs757EfbScreenModel("SETTINGS", "AIRAC DATA", Array.Empty<Bbs757EfbItem>(), new[] { row }), null, NoExtras);
        AssertText(built, "Status: Connected");
    }

    [Theory]
    [InlineData("Loading..", "Loading..: Sign in")]      // two dots are an ellipsis: kept
    [InlineData("Wait...", "Wait...: Sign in")]
    [InlineData("Latest cycle:", "Latest cycle: Sign in")] // its own colon, as before
    [InlineData("Navigraph", "Navigraph: Sign in")]
    public void Only_one_sentence_period_goes_before_the_colon(string label, string expected)
    {
        var button = Control(Bbs757EfbKind.Button, "SIGN IN", "btn:|SIGN IN@50,30", new Rectangle(900, 590, 200, 45), true) with { Label = label };
        Assert.Equal(expected, Bbs757EfbElements.NameOf(button));
    }

    [Fact]
    public void A_name_with_no_value_keeps_its_period()
    {
        var row = Control(Bbs757EfbKind.Row, "Sign in to Navigraph.", "row:sign-in", new Rectangle(300, 200, 200, 30), false);
        var built = Bbs757EfbElements.Build(new Bbs757EfbScreenModel("SETTINGS", "AIRAC DATA", Array.Empty<Bbs757EfbItem>(), new[] { row }), null, NoExtras);
        AssertText(built, "Sign in to Navigraph.");
    }

    // ---- plan 22, Task 5: the tablet's fields are edit boxes -------------------------------------------------------------
    // No committed tree carries a text box's caret, maximum length and keypad flag (all were captured before plan 22 read them), so these
    // pages are given them as the walk reads them now (Bbs757EfbTreeJson.WithTextState). A maximum length plays no part in what the window
    // shows; the measured ones are used where there is one (measured 2026-11-07: Airport 4, runway length 5, Charts 4, the
    // fuel dialog 8, TOD 3, SimBrief 8, Hoppie 24, SayIntentions 64).

    private static Bbs757EfbElements.Built BuildRead(IReadOnlyList<Bbs757EfbMemory.App> apps) =>
        Bbs757EfbElements.Build(Bbs757EfbModelReader.Read(apps), null, NoExtras);

    /// <summary>An Enter-only edit box (the shared window's opt-in, plan 22 Task 4): type "text", its value the box's own text, its set
    /// sent on Enter alone, pressable, keyed by the box's node name, and a tap on the tablet field it was read from.</summary>
    private static Bbs757EfbItem AssertEditBox(Bbs757EfbElements.Built b, string name, string value, string key)
    {
        int at = Keys(b).IndexOf(key);   // by its key: a row's switch and its box can share the row's name (TOD pause)
        Assert.True(at >= 0, $"No element keyed '{key}': " + string.Join(" | ", Texts(b)));
        Assert.Equal((name, "text", value, "enter", "true"),
            (Field(b, at, "text"), Field(b, at, "type"), Field(b, at, "value"), Field(b, at, "commit"), Field(b, at, "clickable")));
        var action = ActionAt(b, at);
        Assert.NotNull(action);
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, action!.Kind);
        Assert.Equal((Bbs757EfbKind.Field, key), (action.Item!.Kind, action.Item.Key));
        return action.Item;
    }

    /// <summary>A line of text that holds no commit either (a value box, or a text box whose caret was not read).</summary>
    private static void AssertLine(Bbs757EfbElements.Built b, string text)
    {
        AssertText(b, text);
        Assert.Equal("", Field(b, IndexOf(b, text), "commit"));
    }

    /// <summary>The trees with <paramref name="child"/> added as the last child of the control named <paramref name="parent"/>: a control
    /// BlueBird shows only in a state no committed tree was captured in.</summary>
    private static IReadOnlyList<Bbs757EfbMemory.App> WithChild(IReadOnlyList<Bbs757EfbMemory.App> apps, string parent, Bbs757EfbMemory.Node child)
    {
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n)
        {
            var kids = n.Children.Select(Walk).ToList();
            if (n.Name == parent) kids.Add(child);
            return n with { Children = kids };
        }
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    /// <summary>The trees with the control named <paramref name="name"/> replaced by <paramref name="by"/>.</summary>
    private static IReadOnlyList<Bbs757EfbMemory.App> Replaced(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, Bbs757EfbMemory.Node by)
    {
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) => n.Name == name ? by : n with { Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    private static Bbs757EfbMemory.Node TextBox(string name, string text, float x, float y, float w, float h, int maxLength, bool keypad) =>
        new("textbox", name, text, x, y, w, h, 0, Array.Empty<Bbs757EfbMemory.Node>(), Caret: text.Length, MaxLength: maxLength, Keypad: keypad);

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_text_box_is_an_enter_only_edit_box(string culture)
    {
        // PERF calc (25b, the scrub's made-up flight in its boxes) and Charts (23) with their boxes' carets read: each box an edit box
        // holding its own text as the tablet writes it (a decimal point under every culture), named as its line was, typed on the tablet
        // when the pilot presses Enter. The window's label is the name alone: the screen reader says it is an edit box.
        var perf = Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated");
        perf = Bbs757EfbTreeJson.WithTextState(perf, "planning_perf_calc_airport_input", 4, 4, false);
        perf = Bbs757EfbTreeJson.WithTextState(perf, "planning_perf_calc_runway_length_input", 5, 5, false);
        perf = Bbs757EfbTreeJson.WithTextState(perf, "planning_perf_calc_press", 5, 5, false);
        var charts = Bbs757EfbTreeJson.WithTextState(Bbs757EfbTreeJson.Load("23-navigation-charts"), "planning_charts_request_input", 4, 4, false);

        var (b, c) = TestCulture.Run(culture, () => (BuildRead(perf), BuildRead(charts)));

        AssertEditBox(b, "Airport", "KSEA", "planning_perf_calc_airport_input");
        AssertEditBox(b, "Length (FT)", "11900", "planning_perf_calc_runway_length_input");
        AssertEditBox(b, "Pressure (inHg)", "30.01", "planning_perf_calc_press");
        AssertEditBox(c, "Planning charts request", "KIGM", "planning_charts_request_input");
        string listing = Bbs757EfbListing.Of(b);
        Assert.Contains("button  Landing\nedit [KSEA]  Airport\ncombo [RW07L] {RW07L|RW07R|RW08|RW25L|RW25R|RW26}  Runway\n", listing);
        Assert.Contains("edit [11900]  Length (FT)\n", listing);
        Assert.Contains("text  Temperature: 15\nedit [30.01]  Pressure (inHg)\n", listing);   // a box whose caret was not read stays a line
        Assert.Contains("text  KINGMAN, UNITED STATES\nedit [KIGM]  Planning charts request\nbutton  STAR\n", Bbs757EfbListing.Of(c));
    }

    [Fact]
    public void A_value_box_is_a_button_with_its_lines_words()
    {
        // The Fuel page's value boxes (fuel_left_qty_box and its siblings) and Weight & balance's (the passenger weights and the cargo
        // values) are BlueBird's buttons: a tap opens its fuel input dialog (measured 2026-11-07), whose box is the edit box. Each is a
        // button pressing it on the tablet, in the words its line read: the pilot hears the same name and value. No caret: never typed.
        var fuel = BuildRead(Bbs757EfbTreeJson.Load("11-aircraft-fuel"));
        var left = AssertPressable(fuel, "Quantity: 1539 Gal").Item!;
        Assert.Equal((Bbs757EfbKind.Field, "fuel_left_qty_box", true, (Bbs757EfbTextState?)null), (left.Kind, left.Key, left.ValueBox, left.TextState));
        Assert.Equal(("button", "", ""), (Field(fuel, IndexOf(fuel, "Quantity: 1539 Gal"), "kind"), Field(fuel, IndexOf(fuel, "Quantity: 1539 Gal"), "type"),
            Field(fuel, IndexOf(fuel, "Quantity: 1539 Gal"), "commit")));
        AssertPressable(fuel, "Weight: 10308 Lbs");
        AssertPressable(fuel, "Quantity: 3077 Gal");
        Assert.Contains("slider 70  Left (70%)\nslider 0  Center (0%)\nslider 70  Right (70%)\nbutton  Quantity: 1539 Gal\nbutton  Weight: 10308 Lbs\n",
            Bbs757EfbListing.Of(fuel));

        var balance = BuildRead(Bbs757EfbTreeJson.Load("12-aircraft-weight-balance"));
        Assert.Equal("wb_pax_business_weight_box", AssertPressable(balance, "Weight: 2960 Lbs").Item!.Key);
        Assert.Contains("slider 100  Business pax (16/16)\nbutton  Weight: 2960 Lbs\n", Bbs757EfbListing.Of(balance));
    }

    [Fact]
    public void A_value_label_that_is_no_button_stays_a_line()
    {
        // Weight & balance's figures in no button (wb_empty_weight_title_box and the rest are BlueBird's plain boxes) take no tap: lines.
        // So does a text box whose caret was not read (every committed tree's), the fuel input dialog's box read so (its unit after its
        // value, as before), and a value box under the dialog, where the tablet takes no tap on its page.
        var balance = BuildRead(Bbs757EfbTreeJson.Load("12-aircraft-weight-balance"));
        foreach (var line in new[] { "Empty: 136940 Lbs", "ZFW: 176060 Lbs", "Gross weight: 186272 Lbs" })
            AssertLine(balance, line);

        var perf = BuildRead(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"));
        foreach (var line in new[] { "Airport: KSEA", "Takeoff Weight (LB): 180000", "Pressure (inHg): 30.01" })
            AssertLine(perf, line);

        var dialog = BuildRead(Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog"));
        AssertLine(dialog, "Fuel: 752 Gal");
        AssertLine(dialog, "Quantity: 752 Gal");
    }

    [Fact]
    public void A_questions_text_box_is_an_edit_box_holding_its_own_text_with_its_unit_in_its_name()
    {
        // The fuel input dialog's box (11c, pre-filled 752, the keypad's): the edit box holds the box's own text, so the typing compares and
        // reads back what the box holds, and its name carries the unit BlueBird draws beside it, after it in parentheses. The dialog's
        // answers stay its answers (INSERT is the pilot's to press); the page under it stays dimmed text.
        var apps = Bbs757EfbTreeJson.WithTextState(Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog"), "fuel_input_textbox", 3, 8, true);
        var model = Bbs757EfbModelReader.Read(apps)!;

        var b = Bbs757EfbElements.Build(model, null, NoExtras);

        Assert.Same(Assert.Single(model.Question!.Rows), AssertEditBox(b, "Fuel (Gal)", "752", "fuel_input_textbox"));
        Assert.Equal("Fuel (Gal)", Bbs757EfbElements.NameOf(model.Question.Rows[0]));
        Assert.StartsWith("title: Aircraft, Fuel\ntext  Insert desired fuel level (0-2176 Gal)\nedit [752]  Fuel (Gal)\n"
            + "button  Cancel\nbutton  Insert\nheading 2  Left\ntext  Left: 34%\ntext  Quantity: 752 Gal\n", Bbs757EfbListing.Of(b));
    }

    [Fact]
    public void Under_a_question_a_pages_edit_box_is_a_line()
    {
        // The tablet dims its page under a question and takes no tap on it: a page's text box reads as its line until it is answered.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithTextState(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"),
            "planning_perf_calc_airport_input", 4, 4, false))!;
        var asked = page with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Answer("YES") }) };

        AssertEditBox(Bbs757EfbElements.Build(page, null, NoExtras), "Airport", "KSEA", "planning_perf_calc_airport_input");
        AssertLine(Bbs757EfbElements.Build(asked, null, NoExtras), "Airport: KSEA");
    }

    [Fact]
    public void The_saved_simbrief_id_is_a_line_and_logout()
    {
        // As the tablet shows it with an ID saved (55 and 55b, the scrub's made-up 123456): its value line, then the row's LOGOUT, named by
        // the row as the tablet's own label reads (SIMBRIEF ID NUMBER). Its text box is hidden while an ID is saved, so nothing is typed.
        foreach (var name in new[] { "55-settings-airac-data", "55b-settings-airac-data-navigraph-signed-out" })
        {
            var b = BuildRead(Bbs757EfbTreeJson.Load(name));
            AssertLine(b, "SIMBRIEF ID number: 123456");
            AssertPressable(b, "SIMBRIEF ID number: Logout");
            Assert.Equal(IndexOf(b, "SIMBRIEF ID number: 123456") + 1, IndexOf(b, "SIMBRIEF ID number: Logout"));
            Assert.Contains("text  SIMBRIEF ID number: 123456\nbutton  SIMBRIEF ID number: Logout\n", Bbs757EfbListing.Of(b));
        }

        // The Hoppie and SayIntentions boxes read as BlueBird's text box class (the committed trees predate it), each holding the scrub's
        // made-up code with its caret: edit boxes named by their rows, the code shown as the tablet holds it (D2); the value labels beside
        // them stay lines.
        var airac = Bbs757EfbTreeJson.Load("55-settings-airac-data");
        airac = Bbs757EfbTreeJson.WithKind(airac, "hoppie_logon_code_textbox", "textbox", "MADEUP");
        airac = Bbs757EfbTreeJson.WithKind(airac, "sayintentions_acars_key_textbox", "textbox", "MADEUP");
        airac = Bbs757EfbTreeJson.WithTextState(airac, "hoppie_logon_code_textbox", 6, 24, false);
        airac = Bbs757EfbTreeJson.WithTextState(airac, "sayintentions_acars_key_textbox", 6, 64, false);
        var codes = BuildRead(airac);
        AssertEditBox(codes, "Hoppie logon code", "MADEUP", "hoppie_logon_code_textbox");
        AssertEditBox(codes, "Sayintentions ACARS key", "MADEUP", "sayintentions_acars_key_textbox");
        AssertLine(codes, "Hoppie logon code: ------");
        AssertLine(codes, "SIMBRIEF ID number: 123456");

        // After LOGOUT the input cell shows the empty box instead of the saved ID ([763.5,95 170x44], the keypad's, at most 8: measured):
        // an edit box like any other, named by its row. (How the action cell reads then was not captured.)
        var loggedOut = Replaced(Bbs757EfbTreeJson.Load("55-settings-airac-data"), "simbrief_id_saved_label",
            TextBox("simbrief_id_textbox", "", 763.5f, 95, 170, 44, 8, keypad: true));
        AssertEditBox(BuildRead(loggedOut), "SIMBRIEF ID number", "", "simbrief_id_textbox");
    }

    [Fact]
    public void The_TOD_pause_box_shown_while_its_switch_is_on_is_an_edit_box()
    {
        // Settings, Aircraft with TOD pause on (measured 2026-11-07): its switch's byte reads on and tod_pause_textbox shows "40" in the row
        // ([1202,210 100x44], the keypad's, at most 3). The row's box is an edit box after the switch, named by the row's label as the switch
        // is; with the switch off the box is hidden and the row reads as before.
        var off = Bbs757EfbTreeJson.Load("51-settings-aircraft");
        var on = WithChild(Bbs757EfbTreeJson.WithToggleOn(off, "tod_pause_toggle", true), "tod_pause_setting_grid",
            TextBox("tod_pause_textbox", "40", 1202, 210, 100, 44, 3, keypad: true));

        var b = BuildRead(on);

        AssertEditBox(b, "TOD pause distance (NM)", "40", "tod_pause_textbox");
        Assert.Contains("checkbox checked  TOD pause distance (NM)\nedit [40]  TOD pause distance (NM)\n", Bbs757EfbListing.Of(b));
        Assert.Contains("checkbox not checked  TOD pause distance (NM)\ncombo [Disabled]", Bbs757EfbListing.Of(BuildRead(off)));
    }

    // ---- the same in any culture ----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_elements_are_the_same_in_any_Windows_culture(string culture)
    {
        var pages = new[]
        {
            Bbs757EfbScreenTests.Load("bright", "14-aircraft-cabin-doors"),    // sliders as numbers
            Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft"),
            Bbs757EfbScreenTests.Load("dim", "50-settings-general"),
            Bbs757EfbScreenTests.Load("bright", "21-defuel-question"),
            Bbs757EfbScreenTests.Load("bright", "19-notification-list"),
            Bbs757EfbScreenTests.Load("bright", "55-settings-airac-data"),   // buttons named by their rows
        };
        var extras = new[] { new Bbs757EfbExtraElement("extra:code", "Navigraph code: ABCD-1234", false) };

        foreach (var page in pages)
        {
            var expected = TestCulture.Run("en-US", () => Bbs757EfbElements.Build(page, "Alert", extras).Data);
            var actual = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, "Alert", extras).Data);
            Assert.Equal(expected, actual);
        }
        Assert.Contains(pages, page => Bbs757EfbElements.Build(page, null, NoExtras).Data.Values.Contains("range"));
    }
}

public class Bbs757EfbNavigationCheckTests
{
    private static Bbs757EfbScreenModel Page(string app, string section) =>
        new(app, section, new List<Bbs757EfbItem>(), new List<Bbs757EfbItem>());

    [Theory]
    [InlineData("AIRCRAFT", "", "Aircraft", true)]                       // an app tapped on Home: its first section shows
    [InlineData("AIRCRAFT", "FUEL", "Aircraft", true)]
    [InlineData("AIRCRAFT", "FUEL", "Fuel", true)]                       // a section tapped in the menu
    [InlineData("HOME", "", "Home", true)]
    [InlineData("HOME", "", "Aircraft", false)]                          // still the old page: read again
    [InlineData("AIRCRAFT", "FUEL", "Fuel (current page)", true)]
    [InlineData("AIRCRAFT", "GROUND SERVICES", "Fuel", false)]
    public void A_navigation_tap_is_judged_by_the_page_name(string app, string section, string tapped, bool landed)
    {
        Assert.Equal(landed, Bbs757EfbPressRunner.PageIs(Page(app, section), tapped));
    }
}
