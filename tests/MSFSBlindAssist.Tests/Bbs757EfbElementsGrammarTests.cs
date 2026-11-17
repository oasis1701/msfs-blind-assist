using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The tablet window's order and wording (plan 20, Task 8): content first, then the status bar, "Sections" with the side menu and
/// Home last; names in sentence case, values as the tablet writes them; no buttons of the old screen reader.
/// </summary>
public class Bbs757EfbElementsGrammarTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static readonly IReadOnlyList<Bbs757EfbExtraElement> NoExtras = Array.Empty<Bbs757EfbExtraElement>();

    private static Bbs757EfbItem Make(Bbs757EfbKind kind, string text, string key, string value = "", string label = "", int level = 0,
        bool? on = null, bool selected = false) =>
        new(kind, text, new Rectangle(300, 200, 120, 30), new Point(360, 215),
            kind is not (Bbs757EfbKind.Text or Bbs757EfbKind.Heading or Bbs757EfbKind.Row or Bbs757EfbKind.LongText), key,
            Bbs757EfbTint.None, on, selected, value, null, level, label);

    private static Bbs757EfbScreenModel Page(string app, string section, IReadOnlyList<Bbs757EfbItem> items) =>
        new(app, section, new[] { Make(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", selected: true), Make(Bbs757EfbKind.Menu, "Maintenance", "menu:MAINTENANCE") }, items);

    private static string F(Bbs757EfbElements.Built b, int i, string field) => b.Data.GetValueOrDefault($"items.{i}.{field}", "");
    private static List<string> Texts(Bbs757EfbElements.Built b) => Enumerable.Range(0, b.Count).Select(i => F(b, i, "text")).ToList();
    private static List<string> Keys(Bbs757EfbElements.Built b) => Enumerable.Range(0, b.Count).Select(i => F(b, i, "key")).ToList();
    private static int IndexOf(Bbs757EfbElements.Built b, string text)
    {
        int index = Texts(b).IndexOf(text);
        Assert.True(index >= 0, $"No element '{text}': " + string.Join(" | ", Texts(b)));
        return index;
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Content_comes_first_then_sections_then_home(string culture)
    {
        var page = Page("AIRCRAFT", "FUEL", new[] { Make(Bbs757EfbKind.Heading, "Tanks", "h:tanks"), Make(Bbs757EfbKind.Row, "Center", "row:c", "14%") }) with
        {
            TopBar = new[] { Make(Bbs757EfbKind.Button, "Notifications", Bbs757EfbTopBar.NotificationsKey) },
            Notices = new[] { new Bbs757EfbNotice(1, "GPU connected.", "14:17Z") },
        };
        var extras = new[] { new Bbs757EfbExtraElement("extra:code", "Navigraph code: ABCD-1234", false) };

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, "Alert", extras));

        Assert.Equal(new[]
        {
            "Alert", "Tanks", "Center: 14%", "Navigraph code: ABCD-1234", "Status bar", "Notifications", "Notifications", "14:17Z GPU connected.",
            "Sections", "Fuel (current page)", "Maintenance", "Home",
        }, Texts(b));
        Assert.Equal(new[] { "h:status", "h:notifications", "h:sections" },
            Keys(b).Where(k => k.StartsWith("h:s", StringComparison.Ordinal) || k == "h:notifications"));
        Assert.Equal("2", F(b, 4, "level"));
        Assert.Equal("2", F(b, 6, "level"));
        Assert.Equal("2", F(b, 8, "level"));
        Assert.Equal("button", F(b, 5, "kind"));
    }

    [Fact]
    public void Home_is_last_and_absent_on_the_home_screen()
    {
        var aircraft = Bbs757EfbElements.Build(Page("AIRCRAFT", "FUEL", new[] { Make(Bbs757EfbKind.Text, "Hello", "t:1") }), null, NoExtras);
        Assert.Equal("btn:HOME", Keys(aircraft)[^1]);
        Assert.Equal("button", F(aircraft, aircraft.Count - 1, "kind"));
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, aircraft.Actions[int.Parse(F(aircraft, aircraft.Count - 1, "aidx"))].Kind);

        var home = Bbs757EfbElements.Build(new Bbs757EfbScreenModel("HOME", "", Array.Empty<Bbs757EfbItem>(),
            new[] { Make(Bbs757EfbKind.Button, "Aircraft", "app:AIRCRAFT") }), null, NoExtras);
        Assert.DoesNotContain("btn:HOME", Keys(home));
        Assert.DoesNotContain("h:sections", Keys(home));
    }

    [Fact]
    public void No_read_again_more_or_scroll_elements()
    {
        Assert.Equal(new[] { "Tap", "Extra" }, Enum.GetNames<Bbs757EfbElements.ActionKind>());
        var page = Page("SETTINGS", "CALLOUTS", new[] { Make(Bbs757EfbKind.Toggle, "80 KTS", "tgl:80", on: true) }) with { Scrollable = true };
        foreach (var model in new Bbs757EfbScreenModel?[] { page, null })
        {
            var texts = Texts(Bbs757EfbElements.Build(model, "Alert", NoExtras));
            Assert.DoesNotContain(texts, t => t is "More" or "Read the tablet again" or "Scroll down" or "Scroll up");
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Names_are_sentence_case_and_values_as_written(string culture)
    {
        var page = Page("AIRCRAFT", "GROUND SERVICES", new[]
        {
            Make(Bbs757EfbKind.Row, "AIRPORT", "row:a", "SEATTLE-TACOMA INTL"),
            Make(Bbs757EfbKind.Toggle, "LOCK KEYBOARD TO EFB", "tgl:lock", on: false),
            Make(Bbs757EfbKind.Button, "GPU CONNECTED", "btn:gpu"),
            Make(Bbs757EfbKind.Heading, "GROUND POWER", "h:gp"),
            Make(Bbs757EfbKind.Field, "QUANTITY", "fld:q", "1539 Gal"),
        });

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, null, NoExtras));

        Assert.Contains("Airport: SEATTLE-TACOMA INTL", Texts(b));
        Assert.Equal("checkbox", F(b, IndexOf(b, "Lock keyboard to EFB"), "type"));
        Assert.Contains("GPU connected", Texts(b));
        Assert.Contains("Ground power", Texts(b));
        Assert.Contains("Quantity: 1539 Gal", Texts(b));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_labelled_button_reads_label_then_words(string culture)
    {
        var page = Page("AIRCRAFT", "CABIN & DOORS", new[]
        {
            Make(Bbs757EfbKind.Button, "OPEN", "btn:nose", label: "NOSE DOOR"),
            Make(Bbs757EfbKind.Button, "Closed", "btn:lf", label: "LEFT FWD"),
        });

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, null, NoExtras));

        Assert.Contains("Nose door: Open", Texts(b));
        Assert.Contains("Left FWD: Closed", Texts(b));   // FWD is an abbreviation Pretty keeps
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_two_choice_switch_is_a_choice_list(string culture)
    {
        var page = Page("SETTINGS", "AIRCRAFT", new[] { Make(Bbs757EfbKind.Toggle, "Nosewheel steering", "tgl:nws", value: "Rudder", on: false) });

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, null, NoExtras));

        int at = IndexOf(b, "Nosewheel steering");
        Assert.Equal("select", F(b, at, "type"));
        Assert.Equal("Rudder", F(b, at, "value"));
        Assert.Equal(new[] { "Realistic", "Rudder" }, F(b, at, "options").Split((char)0x1f));
        Assert.Equal("true", F(b, at, "clickable"));
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, b.Actions[int.Parse(F(b, at, "aidx"))].Kind);

        // An unread word, and an Off/On switch, stay check boxes.
        var unread = Bbs757EfbElements.Build(Page("SETTINGS", "AIRCRAFT", new[]
        {
            Make(Bbs757EfbKind.Toggle, "Nosewheel steering", "tgl:nws"), Make(Bbs757EfbKind.Toggle, "Sync barometers", "tgl:sb", on: true),
        }), null, NoExtras);
        Assert.Equal("checkbox", F(unread, IndexOf(unread, "Nosewheel steering"), "type"));
        Assert.Equal("checkbox", F(unread, IndexOf(unread, "Sync barometers"), "type"));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_current_tab_says_current(string culture)
    {
        var page = Page("AIRCRAFT", "FAILURES", new[]
        {
            Make(Bbs757EfbKind.Tab, "Catalog", "tab:|CATALOG", selected: true), Make(Bbs757EfbKind.Tab, "Random", "tab:|RANDOM"),
        });

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, null, NoExtras));

        int current = IndexOf(b, "Catalog (current)");
        Assert.Equal("button", F(b, current, "kind"));
        Assert.Equal("button", F(b, IndexOf(b, "Random"), "kind"));
        Assert.Equal("true", F(b, IndexOf(b, "Random"), "clickable"));
    }

    [Fact]
    public void A_label_ending_in_a_colon_gets_no_second()
    {
        var page = Page("AIRCRAFT", "FUEL", new[]
        {
            Make(Bbs757EfbKind.Row, "AIRPORT:", "row:a", "KSEA"), Make(Bbs757EfbKind.Row, "RUNWAY", "row:r", "16L"),
            Make(Bbs757EfbKind.Button, "ON", "btn:x", label: "GPU:"),
        });
        var texts = Texts(Bbs757EfbElements.Build(page, null, NoExtras));

        Assert.Contains("Airport: KSEA", texts);
        Assert.Contains("Runway: 16L", texts);
        Assert.Contains("GPU: On", texts);
    }

    private static readonly Bbs757EfbQuestion Defuel = new("Are you sure you want to defuel?",
        new[] { Make(Bbs757EfbKind.Button, "YES", "q:YES"), Make(Bbs757EfbKind.Button, "NO", "q:NO") });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_text_box_is_a_line_with_what_it_holds(string culture)
    {
        // A text box whose caret was not read (no TextState: a torn read, or a tree captured before plan 22) is a line, its name and what
        // it holds as the tablet writes it (a decimal point under every culture), as a value box is; an empty one is its name alone. No
        // edit box, since the typing needs the caret; nothing to press. One whose caret was read is an edit box (Bbs757EfbElementsTests).
        var page = Page("NAVIGATION", "PERF CALC", new[]
        {
            Make(Bbs757EfbKind.Field, "Pressure (inHg)", "planning_perf_calc_press", "30.01"), Make(Bbs757EfbKind.Field, "Wind", "planning_perf_calc_wind"),
        });

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, null, NoExtras));

        foreach (string line in new[] { "Pressure (inHg): 30.01", "Wind" })
        {
            int at = IndexOf(b, line);
            Assert.Equal(("", "false"), (F(b, at, "type"), F(b, at, "clickable")));
            Assert.False(b.Actions.ContainsKey(int.Parse(F(b, at, "aidx"))));
        }
        Assert.Contains("text  Pressure (inHg): 30.01\ntext  Wind\n", Bbs757EfbListing.Of(b));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_drop_down_is_a_choice_list_of_its_entries(string culture)
    {
        // Plan 21: a drop-down is a choice list of its entries, valued by the one it shows; one that shows none of them (Systems'
        // SELECT) starts its list with an empty choice, so no entry reads as chosen. Choosing from it is Task 7's.
        var runway = Make(Bbs757EfbKind.Choice, "Runway", "planning_perf_calc_runway_dropdown", "RW07L") with { Entries = new[] { "RW07L", "RW07R", "RW26" } };
        var menu = Make(Bbs757EfbKind.Choice, "ELECTRICAL", "pb_menu1") with { Entries = new[] { "24 - ELECTRICAL", "28 - FUEL" } };
        var page = Page("NAVIGATION", "PERF CALC", new[] { runway, menu });

        var b = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, null, NoExtras));

        int at = IndexOf(b, "Runway");
        Assert.Equal(("select", "RW07L", "true", "planning_perf_calc_runway_dropdown"), (F(b, at, "type"), F(b, at, "value"), F(b, at, "clickable"), F(b, at, "key")));
        Assert.Equal(new[] { "RW07L", "RW07R", "RW26" }, F(b, at, "options").Split((char)0x1f));
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, b.Actions[int.Parse(F(b, at, "aidx"))].Kind);
        int none = IndexOf(b, "Electrical");
        Assert.Equal(("select", ""), (F(b, none, "type"), F(b, none, "value")));
        Assert.Equal(new[] { "", "24 - ELECTRICAL", "28 - FUEL" }, F(b, none, "options").Split((char)0x1f));
        Assert.Contains("combo [RW07L] {RW07L|RW07R|RW26}  Runway\ncombo [] {|24 - ELECTRICAL|28 - FUEL}  Electrical\n", Bbs757EfbListing.Of(b));

        // Behind a question: a line with the entry it shows, or its name alone.
        var dimmed = Texts(TestCulture.Run(culture, () => Bbs757EfbElements.Build(page with { Question = Defuel }, null, NoExtras)));
        Assert.Contains("Runway: RW07L", dimmed);
        Assert.Contains("Electrical", dimmed);
    }

    [Fact]
    public void Long_text_is_a_pre_block()
    {
        const string route = "KSEA SEA5 KENMO\nJ523 KPAE";
        var b = Bbs757EfbElements.Build(Page("NAVIGATION", "OPERATING FP", new[] { Make(Bbs757EfbKind.LongText, route, "long:r") }), null, NoExtras);

        int at = IndexOf(b, route);
        Assert.Equal("pre", F(b, at, "type"));
        Assert.Equal("false", F(b, at, "clickable"));
    }

    [Fact]
    public void Set_up_and_up_reference_are_sentence_case()
    {
        Assert.Equal("Set up", Bbs757EfbVocabulary.Pretty("SET UP"));
        Assert.Equal("EHSI up reference", Bbs757EfbVocabulary.Pretty("EHSI UP REFERENCE"));
    }

    [Fact]
    public void NotShownText_points_at_F5()
    {
        Assert.Equal("The tablet did not show Fuel. Try again, or press F5 to read the tablet again.", Bbs757EfbPressRunner.NotShownText("Fuel"));
    }
}
