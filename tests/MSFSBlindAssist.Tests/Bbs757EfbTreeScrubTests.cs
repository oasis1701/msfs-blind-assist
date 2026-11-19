using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757EfbTreeScrubTests
{
    private static Bbs757EfbMemory.Node N(string name, string text, params Bbs757EfbMemory.Node[] kids) =>
        new("label", name, text, 1.5f, 2.25f, 30, 40, 0x1234, kids);

    private static IReadOnlyList<Bbs757EfbMemory.App> App(params Bbs757EfbMemory.Node[] roots) =>
        new[] { new Bbs757EfbMemory.App(3, "SETTINGS", roots) };

    [Fact]
    public void The_saved_simbrief_id_becomes_the_made_up_one()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("simbrief_id_saved_label", "987654")));
        Assert.Equal("123456", scrubbed[0].Controls[0].Text);
    }

    [Fact]
    public void The_acars_codes_become_made_up_ones()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(
            N("hoppie_logon_code_value_label", "abc"), N("sayintentions_acars_key_value_label", "xyz"),
            N("hoppie_logon_code_textbox", "secret"), N("hoppie_logon_code_value_label", "------"), N("sayintentions_acars_key_textbox", "------")));
        Assert.Equal(new[] { "MADEUP", "MADEUP", "MADEUP", "------", "------" },
            scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void A_help_text_box_is_kept()
    {
        // B757-24 (plan 21, Task 5): the credential boxes are scrubbed by their exact names. The `_textbox` suffix rule plan 20 had made
        // up Settings > Livery options' VNAV takeoff help text too (fixture 52 read MADEUP from plan 20's capture until plan 21 scrubbed
        // the credential boxes by exact name and recaptured it); a text box holding no credential keeps its words, and so does any other
        // `*_textbox`.
        const string help = "Takeoff VNAV behavior: ARMED engages VNAV at 400 ft.";
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("vnav_takeoff_help_textbox", help), Box("fuel_input_textbox", "1200"),
            N("hoppie_textbox", "words")));
        Assert.Equal(new[] { help, "1200", "words" }, scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void The_flight_plan_becomes_the_made_up_flight()
    {
        var expected = new (string Name, string Made)[]
        {
            ("planning_acars_flight_info_value_0", "MSF1"), ("planning_acars_flight_info_value_1", "MSF1"),
            ("planning_acars_flight_info_value_2", "KSEA / SEA"), ("planning_acars_flight_info_value_3", "KPDX / PDX"),
            ("planning_acars_flight_info_value_4", "0"), ("planning_acars_flight_info_value_5", "0"),
            ("planning_acars_flight_info_value_6", "01 Jan 26"), ("planning_acars_flight_info_value_7", "12:00 UTC"),
            ("planning_acars_flight_info_value_8", "12:00 UTC"), ("planning_acars_flight_info_value_9", "0"),
            ("planning_acars_flight_info_value_10", "0"), ("planning_acars_flight_info_value_11", "0"),
            ("planning_acars_departure_value_0", "SEATTLE-TACOMA INTL"), ("planning_acars_departure_value_1", "16L"),
            ("planning_acars_departure_value_2", "BTG1"),
            ("planning_acars_arrival_value_0", "PORTLAND INTL"), ("planning_acars_arrival_value_1", "28R"),
            ("planning_acars_arrival_value_2", "BTG2"),
            ("planning_acars_flight_plan_value_0", "0"), ("planning_acars_flight_plan_value_11", "0"),
            ("planning_acars_load_sheet_value_0", "0"), ("planning_acars_load_sheet_value_11", "0"),
            ("planning_acars_route_value", "KSEA/16L DCT BTG DCT KPDX/28R"),
            ("planning_operating_fp_value", Bbs757EfbTreeScrub.MadeUpOperatingFp),
        };
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(expected.Select(e => N(e.Name, "REAL VALUE")).ToArray()));
        Assert.Equal(expected.Select(e => e.Made), scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void An_empty_flight_plan_value_stays_empty()
    {
        // The final review's M5: an empty value became "0", so Operating FP's fixture read "pre 0", which the tablet never shows.
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("planning_operating_fp_value", ""), N("planning_acars_flight_info_value_4", ""),
            N("planning_acars_route_value", "")));
        Assert.Equal(new[] { "", "", "" }, scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void The_committed_operating_fp_tree_holds_no_flight_plan_text()
    {
        // Recaptured 2026-11-07 through the scrub: the Operating FP's text box holds the made-up plan alone.
        var value = Bbs757EfbTreeJson.Load("21-navigation-operating-fp").SelectMany(a => a.Controls).SelectMany(Flat)
            .Single(n => n.Name == "planning_operating_fp_value");
        Assert.Equal(("multiline", Bbs757EfbTreeScrub.MadeUpOperatingFp), (value.Kind, value.Text));

        static IEnumerable<Bbs757EfbMemory.Node> Flat(Bbs757EfbMemory.Node n) => new[] { n }.Concat(n.Children.SelectMany(Flat));
    }

    [Fact]
    public void The_airac_text_boxes_become_made_up_ones()
    {
        // The AIRAC page's three text boxes (plan 21 reads a text box's text): the SimBrief ID as the made-up ID, the codes as MADEUP.
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(Box("simbrief_id_textbox", "987654"), Box("hoppie_logon_code_textbox", "abc"),
            Box("sayintentions_acars_key_textbox", "xyz"), Box("simbrief_id_textbox", ""), Box("hoppie_logon_code_textbox", "")));
        Assert.Equal(new[] { "123456", "MADEUP", "MADEUP", "", "" }, scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void The_operating_fp_becomes_a_made_up_plan()
    {
        // The whole Operating FP, read as one multi-line text: a few made-up lines of the made-up flight in its place.
        var fp = new Bbs757EfbMemory.Node("multiline", "planning_operating_fp_value", "REAL PLAN\nLINE 2\nLINE 3", 323.5f, 120, 700, 5000, 0,
            Array.Empty<Bbs757EfbMemory.Node>());
        string made = Bbs757EfbTreeScrub.Scrub(App(fp))[0].Controls[0].Text;
        Assert.Equal(Bbs757EfbTreeScrub.MadeUpOperatingFp, made);
        Assert.True(made.Split('\n').Length is >= 3 and <= 10, made);
        Assert.Contains("KSEA", made);
        Assert.Contains("KPDX", made);
    }

    [Fact]
    public void The_perf_calc_text_boxes_become_made_up_ones()
    {
        // PERF calc's six text boxes (KPHX, 10300, 207322, 260/07, 23 and 29.92 when measured) take the made-up flight's airport and
        // made-up numbers; a PERF calc text box not in the table reads 0; an empty one stays empty; its labels are kept.
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(
            Box("planning_perf_calc_airport_input", "KPHX"), Box("planning_perf_calc_runway_length_input", "10300"),
            Box("planning_perf_calc_weight", "207322"), Box("planning_perf_calc_wind", "260/07"), Box("planning_perf_calc_temp", "23"),
            Box("planning_perf_calc_press", "29.92"), Box("planning_perf_calc_new_input", "77"), Box("planning_perf_calc_weight", ""),
            N("planning_perf_calc_airport_title", "Airport")));
        Assert.Equal(new[] { "KSEA", "11900", "180000", "160/05", "15", "30.01", "0", "", "Airport" },
            scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void The_scrub_reaches_a_drop_downs_rows()
    {
        var dropDown = new Bbs757EfbMemory.Node("pulldown", "some_dropdown", "A", 0, 0, 100, 34, 0, Array.Empty<Bbs757EfbMemory.Node>(),
            Value: "A", Entries: new[] { "A" }, Open: true, Rows: new[] { N("simbrief_id_saved_label", "987654") });
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(dropDown))[0].Controls[0];
        Assert.Equal("123456", Assert.Single(scrubbed.Rows!).Text);
        Assert.Equal(("A", "A"), (scrubbed.Text, scrubbed.Value));
    }

    private static Bbs757EfbMemory.Node Box(string name, string text) =>
        new("textbox", name, text, 1.5f, 2.25f, 30, 40, 0x1234, Array.Empty<Bbs757EfbMemory.Node>());

    [Fact]
    public void A_made_up_text_boxs_caret_is_at_its_end()
    {
        // Plan 22 reads a text box's caret. Where the scrub makes a credential up, the real caret would tell where the real text ends
        // (its length) and could lie past the made-up text's end, which no caret does: it is put at the made-up text's end, where typing
        // leaves it. A made-up flight value keeps its caret (no length of a credential to tell), cut to the made-up text's end; a text the
        // scrub keeps keeps its caret. The maximum length and keypad flag are kept either way.
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(
            Box("hoppie_logon_code_textbox", "REALCODE1234") with { Caret = 12, MaxLength = 24, Keypad = false },
            Box("planning_perf_calc_airport_input", "KPHX") with { Caret = 1, MaxLength = 4, Keypad = false },
            Box("planning_charts_request_input", "EDDF") with { Caret = 2, MaxLength = 4, Keypad = false },
            Box("simbrief_id_textbox", "") with { Caret = 0, MaxLength = 8, Keypad = true },
            Box("hoppie_logon_code_textbox", "REALCODE") with { MaxLength = 24 },
            Box("planning_perf_calc_weight", "1234567") with { Caret = 7, MaxLength = 8, Keypad = true },
            Box("planning_perf_calc_temp", "-12") with { Caret = 1, MaxLength = 8, Keypad = true }))[0].Controls;
        Assert.Equal(new (string, int?)[] { ("MADEUP", 6), ("KSEA", 1), ("EDDF", 2), ("", 0), ("MADEUP", null), ("180000", 6), ("15", 1) },
            scrubbed.Select(c => (c.Text, c.Caret)));
        Assert.Equal(new int?[] { 24, 4, 4, 8, 24, 8, 8 }, scrubbed.Select(c => c.MaxLength));
        Assert.Equal(new bool?[] { false, false, false, true, null, true, true }, scrubbed.Select(c => c.Keypad));
    }

    [Fact]
    public void An_update_or_other_name_containing_date_or_time_is_kept()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("update_label", "UPDATE"), N("planning_time_label", "Time")));
        Assert.Equal(new[] { "UPDATE", "Time" }, scrubbed[0].Controls.Select(c => c.Text));
    }

    [Fact]
    public void An_empty_textbox_stays_empty()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("hoppie_logon_code_textbox", "")));
        Assert.Equal("", scrubbed[0].Controls[0].Text);
    }

    [Fact]
    public void Every_other_text_is_kept()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("title", "Settings", N("child_label", "Keep me"))));
        Assert.Equal("Settings", scrubbed[0].Controls[0].Text);
        Assert.Equal("Keep me", scrubbed[0].Controls[0].Children[0].Text);
    }

    [Fact]
    public void A_nested_credential_is_scrubbed()
    {
        var scrubbed = Bbs757EfbTreeScrub.Scrub(App(N("box", "", N("simbrief_id_saved_label", "999"))));
        Assert.Equal("123456", scrubbed[0].Controls[0].Children[0].Text);
    }

    [Fact]
    public void The_json_round_trips_without_the_address()
    {
        var apps = App(N("a", "x", N("b", "y")));
        var back = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(apps));
        Assert.Equal(3, back[0].Key);
        Assert.Equal("SETTINGS", back[0].Class);
        var a = back[0].Controls[0];
        Assert.Equal(("label", "a", "x", 1.5f, 2.25f, 30f, 40f, 0u), (a.Kind, a.Name, a.Text, a.X, a.Y, a.W, a.H, a.Address));
        Assert.Equal("y", a.Children[0].Text);
    }

    [Fact]
    public void A_sliders_knob_round_trips_and_the_scrub_keeps_it()
    {
        var slider = new Bbs757EfbMemory.Node("slider", "wb_baggage_fwd_slider", "", 1162.5f, 462, 188, 18, 0x1234,
            Array.Empty<Bbs757EfbMemory.Node>(), Knob: 0.598f, KnobSpan: 12);
        foreach (var culture in new[] { "en-US", "de-DE", "sv-SE" })
        {
            string json = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Write(Bbs757EfbTreeScrub.Scrub(App(slider))));
            Assert.Contains("\"knob\": 0.598", json);
            var back = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Read(json))[0].Controls[0];
            Assert.Equal((0.598f, 12f), (back.Knob, back.KnobSpan));
        }
    }

    [Fact]
    public void A_node_without_a_knob_writes_none_and_a_tree_without_knobs_reads_none()
    {
        // The knob fields are optional, so a tree captured before the layout had the slider knob still loads (as one without knobs).
        string json = Bbs757EfbTreeJson.Write(App(N("a", "x")));
        Assert.DoesNotContain("knob", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Bbs757EfbTreeJson.Read(json)[0].Controls[0].Knob);

        const string slider = "[{\"key\": 3, \"class\": \"AIRCRAFT\", \"controls\": [{\"kind\": \"slider\", \"name\": \"wb_baggage_fwd_slider\", "
            + "\"text\": \"\", \"x\": 1162.5, \"y\": 462, \"w\": 188, \"h\": 18, \"children\": []}]}]";
        var read = Bbs757EfbTreeJson.Read(slider)[0].Controls[0];
        Assert.Equal("slider", read.Kind);
        Assert.Null(read.Knob);
        Assert.Equal(0f, read.KnobSpan);
    }

    [Fact]
    public void The_phase_2_fields_round_trip()
    {
        // Plan 21's node fields: a button's selected state, a drop-down's value, entries, open state and rows (drawn rectangles), a text
        // box's text; each written only when set, so a node without them writes as before.
        var row = new Bbs757EfbMemory.Node("button", "item_btn_0", "RW07L", 519.1f, 151, 150, 38, 0, Array.Empty<Bbs757EfbMemory.Node>(),
            Selected: false);
        var list = new Bbs757EfbMemory.Node("scrollbox", "item_scrollbox", "", 519.1f, 189, 150, 76, 0, Array.Empty<Bbs757EfbMemory.Node>(),
            Scroll: 38);
        var dropDown = new Bbs757EfbMemory.Node("pulldown", "planning_perf_calc_runway_dropdown", "RW07L", 519.1f, 156, 150, 34, 0x99,
            Array.Empty<Bbs757EfbMemory.Node>(), Value: "RW07L", Entries: new[] { "RW07L", "RW26" }, Open: true, Rows: new[] { row },
            OpenList: list);
        var tab = new Bbs757EfbMemory.Node("button", "planning_perf_calc_tab_button_0", "TAKE OFF", 300, 100, 120, 40, 0,
            Array.Empty<Bbs757EfbMemory.Node>(), Selected: true);
        var closed = new Bbs757EfbMemory.Node("pulldown", "planning_perf_calc_flap_dropdown", "SELECT", 0, 0, 150, 34, 0,
            Array.Empty<Bbs757EfbMemory.Node>(), Entries: Array.Empty<string>());
        foreach (var culture in new[] { "en-US", "sv-SE" })
        {
            string json = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Write(App(dropDown, tab, closed, N("plain", "x"))));
            var back = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Read(json))[0].Controls;

            Assert.Equal(("RW07L", true), (back[0].Value, back[0].Open));
            Assert.Equal(new[] { "RW07L", "RW26" }, back[0].Entries);
            var r = Assert.Single(back[0].Rows!);
            Assert.Equal(("item_btn_0", "RW07L", 519.1f, 151f, 150f, 38f, (bool?)false), (r.Name, r.Text, r.X, r.Y, r.W, r.H, r.Selected));
            var l = back[0].OpenList!;
            Assert.Equal(("scrollbox", "item_scrollbox", 519.1f, 189f, 150f, 76f, 38f), (l.Kind, l.Name, l.X, l.Y, l.W, l.H, l.Scroll));
            Assert.True(back[1].Selected);
            Assert.Equal(("", false, 0), (back[2].Value, back[2].Open, back[2].Entries!.Count));
            Assert.Null(back[2].Rows);
            Assert.Null(back[2].OpenList);
            var plain = back[3];
            Assert.Equal(((bool?)null, "", false), (plain.Selected, plain.Value, plain.Open));
            Assert.Null(plain.Entries);
            Assert.Null(plain.Rows);
            Assert.Equal(0f, plain.Scroll);
        }

        // A node without them writes none of their names.
        string bare = Bbs757EfbTreeJson.Write(App(N("a", "x")));
        foreach (string field in new[] { "selected", "value", "entries", "open", "rows", "list", "scroll" })
            Assert.DoesNotContain($"\"{field}\"", bare);
    }

    [Fact]
    public void The_notices_pass_the_scrub_as_read()
    {
        // BlueBird's notifications are event texts and Zulu times: no credential or flight plan field was measured in one.
        var notices = new[]
        {
            new Bbs757EfbNoticeRecord(45, "", "CHOCKS REMOVED.", "21:37Z", false),
            new Bbs757EfbNoticeRecord(52, "AUTOBRAKE 1", "TARGET 4.5 | ACTUAL 4.5 ft/s2", "03:20Z", true),
        };
        Assert.Equal(notices, Bbs757EfbTreeScrub.Scrub(notices));
    }

    [Fact]
    public void The_notices_round_trip_and_a_tree_without_them_reads_none()
    {
        var notices = new[]
        {
            new Bbs757EfbNoticeRecord(50, "", "AFT CARGO DOOR OPENED.", "03:15Z", true),
            new Bbs757EfbNoticeRecord(52, "AUTOBRAKE 1", "TARGET 4.5 | ACTUAL 4.5 ft/s2", "03:20Z", false),
        };
        string json = Bbs757EfbTreeJson.Write(App(N("a", "x")), notices);
        Assert.StartsWith("{", json);
        Assert.Equal(notices, Bbs757EfbTreeJson.ReadNotices(json));
        Assert.Equal("x", Bbs757EfbTreeJson.Read(json)[0].Controls[0].Text);

        // A tree written without notices is the array it always was, and reads none.
        string plain = Bbs757EfbTreeJson.Write(App(N("a", "x")));
        Assert.StartsWith("[", plain);
        Assert.Empty(Bbs757EfbTreeJson.ReadNotices(plain));
        Assert.Empty(Bbs757EfbTreeJson.LoadNotices("11-aircraft-fuel"));            // captured before plan 21: a root array
        Assert.NotEmpty(Bbs757EfbTreeJson.LoadNotices("13-aircraft-ground-services")); // recaptured with BlueBird's notifications
        Assert.Empty(Bbs757EfbTreeJson.ReadNotices(Bbs757EfbTreeJson.Write(App(N("a", "x")), Array.Empty<Bbs757EfbNoticeRecord>())));
    }

    [Fact]
    public void The_recaptured_weight_and_balance_tree_carries_its_knobs()
    {
        // Captured live 2026-11-07 with the slider knob in the layout: forward cargo at 59.8 %, its knob 12 px wide.
        var apps = Bbs757EfbTreeJson.Load("12-aircraft-weight-balance");
        var cargo = AllNodes(apps).Single(n => n.Name == "wb_baggage_fwd_slider");
        Assert.Equal(0.598f, cargo.Knob!.Value, 3);
        Assert.Equal(12f, cargo.KnobSpan);

        // WithoutKnobs gives the same tree as a layout without the slider knob reads it.
        var bare = AllNodes(Bbs757EfbTreeJson.WithoutKnobs(apps)).ToList();
        Assert.All(bare, n => { Assert.Null(n.Knob); Assert.Equal(0f, n.KnobSpan); });
        Assert.Equal(AllNodes(apps).Count(), bare.Count);
    }

    private static IEnumerable<Bbs757EfbMemory.Node> AllNodes(IReadOnlyList<Bbs757EfbMemory.App> apps)
    {
        static IEnumerable<Bbs757EfbMemory.Node> Flat(Bbs757EfbMemory.Node n) => new[] { n }.Concat(n.Children.SelectMany(Flat));
        return apps.SelectMany(a => a.Controls).SelectMany(Flat);
    }

    [Fact]
    public void Every_committed_tree_writes_back_as_it_is()
    {
        // A tree's optional fields are written only when the walk read them (plan 22's keyboard state and a text box's caret among
        // them), so every committed tree, captured before they were read, reads and writes back to its own text: adding a field leaves
        // the committed fixtures as they are. Line ends aside: git may check a tree out with CRLF.
        var names = Bbs757EfbTreeJson.CommittedNames();
        Assert.True(names.Count >= 32, $"only {names.Count} trees found");
        foreach (string name in names)
        {
            string text = Bbs757EfbTreeJson.TextOf(name).Replace("\r\n", "\n", StringComparison.Ordinal);
            var apps = Bbs757EfbTreeJson.Read(text);
            string again = text.StartsWith('{') ? Bbs757EfbTreeJson.Write(apps, Bbs757EfbTreeJson.ReadNotices(text)) : Bbs757EfbTreeJson.Write(apps);
            Assert.True(text == again.Replace("\r\n", "\n", StringComparison.Ordinal), name);
        }
    }

    [Fact]
    public void The_json_uses_invariant_numbers_under_a_comma_decimal_culture()
    {
        foreach (var culture in new[] { "de-DE", "sv-SE" })
        {
            string json = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Write(App(N("a", "x"))));
            Assert.Contains("\"x\": 1.5", json);
            var back = TestCulture.Run(culture, () => Bbs757EfbTreeJson.Read(json));
            Assert.Equal(2.25f, back[0].Controls[0].Y);
        }
    }
}
