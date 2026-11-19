using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;
using Node = MSFSBlindAssist.SimConnect.BBS757.Bbs757EfbMemory.Node;
using App = MSFSBlindAssist.SimConnect.BBS757.Bbs757EfbMemory.App;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The EFB model reader turns the memory walk into the window's model. These build synthetic app/control trees shaped
/// like the ones BlueBird's tablet draws (measured live 2026-11-06: the home screen, the Settings sections, a ground
/// service tile) and check the model the reader makes: its menu keys navigate, its section names the page, its rows
/// carry the switch state, and the page chrome is left out.
/// </summary>
public class Bbs757EfbModelReaderTests
{
    private static uint _addr = 0x1000;
    private static Node N(string kind, string name, string text, int x, int y, int w, int h, params Node[] children) =>
        new(kind, name, text, x, y, w, h, _addr += 0x40, children);

    // ---- the model's grammar fields ----

    [Fact]
    public void A_model_with_every_new_field_reads_back_equal()
    {
        var box = new System.Drawing.Rectangle(300, 200, 100, 30);
        var hit = new System.Drawing.Point(350, 215);
        var items = new[]
        {
            new Bbs757EfbItem(Bbs757EfbKind.Heading, "Doors", box, hit, false, "h:Doors", Level: 3, Name: "doors_title"),
            new Bbs757EfbItem(Bbs757EfbKind.Button, "OPEN", box, hit, true, "btn:OPEN", Label: "NOSE DOOR", Name: "nose_btn"),
            new Bbs757EfbItem(Bbs757EfbKind.Field, "SimBrief ID", box, hit, true, "f:id", Value: "12345"),
            new Bbs757EfbItem(Bbs757EfbKind.Tab, "Fuel", box, hit, true, "tab:Fuel", Selected: true),
            new Bbs757EfbItem(Bbs757EfbKind.LongText, "one\ntwo", box, hit, false, "lt:1"),
            new Bbs757EfbItem(Bbs757EfbKind.Field, "Airport", box, hit, true, "planning_perf_calc_airport_input", Value: "KSEA"),
            new Bbs757EfbItem(Bbs757EfbKind.Choice, "Runway", box, hit, true, "planning_perf_calc_runway_dropdown", Value: "RW07L",
                Entries: new[] { "RW07L", "RW26" }),
        };
        var model = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", Array.Empty<Bbs757EfbItem>(), items)
        {
            Viewport = new System.Drawing.Rectangle(1, 2, 3, 4),
            Keyboard = new Bbs757EfbKeyboard(new System.Drawing.Rectangle(0, 648, 1397, 400), new[]
            {
                new Bbs757EfbKey("char_btn_q|Q|1", "q", "Q", "1", new System.Drawing.Point(40, 700)),
                new Bbs757EfbKey("btn_enter", "", "", "", new System.Drawing.Point(900, 800)),
            }),
        };

        var back = Bbs757EfbModelJson.Read(Bbs757EfbModelJson.Write(model));

        // A list compares by reference in a record: the entries are compared on their own.
        Assert.Equal(model.Items.Select(i => i with { Entries = null }), back.Items.Select(i => i with { Entries = null }));
        Assert.Equal(model.Items.Select(i => i.Entries is null ? "-" : string.Join("|", i.Entries)),
            back.Items.Select(i => i.Entries is null ? "-" : string.Join("|", i.Entries)));
        Assert.Equal(model.Viewport, back.Viewport);
        Assert.Equal(model.Keyboard!.Box, back.Keyboard!.Box);
        Assert.Equal(model.Keyboard.Keys, back.Keyboard.Keys);
    }

    // ---- the home screen ----

    [Fact]
    public void Home_offers_the_five_apps_as_navigating_menu_entries()
    {
        var home = new App(6, "HOME", new[]
        {
            N("other", "main_canvas", "", 0, 32, 1397, 1016,
                N("grid", "main_grid_7x3", "", 50, 82, 1297, 600,
                    N("button", "aircraft_btn", "Aircraft", 273, 127, 110, 110),
                    N("button", "navigation_btn", "Navigation", 458, 127, 110, 110),
                    N("button", "systems_btn", "Systems", 644, 127, 110, 110),
                    N("button", "checklist_btn", "Checklist", 829, 127, 110, 110),
                    N("button", "settings_btn", "Settings", 1014, 127, 110, 110))),
        });
        var shell = new App(2, "app", new[] { N("grid", "kb_grid", "", 0, 648, 1397, 400) });

        var model = Bbs757EfbModelReader.Read(new[] { home, shell })!;

        Assert.Equal("HOME", model.App);
        Assert.Equal("Home", model.PageName);
        var keys = model.Items.Select(i => i.Key).ToArray();
        Assert.Equal(new[] { "app:AIRCRAFT", "app:NAVIGATION", "app:SYSTEMS", "app:CHECKLIST", "app:SETTINGS" }, keys);
        Assert.All(model.Items, i => Assert.Equal(Bbs757EfbKind.Menu, i.Kind));
        Assert.Equal("Aircraft", model.Items[0].Text);
    }

    // ---- a Settings page: side menu, section, scrollable, rows ----

    /// <summary>A Settings page showing the section <paramref name="current"/> (its side-menu entry selected, as BlueBird marks it).</summary>
    private static App SettingsApp(string current, string selectedScrollbox, int stackHeight, params Node[] rows) =>
        Showing(SettingsApp(selectedScrollbox, stackHeight, rows), current);

    private static App SettingsApp(string selectedScrollbox, int stackHeight, params Node[] rows)
    {
        var menu = N("grid", "control_grid", "", 0, 82, 250, 966,
            N("other", "btn_stack", "", 0, 142, 250, 716,
                N("button", "btn_general", "GENERAL", 0, 142, 250, 60),
                N("button", "btn_aircraft", "AIRCRAFT", 0, 202, 250, 60),
                N("button", "btn_livery", "LIVERY OPTIONS", 0, 262, 250, 60),
                N("button", "btn_audio", "CALLOUTS", 0, 322, 250, 60),
                N("button", "btn_cabin_audio", "CABIN AUDIO", 0, 382, 250, 60),
                N("button", "btn_airac_data", "AIRAC DATA", 0, 442, 250, 60)));
        var mainGrid = N("grid", "main_grid", "", 0, 32, 1397, 1016,
            N("other", "gradbox_tl", "", 0, 32, 250, 50,
                N("button", "home_btn", "", 0, 32, 50, 50),
                N("label", "page_title_label", "SETTINGS", 60, 42, 145, 36)),
            N("other", "gradbox_br", "", 0, 82, 250, 966, menu));
        var stack = N("other", selectedScrollbox[..^"scrollbox".Length] + "stackbox", "", 250, 94, 1147, stackHeight, rows);
        var pageGrid = N("grid", "page_main_grid", "", 0, 32, 1397, 1016,
            N("grid", "page_control_grid", "", 0, 82, 1397, 966,
                N("other", "page_colorbox", "", 250, 82, 1147, 966,
                    N("other", selectedScrollbox, "", 250, 82, 1147, 966, stack))));
        return new App(12, "SETTINGS", new[] { mainGrid, pageGrid });
    }

    private static Node SettingRow(string name, string kind, string label, string value) =>
        N("grid", name + "_grid", "", 250, 94, 1147, 60,
            N("other", name + "_title_group", "", 250, 94, 647, 60,
                N("other", name + "_label", label, 280, 94, 647, 60)),
            N(kind, name + "_toggle", "", 1218, 107, 60, 35),
            N("label", name + "_value_label", value, 1197, 94, 200, 60));

    [Fact]
    public void Callouts_names_its_section_marks_it_selected_and_scrolls()
    {
        var app = SettingsApp("CALLOUTS", "callouts_scrollbox", 1832,
            SettingRow("pilot_callout_v1", "toggle", "V1", "ON"),
            SettingRow("callout_35", "toggle", "35", "OFF"));

        var model = Bbs757EfbModelReader.Read(new[] { app })!;

        Assert.Equal("SETTINGS", model.App);
        Assert.Equal("CALLOUTS", model.Section);
        Assert.Equal("Settings, Callouts", model.PageName);
        Assert.True(model.Scrollable);

        var menu = model.Menu;
        Assert.Equal(6, menu.Count);
        Assert.All(menu, m => Assert.Equal(Bbs757EfbKind.Menu, m.Kind));
        Assert.Equal(new[] { "menu:GENERAL", "menu:AIRCRAFT", "menu:LIVERY OPTIONS", "menu:CALLOUTS", "menu:CABIN AUDIO", "menu:AIRAC DATA" },
            menu.Select(m => m.Key).ToArray());
        var callouts = Assert.Single(menu, m => m.Key == "menu:CALLOUTS");
        Assert.True(callouts.Selected);
        Assert.DoesNotContain(menu, m => m.Key != "menu:CALLOUTS" && m.Selected);

        var v1 = Assert.Single(model.Items, i => i.Text == "V1");
        Assert.Equal(Bbs757EfbKind.Toggle, v1.Kind);
        Assert.True(v1.On);
        Assert.Equal("ON", v1.Value);
        Assert.Equal("pilot_callout_v1_toggle", v1.Key); // the control name, stable across reads
        var c35 = Assert.Single(model.Items, i => i.Text == "35");
        Assert.False(c35.On);
    }

    [Fact]
    public void A_short_section_does_not_scroll()
    {
        var app = SettingsApp("GENERAL", "general_scrollbox", 420, SettingRow("x", "toggle", "A ROW", "ON"));
        var model = Bbs757EfbModelReader.Read(new[] { app })!;
        Assert.Equal("GENERAL", model.Section);
        Assert.False(model.Scrollable);                          // stackbox 420 fits the 966 scrollbox
    }

    // ---- R1: the section of every page ----

    /// <summary>A page shaped like the real ones: the shell grids and the side menu at the left, the page's controls from x 250.</summary>
    private static App PageApp(string app, string[] menuWords, params Node[] content)
    {
        var buttons = menuWords.Select((w, i) => N("button", "btn_" + i, w, 0, 142 + 60 * i, 250, 60)).ToArray();
        return new App(7, app, new[]
        {
            N("grid", "main_grid", "", 0, 32, 1397, 1016,
                N("grid", "control_grid", "", 0, 82, 250, 966, N("other", "btn_stack", "", 0, 142, 250, 716, buttons))),
            N("grid", "page_main_grid", "", 0, 32, 1397, 1016,
                N("grid", "page_control_grid", "", 0, 82, 1397, 966,
                    N("other", "page_colorbox", "", 250, 82, 1147, 966, content))),
        });
    }

    /// <summary>The page with BlueBird's selected state on its side menu (plan 21): the entry whose words are <paramref name="word"/>
    /// current, the others not, as the memory walk reads a side menu.</summary>
    private static App Showing(App app, string word)
    {
        Node Walk(Node n) => n with
        {
            Selected = n.Kind == "button" && n.X < Bbs757EfbScreen.MenuRight && n.Text.Length > 0 ? n.Text == word : n.Selected,
            Children = n.Children.Select(Walk).ToList(),
        };
        return app with { Controls = app.Controls.Select(Walk).ToList() };
    }

    public static TheoryData<string, string> EveryMenuEntry()
    {
        var data = new TheoryData<string, string>();
        foreach (var app in new[] { "AIRCRAFT", "NAVIGATION", "SETTINGS" })
            foreach (var word in Bbs757EfbVocabulary.Menus[app]) data.Add(app, word);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryMenuEntry))]
    public void The_section_is_the_selected_menu_entry_whatever_the_page_draws(string app, string word)
    {
        // R1 from BlueBird's selected state (plan 21): the entry it marks is the section, and the only one marked, whatever names the
        // page draws (a PERF calc canvas, a fuel_ name, which the page rules' names would have read as their own sections).
        var page = Showing(PageApp(app, Bbs757EfbVocabulary.Menus[app],
            N("other", "planning_perf_calc_canvas", "", 250, 82, 1147, 966, N("other", "fuel_left_tank", "", 300, 200, 100, 30))), word);

        var model = Bbs757EfbModelReader.Read(new[] { page })!;

        Assert.Equal(word, model.Section);
        Assert.Equal(Bbs757EfbVocabulary.Pretty(app) + ", " + Bbs757EfbVocabulary.Pretty(word), model.PageName);
        Assert.Equal(new[] { "menu:" + word }, model.Menu.Where(m => m.Selected).Select(m => m.Key));
    }

    [Theory]
    [InlineData("11-aircraft-fuel", "Aircraft")]
    [InlineData("25-navigation-perf-calc", "Navigation")]
    [InlineData("55-settings-airac-data", "Settings")]
    [InlineData("55b-settings-airac-data-navigraph-signed-out", "Settings")]
    public void A_read_with_no_selected_state_names_no_section(string fixture, string pageName)
    {
        // The four trees captured before plan 21 and not captured again (their states could not be made on 2026-11-07) carry no
        // selected state: no section, no entry marked; the page rules' table of names that told a section went with plan 21.
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!;
        Assert.Equal(("", pageName), (model.Section, model.PageName));
        Assert.DoesNotContain(model.Menu, m => m.Selected);

        var none = Bbs757EfbModelReader.Read(new[] { PageApp("AIRCRAFT", Bbs757EfbVocabulary.Menus["AIRCRAFT"], N("other", "maintenance_layout_grid", "", 250, 82, 1147, 966)) })!;
        Assert.Equal("", none.Section);
    }

    [Fact]
    public void Livery_options_and_aircraft_read_their_selected_entries()
    {
        // Settings > Livery options draws no scrollbox and no name of its own (its rows sit in the stackbox Settings > Aircraft shares):
        // the recaptured pages name each by the entry BlueBird marks.
        Assert.Equal("Settings, Livery options", Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("52-settings-livery-options"))!.PageName);
        Assert.Equal("Settings, Aircraft", Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!.PageName);
    }

    [Fact]
    public void The_section_marks_its_menu_entry_current()
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import"))!;
        var current = Assert.Single(model.Menu, m => m.Selected);
        Assert.Equal("menu:FUEL", current.Key);
    }

    [Theory]
    [InlineData("30-systems", "Systems", null)]
    [InlineData("40-checklist", "Checklist", "menu:BEFORE START")]
    public void A_page_matching_no_section_reads_its_app_alone(string fixture, string pageName, string? current)
    {
        // Systems and Checklist have no sections. The checklist's side menu is its list of checklists: BlueBird marks the one shown
        // (BEFORE START when recaptured), and the RESET ALL beside it, which reads selected too, is a button, never an entry.
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!;
        Assert.Equal("", model.Section);
        Assert.Equal(pageName, model.PageName);
        Assert.Equal(current is null ? Array.Empty<string>() : new[] { current }, model.Menu.Where(m => m.Selected).Select(m => m.Key));
    }

    // ---- R2: tab rows and the current tab ----

    private static Node Tab(string name, string text) => N("button", name, text, 300, 100, 120, 40);

    [Fact]
    public void A_tab_row_marks_its_selected_tab()
    {
        // R2 from BlueBird's selected state (plan 21), on the recaptured pages: Maintenance's gear page and hydraulic page, Weather's
        // two rows (METAR, DEPARTURE) and Charts' two (MANUAL, TAXI).
        var landing = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var gearTabs = landing.Items.Where(i => i.Kind == Bbs757EfbKind.Tab).ToList();
        Assert.Equal(5, gearTabs.Count);
        Assert.Equal("maintenance_landing_gear_tab_button", gearTabs[0].Name);
        Assert.Equal("tab:maintenance_selector_grid|LANDING GEAR", gearTabs[0].Key);
        Assert.Equal("Landing gear", gearTabs[0].Text);
        Assert.Equal(new[] { "LANDING GEAR" }, gearTabs.Where(t => t.Selected).Select(t => t.Key["tab:maintenance_selector_grid|".Length..]));

        string[] Current(string fixture) =>
            Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!.Items.Where(i => i.Kind == Bbs757EfbKind.Tab && i.Selected).Select(t => t.Name).ToArray();
        Assert.Equal(new[] { "maintenance_hydraulic_tab_button" }, Current("15b-aircraft-maintenance-hydraulic"));
        Assert.Equal(new[] { "planning_weather_primary_selector_button_0", "planning_weather_primary_tab_button_0" }, Current("22-navigation-weather"));
        Assert.Equal(new[] { "planning_charts_source_button_2", "planning_charts_type_button_2" }, Current("23-navigation-charts"));
    }

    [Fact]
    public void A_tab_row_with_no_tab_selected_marks_none()
    {
        var page = PageApp("NAVIGATION", Bbs757EfbVocabulary.Menus["NAVIGATION"],
            N("other", "planning_perf_calc_canvas", "", 250, 82, 1147, 966,
                N("grid", "tabs_grid", "", 260, 90, 300, 40, Tab("x_tab_button_0", "TAKE OFF"), Tab("x_tab_button_1", "LANDING")),
                N("other", "x_take_off_panel", "", 300, 200, 100, 30)));

        var tabs = Bbs757EfbModelReader.Read(new[] { page })!.Items.Where(i => i.Kind == Bbs757EfbKind.Tab).ToList();

        Assert.Equal(2, tabs.Count);
        Assert.DoesNotContain(tabs, t => t.Selected);   // a panel named for a tab no longer marks it
    }

    [Fact]
    public void A_tab_row_with_two_tabs_selected_marks_none()
    {
        // A torn read: both of PERF calc's tabs selected. Which one shows is not known, so neither is current.
        var both = Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"),
            "planning_perf_calc_tab_button_0", "planning_perf_calc_tab_button_1");
        Assert.DoesNotContain(Bbs757EfbModelReader.Read(both)!.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Selected);
    }

    [Fact]
    public void A_tab_row_BlueBird_keeps_no_selected_state_for_marks_none()
    {
        // Failures' tabs read no selected state in the recapture (2026-11-07), so none is current; nor does the footer button
        // "RANDOM FAILURES: OFF", which holds the RANDOM tab's word, mark one.
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("16-aircraft-failures"))!;
        var tabs = model.Items.Where(i => i.Kind == Bbs757EfbKind.Tab).ToList();
        Assert.Equal(3, tabs.Count);
        Assert.DoesNotContain(tabs, t => t.Selected);
    }

    [Fact]
    public void A_lone_tab_name_and_the_unit_switches_are_plain_buttons()
    {
        var page = PageApp("NAVIGATION", Bbs757EfbVocabulary.Menus["NAVIGATION"],
            N("other", "planning_perf_calc_canvas", "", 250, 82, 1147, 966,
                N("grid", "g1", "", 260, 90, 300, 40, Tab("x_tab_button_0", "TAKE OFF")),
                N("grid", "g2", "", 260, 140, 300, 40, Tab("x_unit_button_0", "Feet"), Tab("x_unit_button_1", "Pounds"))));

        Assert.DoesNotContain(Bbs757EfbModelReader.Read(new[] { page })!.Items, i => i.Kind == Bbs757EfbKind.Tab);
    }

    [Fact]
    public void The_tab_names_BlueBird_draws_are_tab_rows()
    {
        foreach (var name in new[] { "maintenance_apu_tab_button", "planning_weather_primary_tab_button_3", "failure_tab_2",
                     "planning_charts_source_button_0", "planning_charts_type_button_4" })
            Assert.True(Bbs757EfbPageRules.IsTabName(name), name);
        Assert.True(Bbs757EfbPageRules.IsTabName("planning_weather_primary_selector_button_0"));
        foreach (var name in new[] { "planning_perf_calc_unit_button_0", "failure_tabs_divider", "x_tab_button_x" })
            Assert.False(Bbs757EfbPageRules.IsTabName(name), name);
    }

    [Fact]
    public void The_page_title_and_home_button_are_not_items()
    {
        var model = Bbs757EfbModelReader.Read(new[] { SettingsApp("general_scrollbox", 420) })!;
        Assert.DoesNotContain(model.Menu.Concat(model.Items), i => i.Text == "SETTINGS");
        Assert.DoesNotContain(model.Menu.Concat(model.Items), i => i.Key.EndsWith("home_btn"));
        Assert.DoesNotContain(model.Menu.Concat(model.Items), i => i.Key == "btn:HOME");
    }

    // ---- a ground-service tile keeps its state in its words, keyed for the press runner ----

    [Fact]
    public void A_ground_service_tile_is_keyed_so_the_press_runner_finds_its_service()
    {
        var app = new App(7, "AIRCRAFT", new[]
        {
            N("grid", "page_main_grid", "", 0, 32, 1397, 1016,
                N("button", "gpu_btn", "GPU REMOVED", 300, 300, 200, 120),
                N("button", "fuel_truck_btn", "FUEL TRUCK OFF", 520, 300, 200, 120)),
        });

        var model = Bbs757EfbModelReader.Read(new[] { app })!;

        var gpu = Assert.Single(model.Items, i => i.Text.StartsWith("GPU", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("GPU", Bbs757EfbScreen.KeyWords(gpu.Key)); // the press runner's ground-power branch keys off "GPU"
        var fuel = Assert.Single(model.Items, i => i.Text.StartsWith("Fuel truck", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("FUEL TRUCK", Bbs757EfbScreen.KeyWords(fuel.Key));
        Assert.NotNull(Bbs757GroundServices.ForTile(Bbs757EfbScreen.KeyWords(fuel.Key)));
    }

    [Fact]
    public void No_front_app_reads_as_null()
    {
        Assert.Null(Bbs757EfbModelReader.Read(Array.Empty<App>()));
        var shellOnly = new App(2, "app", new[] { N("grid", "kb_grid", "", 0, 648, 1397, 400) });
        Assert.Null(Bbs757EfbModelReader.Read(new[] { shellOnly }));
    }

    // ---- R3 headings, R4 value lines, R10 pictures and separators ----

    private static Node Lbl(string name, string text, int x = 300, int y = 200, int w = 150) => N("label", name, text, x, y, w, 24);

    private static IReadOnlyList<Bbs757EfbItem> ItemsOf(params Node[] content) =>
        Bbs757EfbModelReader.Read(new[] { PageApp("AIRCRAFT", Bbs757EfbVocabulary.Menus["AIRCRAFT"], content) })!.Items;

    /// <summary>The items of an Aircraft page showing <paramref name="section"/> (its entry selected), for the orders a section gives (R9).</summary>
    private static IReadOnlyList<Bbs757EfbItem> ItemsOn(string section, params Node[] content) =>
        Bbs757EfbModelReader.Read(new[] { Showing(PageApp("AIRCRAFT", Bbs757EfbVocabulary.Menus["AIRCRAFT"], content), section) })!.Items;

    [Fact]
    public void A_label_and_value_named_alike_are_one_line()
    {
        var items = ItemsOf(
            Lbl("planning_acars_flight_info_label_0", "Flight Number", 300, 200), Lbl("planning_acars_flight_info_value_0", "4RK", 300, 220),
            Lbl("planning_acars_flight_info_label_1", "Callsign", 500, 200), Lbl("planning_acars_flight_info_value_1", "MSF1", 500, 220));

        Assert.Equal(2, items.Count);
        var flight = items[0];
        Assert.Equal((Bbs757EfbKind.Row, "Flight Number", "4RK", "planning_acars_flight_info_label_0"), (flight.Kind, flight.Text, flight.Value, flight.Name));
        Assert.Equal(("Callsign", "MSF1"), (items[1].Text, items[1].Value));
    }

    [Fact]
    public void A_title_with_a_value_is_a_line_not_a_heading()
    {
        var items = ItemsOf(Lbl("wb_empty_weight_title", "EMPTY"), Lbl("wb_empty_weight_value", "136940 Lbs"));

        var line = Assert.Single(items);
        Assert.Equal((Bbs757EfbKind.Row, "EMPTY", "136940 Lbs"), (line.Kind, line.Text, line.Value)); // the tablet's case stays
        Assert.DoesNotContain(items, i => i.Kind == Bbs757EfbKind.Heading);
    }

    [Fact]
    public void Lbl_title_and_lbl_val_pair()
    {
        var items = ItemsOf(
            Lbl("lbl_title_V1", "V1", 300, 200), Lbl("lbl_val_V1", "---", 300, 220),
            Lbl("len_title_TODA", "TODA", 500, 200), Lbl("len_val_TODA", "---", 500, 220),
            Lbl("lbl_title_FLX TMP", "FLX TMP", 700, 200), Lbl("lbl_val_FLX TMP", "---", 700, 220));

        Assert.Equal(new[] { ("V1", "---"), ("TODA", "---"), ("FLX TMP", "---") }, items.Select(i => (i.Text, i.Value)).ToArray());
        Assert.All(items, i => Assert.Equal(Bbs757EfbKind.Row, i.Kind));
    }

    [Fact]
    public void A_name_and_value_pair()
    {
        var items = ItemsOf(Lbl("cabin_ceiling_name", "CEILING"), Lbl("cabin_window_name", "WINDOW", 300, 240),
            Lbl("cabin_ceiling_value", "OFF", 500, 200), Lbl("cabin_window_value", "OFF", 500, 240));

        Assert.Equal(new[] { ("CEILING", "OFF"), ("WINDOW", "OFF") }, items.Select(i => (i.Text, i.Value)).ToArray());
    }

    [Fact]
    public void A_value_label_and_a_saved_label_are_value_side()
    {
        var items = ItemsOf(
            Lbl("latest_cycle_title_label", "LATEST CYCLE:", 300, 200), Lbl("latest_cycle_value_label", "2611", 500, 200),
            Lbl("simbrief_id_label", "SIMBRIEF ID NUMBER", 300, 240), Lbl("simbrief_id_saved_label", "123456", 500, 240));

        Assert.Equal(new[] { ("LATEST CYCLE:", "2611"), ("SIMBRIEF ID NUMBER", "123456") }, items.Select(i => (i.Text, i.Value)).ToArray());
        Assert.All(items, i => Assert.Equal(Bbs757EfbKind.Row, i.Kind));
    }

    [Fact]
    public void A_title_and_a_longer_value_title_do_not_pair()
    {
        // PERF calc's "Length Type" and "Length (FT)": both titles, so neither is the other's value.
        var items = ItemsOf(Lbl("planning_perf_calc_length_title", "Length Type"), Lbl("planning_perf_calc_length_value_title", "Length (FT)", 500, 200));

        Assert.Equal(new[] { "Length Type", "Length (FT)" }, items.Select(i => i.Text).ToArray());
        Assert.All(items, i => Assert.Equal(Bbs757EfbKind.Heading, i.Kind));
    }

    [Fact]
    public void A_unit_joins_its_value()
    {
        var paired = ItemsOf(Lbl("fuel_left_qty_label", "LEFT TANK"), Lbl("fuel_left_qty_value", "1539", 500, 200), Lbl("fuel_left_qty_unit", "Gal", 600, 200));
        var line = Assert.Single(paired);
        Assert.Equal((Bbs757EfbKind.Row, "LEFT TANK", "1539 Gal"), (line.Kind, line.Text, line.Value));

        // The unit may drop qty from its name (fuel_left_qty_unit beside fuel_left_value).
        var shorter = ItemsOf(Lbl("fuel_left_label", "LEFT"), Lbl("fuel_left_value", "1539", 500, 200), Lbl("fuel_left_qty_unit", "Gal", 600, 200));
        Assert.Equal("1539 Gal", Assert.Single(shorter).Value);
    }

    [Fact]
    public void A_number_with_no_label_reads_with_its_unit()
    {
        // The Fuel page's names: the number has no label or value word in its name, only its unit beside it.
        var items = ItemsOf(Lbl("fuel_left_qty", "1539"), Lbl("fuel_left_qty_unit", "Gal", 600, 200),
            Lbl("fuel_left_weight", "10308", 300, 240), Lbl("fuel_left_weight_unit", "Lbs", 600, 240));

        Assert.Equal(new[] { "1539 Gal", "10308 Lbs" }, items.Select(i => i.Text).ToArray());
        Assert.Equal("fuel_left_qty", items[0].Key);
    }

    [Fact]
    public void A_unit_whose_number_is_not_read_stays_a_line_of_its_own()
    {
        // The Fuel page's numbers sit in value boxes, which are fields now (R7): the unit joins the number, no line of its own.
        var fuel = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11-aircraft-fuel"))!.Items;
        Assert.DoesNotContain(fuel, i => i.Kind == Bbs757EfbKind.Text && i.Text is "Gal" or "Lbs");
        var left = fuel.Where(i => i.Kind == Bbs757EfbKind.Field).Take(2).Select(i => (i.Text, i.Value)).ToArray();
        Assert.Equal(new[] { ("QUANTITY", "1539 Gal"), ("WEIGHT", "10308 Lbs") }, left);

        // A unit with no number to join (nothing is named like it) still reads, as a line.
        var alone = ItemsOf(Lbl("fuel_left_qty_unit", "Gal", 600, 200));
        Assert.Equal(("Gal", Bbs757EfbKind.Text), (Assert.Single(alone).Text, alone[0].Kind));
    }

    [Fact]
    public void A_card_title_is_a_level_2_heading()
    {
        var items = ItemsOf(N("other", "planning_acars_flight_plan_canvas", "", 260, 90, 800, 300,
            Lbl("planning_acars_flight_plan_title", "FLIGHT PLAN", 300, 100, 300),
            Lbl("planning_acars_flight_plan_label_0", "Route Distance", 300, 140), Lbl("planning_acars_flight_plan_value_0", "0", 300, 160)));

        Assert.Equal(new[] { Bbs757EfbKind.Heading, Bbs757EfbKind.Row }, items.Select(i => i.Kind).ToArray());
        Assert.Equal(("FLIGHT PLAN", 2), (items[0].Text, items[0].Level));
        Assert.Equal(0, items[1].Level);
    }

    [Fact]
    public void A_title_inside_a_titled_card_is_level_3()
    {
        var items = ItemsOf(N("other", "outer_card", "", 260, 90, 800, 600,
            Lbl("outer_title", "OUTER", 300, 100),
            N("other", "inner_card", "", 270, 140, 700, 300, Lbl("inner_title", "inner", 300, 150)),
            N("other", "second_card", "", 270, 450, 700, 200, Lbl("second_title", "SECOND", 300, 460))));

        Assert.Equal(new[] { ("OUTER", 2), ("inner", 3), ("SECOND", 3) }, items.Select(i => (i.Text, i.Level)).ToArray());
        Assert.All(items, i => Assert.Equal(Bbs757EfbKind.Heading, i.Kind));
    }

    [Fact]
    public void Sibling_cards_each_titled_are_all_level_2()
    {
        var items = ItemsOf(
            N("other", "a_card", "", 260, 90, 400, 300, Lbl("a_title", "FROM", 300, 100)),
            N("other", "b_card", "", 700, 90, 400, 300, Lbl("b_title", "TO", 740, 100)));

        Assert.Equal(new[] { ("FROM", 2), ("TO", 2) }, items.Select(i => (i.Text, i.Level)).ToArray());
    }

    [Fact]
    public void The_route_is_no_heading()
    {
        var items = ItemsOf(N("other", "planning_acars_route_canvas", "", 260, 90, 1100, 200,
            Lbl("planning_acars_route_title", "ROUTE", 300, 100),
            N("other", "planning_acars_route_value", "KSEA/16L DCT BTG DCT KPDX/28R", 324, 140, 1000, 40)));

        var heading = Assert.Single(items, i => i.Kind == Bbs757EfbKind.Heading);
        Assert.Equal("ROUTE", heading.Text);
        var route = Assert.Single(items, i => i.Text.StartsWith("KSEA", StringComparison.Ordinal));
        Assert.Equal(Bbs757EfbKind.Text, route.Kind); // one line tall: plain text
    }

    [Fact]
    public void The_route_is_long_text()
    {
        var tall = ItemsOf(N("other", "planning_acars_route_value", "KSEA/16L DCT BTG DCT KPDX/28R", 324, 140, 1000, 80));
        Assert.Equal((Bbs757EfbKind.LongText, "KSEA/16L DCT BTG DCT KPDX/28R"), (Assert.Single(tall).Kind, tall[0].Text));

        var broken = ItemsOf(Lbl("vnav_takeoff_help_textbox", "first line\nsecond line"));
        Assert.Equal(Bbs757EfbKind.LongText, Assert.Single(broken).Kind);
        Assert.Equal("first line\nsecond line", broken[0].Text);

        // Operating FP's plan is a long text when it holds one; an empty one reads as nothing (M5).
        var plan = ItemsOf(N("other", "planning_operating_fp_value", "KSEA DCT BTG DCT KPDX", 324, 152, 1000, 25761));
        Assert.Equal(Bbs757EfbKind.LongText, Assert.Single(plan).Kind);
        Assert.Empty(ItemsOf(N("multiline", "planning_operating_fp_value", "", 324, 152, 1000, 25761)));
    }

    [Fact]
    public void A_title_label_that_is_not_the_page_title_is_kept()
    {
        var items = ItemsOf(Lbl("page_title_label", "AIRCRAFT"), Lbl("gnd_service_mode_title_label", "GND SRVC Mode:"),
            Lbl("latest_cycle_title_label", "LATEST CYCLE:", 300, 240), Lbl("latest_cycle_value_label", "2611", 500, 240));

        Assert.Equal(2, items.Count);
        Assert.Equal((Bbs757EfbKind.Heading, "GND SRVC Mode:"), (items[0].Kind, items[0].Text));
        Assert.Equal((Bbs757EfbKind.Row, "LATEST CYCLE:", "2611"), (items[1].Kind, items[1].Text, items[1].Value));
        Assert.DoesNotContain(items, i => i.Text == "AIRCRAFT");
    }

    [Fact]
    public void A_heading_name_and_the_presets_labels_are_headings()
    {
        var callouts = ItemsOf(Lbl("callouts_pilot_heading", "PILOT CALLOUTS"), Lbl("callouts_dh_help", "Captain DH must be above -20.", 300, 240));
        Assert.Equal((Bbs757EfbKind.Heading, Bbs757EfbKind.Text), (callouts[0].Kind, callouts[1].Kind));

        // In the order and at the levels Cockpit presets reads them (Bbs757EfbPageRules.Reorder): its three columns under PRESETS.
        var presets = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("10-aircraft-cockpit-presets"))!.Items;
        Assert.Equal(new[] { ("PRESETS", 2), ("ON GROUND", 3), ("IN FLIGHT", 3), ("CUSTOM", 3), ("QUICK START", 2), ("CIRCUITS STATE", 2) },
            presets.Where(i => i.Kind == Bbs757EfbKind.Heading).Select(i => (i.Text, i.Level)).ToArray());
    }

    [Fact]
    public void A_line_whose_label_ends_in_a_colon_reads_with_one_colon()
    {
        var model = Bbs757EfbModelReader.Read(new[] { PageApp("NAVIGATION", Bbs757EfbVocabulary.Menus["NAVIGATION"],
            Lbl("planning_acars_departure_label_0", "Airport:"), Lbl("planning_acars_departure_value_0", "SEATTLE-TACOMA INTL", 500, 200),
            Lbl("planning_acars_flight_info_label_0", "Callsign"), Lbl("planning_acars_flight_info_value_0", "MSF1", 500, 240)) })!;

        var texts = Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>()).Data
            .Where(kv => kv.Key.EndsWith(".text", StringComparison.Ordinal)).Select(kv => kv.Value).ToList();

        Assert.Contains("Airport: SEATTLE-TACOMA INTL", texts);
        Assert.Contains("Callsign: MSF1", texts);
    }

    [Fact]
    public void A_level_3_heading_is_built_at_level_3()
    {
        var model = new Bbs757EfbScreenModel("AIRCRAFT", "", Array.Empty<Bbs757EfbItem>(), new[]
        {
            new Bbs757EfbItem(Bbs757EfbKind.Heading, "Inner", default, default, false, "h:in", Level: 3),
            new Bbs757EfbItem(Bbs757EfbKind.Heading, "Outer", default, default, false, "h:out", Level: 2),
        });
        var data = Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>()).Data;
        var levels = Enumerable.Range(0, 4).Where(i => data.ContainsKey($"items.{i}.text"))
            .ToDictionary(i => data[$"items.{i}.text"], i => data[$"items.{i}.level"]);
        Assert.Equal(("3", "2"), (levels["Inner"], levels["Outer"]));
    }

    [Fact]
    public void A_capitals_label_is_a_heading_only_when_it_is_named_a_title()
    {
        // Wide capitals used to make a heading (a route, a cycle number); now only BlueBird's *_title does.
        var items = ItemsOf(Lbl("some_label", "SOMETHING VERY WIDE IN CAPITALS", 300, 200, 600), Lbl("planning_x_title", "A CARD", 300, 240, 600));

        Assert.Equal((Bbs757EfbKind.Text, 0), (items[0].Kind, items[0].Level));
        Assert.Equal(Bbs757EfbKind.Heading, items[1].Kind);
    }

    [Fact]
    public void Separators_are_dropped()
    {
        var items = ItemsOf(
            Lbl("planning_weather_primary_info_label_0", "AIRPORT"), Lbl("planning_weather_primary_info_value_0", "KSEA/SEA", 300, 220),
            Lbl("planning_weather_primary_info_separator_value_0", "|", 500, 220),
            Lbl("planning_weather_primary_info_label_1", "ELEVATION", 600), Lbl("planning_weather_primary_info_value_1", "432 FT", 600, 220),
            Lbl("a_dash", "-- / --", 300, 300), Lbl("lone", "|", 300, 340));

        Assert.Equal(new[] { ("AIRPORT", "KSEA/SEA"), ("ELEVATION", "432 FT") }, items.Select(i => (i.Text, i.Value)).ToArray());
    }

    [Fact]
    public void A_pairing_is_read_the_same_under_other_cultures()
    {
        foreach (var culture in new[] { "de-DE", "sv-SE", "tr-TR" })
        {
            var items = TestCulture.Run(culture, () => ItemsOf(Lbl("WB_EMPTY_TITLE", "EMPTY"), Lbl("wb_empty_value", "136940 Lbs", 500, 200)));
            Assert.Equal(("EMPTY", "136940 Lbs"), (Assert.Single(items).Text, items[0].Value));
        }
    }

    [Theory]
    [InlineData("./data/efb/images/bbs_ldg_gr_chocks.png", "IN PLACE")]
    [InlineData("./data/efb/images/bbs_ldg_gr.png", "REMOVED")]
    [InlineData("bbs_ldg_gr.png", "REMOVED")]
    [InlineData("./data/efb/images/BBS_LDG_GR.PNG", "REMOVED")]
    [InlineData("./data/efb/aircraft/bbs752pw_top.png", null)]
    [InlineData("./data/efb/bbs_logo_light.png", null)]
    [InlineData("", null)]
    public void A_picture_file_names_a_state_or_none(string text, string? state) =>
        Assert.Equal(state, Bbs757EfbPageRules.PictureState(text));

    [Theory]
    [InlineData("13-aircraft-ground-services", "IN PLACE")]
    [InlineData("13b-aircraft-ground-services-chocks-removed", "REMOVED")]
    public void The_chocks_picture_gives_its_tile_a_state(string fixture, string state)
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!;

        var chocks = Assert.Single(model.Items, i => i.Text.StartsWith("Chocks", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("CHOCKS " + state, chocks.Text, ignoreCase: true);
        Assert.Equal(Bbs757EfbKind.Button, chocks.Kind);
        Assert.StartsWith("btn:|CHOCKS@", chocks.Key);
        Assert.Equal("CHOCKS", Bbs757EfbScreen.KeyWords(chocks.Key)); // the press runner finds the service by these words
        Assert.NotNull(Bbs757GroundServices.ForTile(Bbs757EfbScreen.KeyWords(chocks.Key)));
        Assert.DoesNotContain(model.Items, i => i.Text.Contains(".png"));
    }

    [Fact]
    public void The_chocks_tile_keeps_its_key_when_its_state_changes()
    {
        string KeyOf(string fixture) => Assert.Single(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!.Items,
            i => i.Text.StartsWith("Chocks", StringComparison.OrdinalIgnoreCase)).Key;

        Assert.Equal(KeyOf("13-aircraft-ground-services"), KeyOf("13b-aircraft-ground-services-chocks-removed"));
    }

    [Fact]
    public void A_picture_joins_the_button_in_its_box()
    {
        var items = ItemsOf(N("other", "chocks_service_box", "", 300, 500, 220, 200,
            N("button", "btn_chocks_toggle", "CHOCKS", 310, 512, 200, 42),
            N("other", "chocks_status_image", "./data/efb/images/bbs_ldg_gr_chocks.png", 310, 565, 200, 100)),
            N("button", "btn_gpu_toggle", "GPU CONNECTED", 600, 512, 200, 42));

        Assert.Equal(new[] { "Chocks in place", "GPU connected" }, items.Select(i => i.Text).ToArray(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("btn:|CHOCKS@20,26", items[0].Key);
    }

    [Fact]
    public void A_picture_with_no_state_joins_nothing()
    {
        var items = ItemsOf(N("other", "chocks_service_box", "", 300, 500, 220, 200,
            N("button", "btn_chocks_toggle", "CHOCKS", 310, 512, 200, 42),
            N("other", "label_bg_img", "./data/efb/aircraft/bbs752pw_top.png", 310, 565, 200, 100)));

        Assert.Equal("Chocks", Assert.Single(items).Text);
    }

    [Fact]
    public void The_state_words_a_press_strips_include_a_multi_word_state()
    {
        Assert.Contains("IN PLACE", Bbs757EfbScreen.StateWords);
        Assert.Equal("CHOCKS", Bbs757EfbScreen.KeyWords("btn:|CHOCKS@15,25"));
    }

    // ---- R5 names, R6 index rows, R7 value boxes, R8 sliders, R11 icon buttons ----

    private static Node Btn(string name, string text, int x, int y, int w = 130, int h = 50, params Node[] kids) => N("button", name, text, x, y, w, h, kids);

    [Fact]
    public void A_state_word_button_takes_the_label_sharing_its_name()
    {
        var items = ItemsOf(
            Lbl("maintenance_gear_door_nose_label", "NOSE DOOR", 845, 608, 72), Btn("maintenance_gear_door_nose_button", "OPEN", 780, 636),
            Lbl("maintenance_gear_door_left_label", "LEFT DOOR", 985, 608, 68), Btn("maintenance_gear_door_left_button", "OPEN", 920, 636));

        Assert.Equal(new[] { ("Open", "NOSE DOOR"), ("Open", "LEFT DOOR") }, items.Select(i => (i.Text, i.Label)).ToArray());
        Assert.All(items, i => Assert.Equal(Bbs757EfbKind.Button, i.Kind));
        Assert.DoesNotContain(items, i => i.Text == "NOSE DOOR"); // the label is the button's name, not a line of its own
    }

    [Fact]
    public void A_door_button_takes_the_label_touching_it()
    {
        var items = ItemsOf(Btn("btn_l1_door", "CLOSED", 674, 187, 100, 40), Lbl("l1", "LEFT FWD", 692, 227, 64));

        var door = Assert.Single(items);
        Assert.Equal(("Closed", "LEFT FWD"), (door.Text, door.Label));
        Assert.StartsWith("btn:|@", door.Key); // the key is still the button's own, positional
    }

    [Fact]
    public void A_moving_door_reads_moving()
    {
        // Measured live 2026-11-07 (plan 21's findings, section 5): while a door moves, its button reads MOVING, and the window read
        // the row as "text  AFT CARGO". The cause: MOVING was no state word, so the button did not wait for a name (R5) and kept its bare
        // words ("button  Moving", in the doors' tree order), and its label AFT CARGO, naming nothing, read as a line of its own after
        // the door buttons. A sighted pilot sees MOVING under AFT CARGO: it stays spoken, as the door's state, under the door's name.
        var doors = Bbs757EfbTreeJson.Load("14-aircraft-cabin-doors");
        var apps = Bbs757EfbTreeJson.WithText(doors, "btn_aft_cargo_door", "MOVING");

        var model = Bbs757EfbModelReader.Read(apps)!;
        string listing = Bbs757EfbListing.Of(Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>()));

        Assert.Contains("button  FWD cargo: Closed\nbutton  AFT cargo: Moving\nbutton  Close all\n", listing);
        Assert.DoesNotContain("AFT CARGO", listing);
        var door = Assert.Single(model.Items, i => i.Name == "btn_aft_cargo_door");
        Assert.Equal((Bbs757EfbKind.Button, "Moving", "AFT CARGO"), (door.Kind, door.Text, door.Label));
        // The key a press relocates the door by is the one it had closed.
        Assert.Equal(Bbs757EfbModelReader.Read(doors)!.Items.Single(i => i.Name == "btn_aft_cargo_door").Key, door.Key);
    }

    [Fact]
    public void An_opened_door_reads_opened_under_its_name()
    {
        // The owner's in-sim check of 2026-11-07: once the AFT cargo door had opened, its button read "OPENED" and the window said
        // "Opened" alone: OPENED was no state word (as MOVING had been), so the button lost the label that names it.
        var doors = Bbs757EfbTreeJson.Load("14-aircraft-cabin-doors");
        var apps = Bbs757EfbTreeJson.WithText(doors, "btn_aft_cargo_door", "OPENED");

        var model = Bbs757EfbModelReader.Read(apps)!;
        string listing = Bbs757EfbListing.Of(Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>()));

        Assert.Contains("button  FWD cargo: Closed\nbutton  AFT cargo: Opened\nbutton  Close all\n", listing);
        Assert.DoesNotContain("AFT CARGO", listing);
        var door = Assert.Single(model.Items, i => i.Name == "btn_aft_cargo_door");
        Assert.Equal((Bbs757EfbKind.Button, "Opened", "AFT CARGO"), (door.Kind, door.Text, door.Label));
        Assert.Equal(Bbs757EfbModelReader.Read(doors)!.Items.Single(i => i.Name == "btn_aft_cargo_door").Key, door.Key);
    }

    [Fact]
    public void A_button_two_labels_touch_keeps_its_own_words()
    {
        var items = ItemsOf(Btn("btn_gate_door", "CLOSED", 300, 200, 100, 40), Lbl("below", "ONE", 310, 240, 60), Lbl("beside", "TWO", 200, 205, 95));

        var door = Assert.Single(items, i => i.Kind == Bbs757EfbKind.Button);
        Assert.Equal(("Closed", ""), (door.Text, door.Label));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void A_label_as_near_two_buttons_names_neither()
    {
        // The final review's M1: one label 6 px below one OPEN and 6 px above another. A tie names neither, whichever comes first.
        var items = ItemsOf(Btn("btn_a", "OPEN", 400, 200, 100, 40), N("label", "shared_label", "SHARED", 400, 246, 100, 16),
            Btn("btn_b", "OPEN", 400, 268, 100, 40));

        Assert.Equal(new[] { ("Open", ""), ("Open", "") }, items.Where(i => i.Kind == Bbs757EfbKind.Button).Select(i => (i.Text, i.Label)).ToArray());
        Assert.Contains(items, i => i.Kind == Bbs757EfbKind.Text && i.Text == "SHARED");   // still read, as a line of its own

        // One pixel nearer the second: the second takes it.
        var nearer = ItemsOf(Btn("btn_a", "OPEN", 400, 200, 100, 40), N("label", "shared_label", "SHARED", 400, 246, 100, 16),
            Btn("btn_b", "OPEN", 400, 267, 100, 40));
        Assert.Equal(new[] { "", "SHARED" }, nearer.Where(i => i.Kind == Bbs757EfbKind.Button).Select(i => i.Label).ToArray());
    }

    [Fact]
    public void A_settings_rows_buttons_are_named_by_the_row()
    {
        // The final review's I8: R5's fourth source. SimBrief's and Navigraph's LOGOUT, FETCH KEY and REMOVE read with their row's label;
        // a label that is a line with its value stays that line too.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("55-settings-airac-data"))!;

        Assert.Equal(new[]
        {
            ("simbrief_id_logout_button", "SIMBRIEF ID NUMBER"), ("sayintentions_fetch_key_button", "SAYINTENTIONS ACARS KEY"),
            ("btn_update", "LATEST CYCLE:"), ("btn_logout", "CONNECTED TO NAVIGRAPH"),
        }, page.Items.Where(i => i.Kind == Bbs757EfbKind.Button).Select(i => (i.Name, i.Label)).ToArray());
        Assert.Contains(page.Items, i => i.Kind == Bbs757EfbKind.Row && i.Text == "SIMBRIEF ID NUMBER" && i.Value == "123456");
        Assert.Contains(page.Items, i => i.Kind == Bbs757EfbKind.Row && i.Text == "LATEST CYCLE:" && i.Value == "2611");
        Assert.DoesNotContain(page.Items, i => i.Kind == Bbs757EfbKind.Text && i.Text == "CONNECTED TO NAVIGRAPH");   // said with its button
    }

    [Fact]
    public void A_button_whose_nearest_grid_is_no_settings_row_takes_no_row_name()
    {
        // PERF calc's unit buttons sit in a grid that holds the field titles (planning_perf_calc_row1_grid is no *_row): their own words.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25-navigation-perf-calc"))!;
        Assert.All(page.Items.Where(i => i.Kind == Bbs757EfbKind.Button), b => Assert.Equal("", b.Label));
    }

    [Fact]
    public void A_gear_doors_percent_is_named_by_its_doors_label()
    {
        // The final review's I9: maintenance_gear_door_nose_percent has no role word; maintenance_gear_door_nose_label names the door's
        // button and the percent below it.
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!.Items;

        Assert.Equal(new[] { ("NOSE DOOR", "0%"), ("LEFT DOOR", "0%"), ("RIGHT DOOR", "0%") },
            items.Where(i => i.Name.EndsWith("_percent", StringComparison.Ordinal)).Select(i => (i.Text, i.Value)).ToArray());
        Assert.All(items.Where(i => i.Name.EndsWith("_percent", StringComparison.Ordinal)), i => Assert.Equal(Bbs757EfbKind.Row, i.Kind));
        Assert.Equal(new[] { "NOSE DOOR", "LEFT DOOR", "RIGHT DOOR" },
            items.Where(i => i.Name.StartsWith("maintenance_gear_door_", StringComparison.Ordinal) && i.Kind == Bbs757EfbKind.Button).Select(i => i.Label).ToArray());
    }

    [Fact]
    public void A_role_less_value_under_a_label_that_names_no_control_keeps_its_words()
    {
        // Failures' column headers: failure_header_status shares failure_header_name's stem, but that label names no control, so
        // STATUS stays STATUS (never "FAILURE: STATUS").
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("16-aircraft-failures"))!.Items;
        Assert.Contains(items, i => i.Kind == Bbs757EfbKind.Text && i.Text == "STATUS");
        Assert.Contains(items, i => i.Kind == Bbs757EfbKind.Text && i.Text == "ACTIONS");
    }

    [Fact]
    public void A_cards_title_reads_before_the_rest_of_its_card()
    {
        // The final review's M3: REPAIR ALTN comes before LANDING GEAR DOORS in its card's tree; the title reads first.
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!.Items.Select(i => i.Name).ToList();
        Assert.True(items.IndexOf("maintenance_gear_doors_title") < items.IndexOf("maintenance_alternate_gear_repair"));
        Assert.True(items.IndexOf("maintenance_brake_reset_button") < items.IndexOf("maintenance_gear_doors_title"));
    }

    [Fact]
    public void Cockpit_presets_reads_each_heading_with_what_is_drawn_under_it()
    {
        // The final review's M3: BlueBird's tree gives all six headings, then the 17 buttons. By where they are drawn: each section,
        // and in PRESETS its three columns at level 3, each followed by its own buttons.
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("10-aircraft-cockpit-presets"))!.Items;
        string Line(Bbs757EfbItem i) => i.Kind == Bbs757EfbKind.Heading ? i.Level + " " + i.Text : i.Text;

        Assert.Equal(new[]
        {
            "2 PRESETS",
            "3 ON GROUND", "Cold & dark", "Turnaround", "Before start", "Ready for taxi", "Ready for takeoff",
            "3 IN FLIGHT", "After takeoff", "Cruise", "Approach", "Final approach",
            "3 CUSTOM", "Save user config", "Load user config", "Choose SAVE or LOAD,\nthen select a panel state.",
            "2 QUICK START", "APU quick start", "Engine quick start", "IRS rapid align",
            "2 CIRCUITS STATE", "ALL BREAKERS PUSHED IN", "PUSH ALL CBs", "Battery", "External power",
        }, items.Select(Line).ToArray());
    }

    [Fact]
    public void A_value_of_marks_reads_and_a_separator_does_not()
    {
        // The final review's M11: an unpaired value of marks is a value (a sighted pilot sees "------"); a role-less mark, or one
        // BlueBird names a separator, is a separator (R10).
        var items = ItemsOf(Lbl("hoppie_logon_code_value", "------", 300, 200), Lbl("planning_x_info_separator_value_0", "|", 300, 240),
            Lbl("divider", "|", 300, 280));

        var line = Assert.Single(items);
        Assert.Equal((Bbs757EfbKind.Text, "------"), (line.Kind, line.Text));
    }

    [Fact]
    public void A_label_far_from_a_button_names_nothing()
    {
        var door = Assert.Single(ItemsOf(Btn("btn_gate_door", "CLOSED", 300, 200, 100, 40), Lbl("far", "ONE", 310, 270, 60)), i => i.Kind == Bbs757EfbKind.Button);
        Assert.Equal("", door.Label);
    }

    [Fact]
    public void An_index_row_names_its_controls_and_state()
    {
        var items = ItemsOf(N("other", "failure_table", "", 280, 340, 1050, 500,
            Lbl("failure_name_0", "L ENGINE FIRE", 296, 390, 106), Lbl("failure_state_0", "READY", 730, 388, 61),
            Btn("failure_action_0", "FAIL NOW", 925, 380, 180, 44), Btn("failure_setup_0", "SET UP", 1125, 380, 180, 44),
            Lbl("failure_name_1", "R ENGINE FIRE", 296, 442, 107), Lbl("failure_state_1", "FAILED", 730, 440, 61),
            Btn("failure_action_1", "FAIL NOW", 925, 432, 180, 44), Btn("failure_setup_1", "SET UP", 1125, 432, 180, 44)));

        Assert.Equal(new[]
        {
            (Bbs757EfbKind.Row, "L ENGINE FIRE", "READY", ""), (Bbs757EfbKind.Button, "Fail now", "", "L ENGINE FIRE"), (Bbs757EfbKind.Button, "Set up", "", "L ENGINE FIRE"),
            (Bbs757EfbKind.Row, "R ENGINE FIRE", "FAILED", ""), (Bbs757EfbKind.Button, "Fail now", "", "R ENGINE FIRE"), (Bbs757EfbKind.Button, "Set up", "", "R ENGINE FIRE"),
        }, items.Select(i => (i.Kind, i.Text, i.Value, i.Label)).ToArray());
    }

    [Fact]
    public void An_index_row_needs_a_control_and_the_tab_buttons_make_none()
    {
        // A label and its value numbered alike are a line (R4), not an index row; tab buttons share a number with nothing else.
        var items = ItemsOf(N("other", "box", "", 280, 200, 900, 200,
            Lbl("planning_acars_flight_info_label_0", "Flight Number", 300, 200), Lbl("planning_acars_flight_info_value_0", "4RK", 300, 220),
            Tab("x_tab_button_0", "TAKE OFF"), Tab("x_tab_button_1", "LANDING")));
        Assert.Equal((Bbs757EfbKind.Row, "Flight Number", "4RK"), (items[0].Kind, items[0].Text, items[0].Value));
        Assert.Equal(2, items.Count(i => i.Kind == Bbs757EfbKind.Tab));
    }

    [Fact]
    public void A_value_box_is_a_field()
    {
        var items = ItemsOf(Btn("fuel_left_qty_box", "", 377, 780, 150, 34, Lbl("fuel_left_qty", "1539", 422, 782, 60)), Lbl("fuel_left_qty_unit", "Gal", 537, 785, 31),
            Btn("total_fuel_weight_box", "", 722, 954, 150, 34, Lbl("total_fuel_weight", "20617", 759, 956, 76)), Lbl("total_fuel_weight_unit", "Lbs", 882, 959, 34));

        Assert.Equal(new[] { (Bbs757EfbKind.Field, "QUANTITY", "1539 Gal"), (Bbs757EfbKind.Field, "WEIGHT", "20617 Lbs") },
            items.Select(i => (i.Kind, i.Text, i.Value)).ToArray());
        Assert.Equal("fuel_left_qty_box", items[0].Key);
    }

    [Fact]
    public void The_weight_and_balance_boxes_are_fields_with_their_unit()
    {
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("12-aircraft-weight-balance"))!.Items.Where(i => i.Kind == Bbs757EfbKind.Field).ToList();

        Assert.Equal(new[] { "2960 Lbs", "29600 Lbs", "6160 Lbs", "0 Lbs" }, items.Select(i => i.Value).ToArray());
        Assert.All(items, i => Assert.Equal("WEIGHT", i.Text));
    }

    [Fact]
    public void A_slider_outside_a_settings_row_is_kept_with_its_value()
    {
        var cabin = ItemsOf(Lbl("cabin_ceiling_name", "CEILING", 1063, 302, 52), Lbl("cabin_ceiling_value", "OFF", 1303, 302, 25),
            N("slider", "cabin_ceiling_slider", "", 1140, 299, 130, 20));
        var ceiling = Assert.Single(cabin);
        Assert.Equal((Bbs757EfbKind.Slider, "CEILING", "OFF", 1140), (ceiling.Kind, ceiling.Text, ceiling.Value, ceiling.Slider!.KnobX));

        var fuel = ItemsOn("FUEL", Lbl("fuel_left_slider_title", "LEFT", 453, 723, 48), N("slider", "fuel_slider_left", "", 377, 755, 200, 20),
            Lbl("fuel_left_tank_label", "70%", 474, 410, 49));
        var slider = Assert.Single(fuel, i => i.Kind == Bbs757EfbKind.Slider);
        Assert.Equal(("LEFT", "70%"), (slider.Text, slider.Value));
        Assert.Equal(new Bbs757EfbSlider(377, 577, 517), slider.Slider); // the knob at 70 % of 200 px
        Assert.Equal(2, fuel.Count);                                     // the title stays its heading; the tank's percent is the slider's
        Assert.Equal(Bbs757EfbKind.Heading, fuel[0].Kind);

        var total = ItemsOn("FUEL", Lbl("total_fuel_label", "TOTAL FUEL", 542, 884, 147), N("slider", "fuel_slider_total", "", 722, 889, 200, 20),
            Lbl("total_fuel_perc_label", "27%", 942, 884, 49));
        var totalSlider = Assert.Single(total, i => i.Kind == Bbs757EfbKind.Slider);
        Assert.Equal(("TOTAL FUEL", "27%", 776), (totalSlider.Text, totalSlider.Value, totalSlider.Slider!.KnobX));
        Assert.Equal((Bbs757EfbKind.Heading, "TOTAL FUEL"), (total[0].Kind, total[0].Text)); // the heading Fuel's order makes from the slider
    }

    [Fact]
    public void A_slider_percent_reads_the_same_under_other_cultures()
    {
        foreach (var culture in new[] { "de-DE", "sv-SE" })
        {
            var slider = TestCulture.Run(culture, () => Assert.Single(ItemsOf(
                Lbl("fuel_left_slider_title", "LEFT"), N("slider", "fuel_slider_left", "", 377, 755, 200, 20), Lbl("fuel_left_tank_label", "70%")),
                i => i.Kind == Bbs757EfbKind.Slider));
            Assert.Equal(517, slider.Slider!.KnobX);
        }
    }

    [Fact]
    public void A_slider_with_no_label_is_named_by_its_control_name()
    {
        var zoom = Assert.Single(ItemsOf(N("slider", "planning_charts_zoom_slider", "", 1165, 202, 154, 42)));
        Assert.Equal("PLANNING CHARTS ZOOM", zoom.Text);
        Assert.Equal(new Bbs757EfbSlider(1165, 1319, 1165), zoom.Slider);
    }

    [Fact]
    public void A_settings_slider_carries_its_track()
    {
        var app = SettingsApp("aircraft_scrollbox", 600,
            N("grid", "loading_speed_grid", "", 250, 842, 1147, 60,
                N("other", "loading_speed_label", "BOARDING & LOAD RATE", 280, 842, 647, 60),
                N("slider", "loading_speed_slider", "", 897, 857, 300, 30)),
            N("grid", "level_grid", "", 250, 900, 1147, 60,
                N("other", "level_label", "LEVEL", 280, 900, 647, 60),
                N("slider", "level_slider", "", 897, 915, 300, 30),
                N("label", "level_value_label", "50%", 1197, 900, 200, 60)));

        var items = Bbs757EfbModelReader.Read(new[] { app })!.Items;

        Assert.Equal(new Bbs757EfbSlider(897, 1197, 897), items.Single(i => i.Text == "BOARDING & LOAD RATE").Slider);
        Assert.Equal(new Bbs757EfbSlider(897, 1197, 1047), items.Single(i => i.Text == "LEVEL").Slider);
    }

    // ---- a slider's place from BlueBird's knob (plan 20, Task 13) ----

    /// <summary>A slider whose knob the memory walk read at <paramref name="place"/> (0 to 1), the knob <paramref name="span"/> px along
    /// its track.</summary>
    private static Node Knobbed(Node slider, float place, float span) => slider with { Knob = place, KnobSpan = span };

    [Fact]
    public void A_slider_with_its_knob_runs_along_the_knobs_center()
    {
        // Measured 2026-11-07: a tap centers the knob on the tap point (Charts' zoom: tap x 1204, knob center 1204.5), so the track a
        // set taps along is the knob center's range, X + kw/2 to X + W - kw/2. Forward cargo's knob at 59.8 %, 12 px wide.
        var cargo = Assert.Single(ItemsOf(Knobbed(N("slider", "wb_baggage_fwd_slider", "", 0, 462, 188, 18) with { X = 1162.5f }, 0.598f, 12)));

        Assert.Equal(new Bbs757EfbSlider(1168, 1344, 1273) { KnobRead = true }, cargo.Slider);
        Assert.Equal(60, Math.Round(cargo.Slider!.PercentOf(cargo.Slider.KnobX)));
    }

    [Fact]
    public void An_upright_slider_with_its_knob_runs_from_its_bottom()
    {
        // Measured 2026-11-07: bottom is 0 (Systems' zoom "+" moves the knob up); the left reservoir's knob at 78.1 %, 12 px tall.
        var reservoir = Assert.Single(ItemsOf(Knobbed(N("slider", "maintenance_hyd_res_left_slider", "", 423, 279, 24, 190), 0.781f, 12)));

        Assert.Equal(new Bbs757EfbSlider(463, 285, 324) { Vertical = true, AcrossX = 435, KnobRead = true }, reservoir.Slider);
    }

    [Fact]
    public void A_value_the_tablet_shows_still_names_a_slider_whose_place_is_its_knobs()
    {
        // Fuel's left tank, measured 2026-11-07: the knob at 34.5 %, the label reading 34%. The label names it; only the place is the knob's.
        var fuel = ItemsOf(Lbl("fuel_left_slider_title", "LEFT", 453, 723, 48),
            Knobbed(N("slider", "fuel_slider_left", "", 377, 755, 200, 20), 0.345f, 12), Lbl("fuel_left_tank_label", "34%", 474, 410, 49));
        var left = Assert.Single(fuel, i => i.Kind == Bbs757EfbKind.Slider);

        Assert.Equal(("LEFT", "34%"), (left.Text, left.Value));
        Assert.Equal(new Bbs757EfbSlider(383, 571, 448) { KnobRead = true }, left.Slider);   // At(34.5); the label's 34 would be 447
    }

    [Fact]
    public void A_settings_slider_with_its_knob_runs_along_the_knobs_center()
    {
        // Settings, Aircraft's Boarding & load rate, measured 2026-11-07: its 15 px knob at the end (its last choice).
        var app = SettingsApp("aircraft_scrollbox", 600,
            N("grid", "loading_speed_grid", "", 250, 842, 1147, 60,
                N("other", "loading_speed_label", "BOARDING & LOAD RATE", 280, 842, 647, 60),
                Knobbed(N("slider", "loading_speed_slider", "", 897, 857, 300, 30), 1f, 15)));

        var rate = Bbs757EfbModelReader.Read(new[] { app })!.Items.Single(i => i.Text == "BOARDING & LOAD RATE");

        Assert.Equal(new Bbs757EfbSlider(904, 1189, 1189) { KnobRead = true }, rate.Slider);
    }

    [Fact]
    public void A_slider_without_a_knob_is_not_marked_as_bluebirds()
    {
        var zoom = Assert.Single(ItemsOf(N("slider", "planning_charts_zoom_slider", "", 1165, 202, 154, 42)));
        Assert.False(zoom.Slider!.KnobRead);
    }

    [Fact]
    public void The_listing_reads_a_sliders_place_from_its_knob()
    {
        // The real pages (captured live with their knobs, 2026-11-07): the knob places a slider (BUSINESS PAX's title count and the
        // reservoirs' missing values name them as before). Without the knob, a slider whose value shows nowhere sits at its start.
        static string Listing(IReadOnlyList<App> apps) =>
            Bbs757EfbListing.Of(Bbs757EfbElements.Build(Bbs757EfbModelReader.Read(apps), null, Array.Empty<Bbs757EfbExtraElement>()));
        var wb = Bbs757EfbTreeJson.Load("12-aircraft-weight-balance");
        var hyd = Bbs757EfbTreeJson.Load("15b-aircraft-maintenance-hydraulic");

        string weights = Listing(wb), reservoirs = Listing(hyd);

        Assert.Contains("slider 60  Forward cargo\n", weights);
        Assert.Contains("slider 100  Business pax (16/16)\n", weights);
        Assert.Contains("slider 78  Maintenance hyd res left\n", reservoirs);
        Assert.Contains("slider 0  Forward cargo\n", Listing(Bbs757EfbTreeJson.WithoutKnobs(wb)));
        Assert.Contains("slider 100  Business pax (16/16)\n", Listing(Bbs757EfbTreeJson.WithoutKnobs(wb)));   // its count places it
        Assert.Contains("slider 0  Maintenance hyd res left\n", Listing(Bbs757EfbTreeJson.WithoutKnobs(hyd)));
    }

    [Fact]
    public void An_icon_button_is_named_from_its_control_name()
    {
        var items = ItemsOf(Btn("btn_pushback_left", "", 723, 960, 56, 56), Btn("btn_x1", "", 800, 960, 56, 56), Btn("btn_5th_row", "", 880, 960, 56, 56));

        var pushback = Assert.Single(items);
        Assert.Equal("Pushback left", pushback.Text, ignoreCase: true);
        Assert.Equal("PUSHBACK LEFT", Bbs757EfbScreen.KeyWords(pushback.Key));
    }

    [Fact]
    public void A_word_less_button_with_a_touching_label_is_named_by_it()
    {
        var item = Assert.Single(ItemsOf(Btn("btn_unknown", "", 300, 200, 56, 56), Lbl("caption", "SPARE", 300, 256, 60)), i => i.Kind == Bbs757EfbKind.Button);
        Assert.Equal("Spare", item.Text, ignoreCase: true);
    }

    [Fact]
    public void The_pushback_arrows_read_by_their_names_on_the_ground_page()
    {
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("13-aircraft-ground-services"))!.Items;
        var pushback = items.Where(i => i.Name.StartsWith("btn_pushback_", StringComparison.Ordinal)).Select(i => i.Text.ToUpperInvariant()).ToList();
        Assert.Equal(new[] { "REQ PB TUG", "REMOVE TUG", "PUSHBACK LEFT", "PUSHBACK STRAIGHT", "PUSHBACK RIGHT" }, pushback);
    }

    [Fact]
    public void A_title_naming_an_unread_input_is_a_field_with_no_value()
    {
        var items = ItemsOf(
            N("other", "planning_perf_calc_flap_field_canvas", "", 479, 216, 229, 84, Lbl("planning_perf_calc_flap_field_title", "Flap Setting"),
                N("other", "planning_perf_calc_flap_dropdown", "", 519, 240, 150, 34)),
            N("other", "planning_perf_calc_length_canvas", "", 709, 132, 229, 84, Lbl("planning_perf_calc_length_title", "Length Type"),
                N("other", "planning_perf_calc_tora_dropdown", "", 749, 156, 150, 34)),
            Lbl("planning_perf_calc_units_title", "Units", 1268, 137, 28));

        Assert.Equal(new[] { (Bbs757EfbKind.Field, "Flap Setting", ""), (Bbs757EfbKind.Field, "Length Type", ""), (Bbs757EfbKind.Heading, "Units", "") },
            items.Select(i => (i.Kind, i.Text, i.Value)).ToArray());
        Assert.Equal("planning_perf_calc_flap_dropdown", items[0].Key);
    }

    [Fact]
    public void Perf_calc_reads_its_inputs_as_fields()
    {
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25-navigation-perf-calc"))!.Items;

        var fields = items.Where(i => i.Kind == Bbs757EfbKind.Field).Select(i => i.Text).ToArray();
        Assert.Equal(new[] { "Airport", "Runway", "Length Type", "Length (FT)", "Takeoff Weight (LB)", "Flap Setting", "Thrust Setting", "Bleed Setting",
            "Anti-ice Setting", "Wind", "Temperature", "Pressure (inHg)", "Surface Condition", "Flex/Assumed Temperature", "Improved Climb Calculations" }, fields);
        Assert.Equal(new[] { "Units", "Weather Source" }, items.Where(i => i.Kind == Bbs757EfbKind.Heading).Select(i => i.Text).ToArray());
    }

    [Fact]
    public void A_labelled_button_reads_label_then_words()
    {
        var door = new Bbs757EfbItem(Bbs757EfbKind.Button, "Closed", default, default, true, "btn:|@36,10", Label: "LEFT FWD");
        var plain = new Bbs757EfbItem(Bbs757EfbKind.Button, "Close all", default, default, true, "btn:|CLOSE ALL@16,11");
        var model = new Bbs757EfbScreenModel("HOME", "", Array.Empty<Bbs757EfbItem>(), new[] { door, plain });

        var live = Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>()).Data;
        Assert.Equal(("Left FWD: Closed", "btn:|@36,10", "Close all"), (live["items.0.text"], live["items.0.key"], live["items.1.text"]));

        var dimmed = Bbs757EfbElements.Build(model with { Question = new Bbs757EfbQuestion("Sure?", new[] { plain with { Text = "YES", Key = "q:yes" } }) },
            null, Array.Empty<Bbs757EfbExtraElement>()).Data;
        Assert.Contains("Left FWD: Closed", dimmed.Where(kv => kv.Key.EndsWith(".text", StringComparison.Ordinal)).Select(kv => kv.Value));
        Assert.Equal("btn:|@36,10", dimmed.Single(kv => kv.Value == "Left FWD: Closed").Key.Replace(".text", ".key") is var k ? dimmed[k] : "");
    }

    [Fact]
    public void A_field_is_built_as_a_value_line_under_every_culture()
    {
        var model = new Bbs757EfbScreenModel("HOME", "", Array.Empty<Bbs757EfbItem>(), new[]
        {
            new Bbs757EfbItem(Bbs757EfbKind.Field, "QUANTITY", default, default, true, "fuel_left_qty_box", Value: "1539,5 Gal"),
            new Bbs757EfbItem(Bbs757EfbKind.Field, "Flap Setting", default, default, true, "planning_perf_calc_flap_dropdown"),
        });
        foreach (var culture in new[] { "en-US", "de-DE", "sv-SE" })
        {
            var texts = TestCulture.Run(culture, () =>
            {
                var data = Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>()).Data;
                return Enumerable.Range(0, 2).Select(i => data[$"items.{i}.text"]).ToArray();
            });
            Assert.Equal(new[] { "Quantity: 1539,5 Gal", "Flap Setting" }, texts);
        }
    }

    // ---- plan 21: text boxes, the flight plan, drop-downs and BlueBird's selected state ----

    private static string ListingOf(IReadOnlyList<App> apps) =>
        Bbs757EfbListing.Of(Bbs757EfbElements.Build(Bbs757EfbModelReader.Read(apps), null, Array.Empty<Bbs757EfbExtraElement>()));

    [Fact]
    public void A_text_box_is_a_field_named_by_its_title()
    {
        // PERF calc's airport box (plan 21, recaptured 2026-11-07, the scrub's KSEA in it): BlueBird's text box class, named by the title
        // naming it as plan 20 named the input it could not read. Its caret not read (the tree predates plan 22), a line with what it
        // holds, as the tablet writes it, not an edit box: only a box whose caret was read is typed into (plan 22, Task 5).
        var apps = Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated");

        var items = Bbs757EfbModelReader.Read(apps)!.Items;

        var airport = Assert.Single(items, i => i.Name == "planning_perf_calc_airport_input");
        Assert.Equal((Bbs757EfbKind.Field, "Airport", "KSEA", "planning_perf_calc_airport_input"), (airport.Kind, airport.Text, airport.Value, airport.Key));
        Assert.DoesNotContain(items, i => i.Name == "planning_perf_calc_airport_title");   // the title is its name, not a heading
        Assert.Contains("button  Landing\ntext  Airport: KSEA\n", ListingOf(apps));
        Assert.DoesNotContain("edit [", ListingOf(apps));

        // An empty one reads its name alone; so does the box the trees captured before plan 21 could not read.
        Assert.Contains("button  Landing\ntext  Airport\n", ListingOf(Bbs757EfbTreeJson.WithText(apps, "planning_perf_calc_airport_input", "")));
        var unread = Assert.Single(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25-navigation-perf-calc"))!.Items,
            i => i.Name == "planning_perf_calc_airport_input");
        Assert.Equal((Bbs757EfbKind.Field, ""), (unread.Kind, unread.Value));
    }

    [Fact]
    public void A_text_box_no_title_names_is_named_by_its_row_or_its_own_name()
    {
        // Charts' request box (recaptured 2026-11-07): no title names it, and the label beside it sharing its name,
        // planning_charts_request_name_label "KINGMAN", is the requested airport's name, a value, not the box's: a text box shows words of
        // its own, so R5's labels name none (the coordinator's ruling of 2026-11-07). Its own name's words name it; KINGMAN reads as itself.
        string charts = ListingOf(Bbs757EfbTreeJson.Load("23-navigation-charts"));
        Assert.Contains("button  Manual (current)\ntext  KINGMAN\ntext  KINGMAN, UNITED STATES\ntext  Planning charts request: KIGM\nbutton  STAR\n", charts);

        // The AIRAC page's code boxes (empty when measured, 2026-11-07; the page was not captured again): their settings row's label,
        // which stays the line it makes with the saved value; empty, the box reads its name alone.
        var airac = Bbs757EfbTreeJson.WithKind(Bbs757EfbTreeJson.WithKind(Bbs757EfbTreeJson.Load("55-settings-airac-data"),
            "hoppie_logon_code_textbox", "textbox", ""), "sayintentions_acars_key_textbox", "textbox", "");
        Assert.Contains("text  Hoppie logon code: ------\ntext  Hoppie logon code\n" +
            "text  Sayintentions ACARS key: ------\ntext  Sayintentions ACARS key\nbutton  Sayintentions ACARS key: Fetch key\n", ListingOf(airac));
    }

    [Fact]
    public void The_operating_fp_is_long_text()
    {
        // The Operating FP is BlueBird's multi-line text box (91,869 characters measured, 2026-11-07; the recapture holds the scrub's few
        // made-up lines): one long text, every line of it in the window's pre block, under its title.
        var apps = Bbs757EfbTreeJson.Load("21-navigation-operating-fp");

        var fp = Assert.Single(Bbs757EfbModelReader.Read(apps)!.Items, i => i.Name == "planning_operating_fp_value");

        Assert.Equal((Bbs757EfbKind.LongText, Bbs757EfbTreeScrub.MadeUpOperatingFp, false), (fp.Kind, fp.Text, fp.Clickable));
        Assert.Contains("heading 2  Operating flight plan\npre\n" + string.Concat(Bbs757EfbTreeScrub.MadeUpOperatingFp.Split('\n').Select(l => "    " + l + "\n")),
            ListingOf(apps));

        // A multi-line box of one short line (the ACARS route, 38 px tall) is long text too: its class says so, not its height.
        Assert.Contains("heading 2  Route\npre\n    KSEA/16L DCT BTG DCT KPDX/28R\n", ListingOf(Bbs757EfbTreeJson.Load("20-navigation-acars-dashboard")));
    }

    [Fact]
    public void A_drop_down_is_a_choice()
    {
        // PERF calc's runway drop-down (recaptured 2026-11-07): its face shows RW07L, the first of its six entries.
        var apps = Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated");

        var runway = Assert.Single(Bbs757EfbModelReader.Read(apps)!.Items, i => i.Name == "planning_perf_calc_runway_dropdown");

        Assert.Equal((Bbs757EfbKind.Choice, "Runway", "RW07L", "planning_perf_calc_runway_dropdown"), (runway.Kind, runway.Text, runway.Value, runway.Key));
        Assert.Equal(new[] { "RW07L", "RW07R", "RW08", "RW25L", "RW25R", "RW26" }, runway.Entries);
        Assert.Contains("text  Airport: KSEA\ncombo [RW07L] {RW07L|RW07R|RW08|RW25L|RW25R|RW26}  Runway\ncombo [FULL] {ASDA|FULL|LDA|Shorten Length|TODA|TORA}  Length Type\n",
            ListingOf(apps));

        // One no title or row names, whose face shows an entry: by its own name's words, as a slider is (Map's provider, Charts' list).
        Assert.StartsWith("title: Navigation, Map\ncombo [IFR Low] {IFR Low|IFR High|VFR|World}  Map provider\nbutton  Night\n",
            ListingOf(Bbs757EfbTreeJson.Load("24-navigation-map")));
        Assert.Contains("combo [AP  AIRPORT, AIRPORT INFO  KIGM109] {AP  AIRPORT, AIRPORT INFO  KIGM109|AA  AIRPORT INFO (CONTD), TAKE-OFF MNMS  KIGM109A}  Planning charts\n",
            ListingOf(Bbs757EfbTreeJson.Load("23-navigation-charts")));
    }

    [Fact]
    public void A_drop_down_with_no_current_entry_has_no_value()
    {
        // Systems' page menu (recaptured 2026-11-07) always shows SELECT, none of its entries: no value, and the window's list starts with
        // an empty choice in its place, so it never reads an entry as the chosen one. Its face is a prompt, and nothing else names it,
        // so the face's word does ("Select"). The system shown, selected_page_title_label, stays the page's heading: a title names an
        // input only in a container holding the two alone (PERF calc's field canvases), and the header holds four controls (the
        // coordinator's ruling of 2026-11-07).
        var apps = Bbs757EfbTreeJson.Load("30-systems");

        var menu = Assert.Single(Bbs757EfbModelReader.Read(apps)!.Items, i => i.Name == "pb_menu1");

        Assert.Equal((Bbs757EfbKind.Choice, "SELECT", ""), (menu.Kind, menu.Text, menu.Value));
        Assert.Equal(new[] { "24 - ELECTRICAL", "28 - FUEL", "29 - HYDRAULICS", "36 - PNEUMATICS" }, menu.Entries);
        Assert.StartsWith("title: Systems\nheading 2  Electrical\ncombo [] {|24 - ELECTRICAL|28 - FUEL|29 - HYDRAULICS|36 - PNEUMATICS}  Select\nbutton  Elec zoom in\n",
            ListingOf(apps));

        // A face showing an entry is its value, never its name: one no title names takes its own name's words.
        var shown = Bbs757EfbTreeJson.AsDropDown(apps, "pb_menu1", "28 - FUEL", "24 - ELECTRICAL", "28 - FUEL");
        Assert.Contains("combo [28 - FUEL] {24 - ELECTRICAL|28 - FUEL}  PB MENU1\n", ListingOf(shown));
    }

    [Fact]
    public void A_drop_down_with_no_entries_read_shows_its_face_as_its_value()
    {
        // Task 5's review, M2: a face is a prompt only beside entries it matches none of. With none read (a torn vector, which the walk
        // reads as none; a list BlueBird has not filled yet) the face is the value and the drop-down keeps its own name: Map's provider
        // reads "Map provider" showing IFR Low, never a drop-down named "IFR Low" with nothing chosen.
        var map = Bbs757EfbTreeJson.AsDropDown(Bbs757EfbTreeJson.Load("24-navigation-map"), "map_provider_dropdown", "IFR Low");

        var provider = Assert.Single(Bbs757EfbModelReader.Read(map)!.Items, i => i.Name == "map_provider_dropdown");

        Assert.Equal((Bbs757EfbKind.Choice, "MAP PROVIDER", "IFR Low"), (provider.Kind, provider.Text, provider.Value));
        Assert.Empty(provider.Entries!);
        Assert.StartsWith("title: Navigation, Map\ncombo [IFR Low] {IFR Low}  Map provider\n", ListingOf(map));

        // Systems' SELECT, with its entries read, stays a prompt.
        Assert.Contains("combo [] {|24 - ELECTRICAL|28 - FUEL|29 - HYDRAULICS|36 - PNEUMATICS}  Select\n", ListingOf(Bbs757EfbTreeJson.Load("30-systems")));
    }

    [Fact]
    public void The_selected_menu_entry_is_the_section()
    {
        // BlueBird's selected state (plan 21, measured 2026-11-07 on three side menus): the entry it marks current is the section and
        // the "(current page)", whatever the page's names say (the Fuel page with MAINTENANCE marked reads Maintenance).
        var fuel = Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import"), "btn_maintenance");
        var model = Bbs757EfbModelReader.Read(fuel)!;
        Assert.Equal("MAINTENANCE", model.Section);
        Assert.Equal(new[] { "menu:MAINTENANCE" }, model.Menu.Where(m => m.Selected).Select(m => m.Key));
        Assert.Contains("button  Maintenance (current page)\n", ListingOf(fuel));

        // Livery options draws its rows in aircraft_stackbox, the stackbox Settings > Aircraft shares, and no name of its own: its
        // selected entry names it, on its own page or another's.
        Assert.Equal("Settings, Livery options",
            Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("51-settings-aircraft"), "btn_livery"))!.PageName);
        Assert.Equal("Settings, General",
            Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("52-settings-livery-options"), "btn_general"))!.PageName);

        // Two entries marked (a torn read): which page shows is not known, so no section and none marked.
        var torn = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"), "btn_maintenance", "btn_fuel"))!;
        Assert.Equal(("", "Aircraft"), (torn.Section, torn.PageName));
        Assert.DoesNotContain(torn.Menu, m => m.Selected);
    }

    [Fact]
    public void The_selected_tab_is_current()
    {
        // PERF calc's TAKE OFF and LANDING have no panel named for either, so no tab read as current before plan 21: BlueBird's selected
        // state marks the one shown (TAKE OFF when recaptured; LANDING when it marks that one).
        var perf = Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated");
        Assert.StartsWith("title: Navigation, PERF calc\nbutton  Take off (current)\nbutton  Landing\n", ListingOf(perf));
        var landing = Bbs757EfbTreeJson.WithSelected(perf, "planning_perf_calc_tab_button_1");
        Assert.Equal(new[] { "planning_perf_calc_tab_button_1" },
            Bbs757EfbModelReader.Read(landing)!.Items.Where(i => i.Kind == Bbs757EfbKind.Tab && i.Selected).Select(i => i.Name));
        Assert.StartsWith("title: Navigation, PERF calc\nbutton  Take off\nbutton  Landing (current)\n", ListingOf(landing));

        // The selected state wins over the content's names: the landing gear page with HYDRAULIC selected reads HYDRAULIC current.
        var hydraulic = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"),
            "maintenance_hydraulic_tab_button"))!;
        Assert.Equal(new[] { "maintenance_hydraulic_tab_button" },
            hydraulic.Items.Where(i => i.Kind == Bbs757EfbKind.Tab && i.Selected).Select(i => i.Name));
    }

    [Fact]
    public void A_moving_door_reads_moving_on_the_live_capture()
    {
        // Ruling 4 of 2026-11-07: the AFT cargo door captured while it moved (its button "MOVING") reads under its name, in its place.
        string listing = ListingOf(Bbs757EfbTreeJson.Load("14b-aircraft-cabin-doors-door-moving"));
        Assert.Contains("button  FWD cargo: Closed\nbutton  AFT cargo: Moving\nbutton  Close all\n", listing);
        Assert.DoesNotContain("AFT CARGO", listing);
    }

    [Fact]
    public void An_image_reads_as_its_picture_did()
    {
        // BlueBird's image class (the chocks' picture) reads as plan 20's "other" did: its path is no line, and the chocks' file still
        // gives its tile the state (R10).
        var apps = Bbs757EfbTreeJson.Load("13b-aircraft-ground-services-chocks-removed");
        var image = Bbs757EfbTreeJson.WithKind(apps, "chocks_status_image", "image", "./data/efb/images/bbs_ldg_gr.png");

        Assert.Equal(ListingOf(apps), ListingOf(image));
        Assert.Contains(Bbs757EfbModelReader.Read(image)!.Items, i => string.Equals(i.Text, "Chocks removed", StringComparison.OrdinalIgnoreCase));
    }

    // ---- plan 21, Task 6: the dialog over the page ----

    private static Bbs757EfbScreenModel ReadCapture(string fixture) =>
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture), Bbs757EfbTreeJson.LoadNotices(fixture))!;

    /// <summary>The dialog app (app_modaldialog) holding <paramref name="page"/>, as the walk returns it while BBOS's dialog byte is 1.</summary>
    private static App DialogApp(Node page) => new(3, Bbs757EfbMemory.DialogClass, new[] { page });

    /// <summary>The defuel question as BlueBird lays it out (measured 2026-11-07 on the stale one, read only): main_grid > colorbox1 >
    /// dialog_grid > message_grid (its icon, its words) and btn_grid (YES, NO).</summary>
    private static Node DefuelDialog() =>
        N("grid", "main_grid", "", 0, 32, 1397, 1016,
            N("other", "colorbox1", "", 318, 415, 760, 250,
                N("grid", "dialog_grid", "", 334, 431, 728, 218,
                    N("grid", "message_grid", "", 334, 431, 728, 158,
                        N("image", "fuel_icon", "./data/efb/icons/bbs_icon_fuel1.png", 350, 478, 64, 64),
                        N("label", "text_label", "Current fuel exceeds the flight plan fuel.\nAre you sure you want to defuel?", 435, 436, 622, 148)),
                    N("grid", "btn_grid", "", 334, 589, 728, 60,
                        N("button", "yes_btn", "YES", 334, 589, 180, 60),
                        N("button", "no_btn", "NO", 882, 589, 180, 60)))));

    [Fact]
    public void A_dialog_reads_its_words_and_answers()
    {
        // The fuel input dialog (11c, captured live 2026-11-07 with BBOS's dialog byte 1): its prompt is its words, its text box a row
        // holding its own text, with the unit BlueBird draws beside it (fuel_input_unit_label shares the box's name) apart from it (plan
        // 22, Task 5: the typing compares and reads back what the box holds), and CANCEL and INSERT under btn_grid its answers, each
        // tapped at the middle of its drawn rectangle. Its icon and its empty error label are no words.
        var m = ReadCapture("11c-aircraft-fuel-input-dialog");

        var q = Assert.IsType<Bbs757EfbQuestion>(m.Question);
        Assert.Equal("Insert desired fuel level (0-2176 Gal)", q.Text);
        var row = Assert.Single(q.Rows);
        Assert.Equal((Bbs757EfbKind.Field, "FUEL", "752", "Gal", "fuel_input_textbox"), (row.Kind, row.Text, row.Value, row.Unit, row.Key));
        Assert.Equal(new[] { ("Cancel", "q:CANCEL"), ("Insert", "q:INSERT") }, q.Buttons.Select(b => (b.Text, b.Key)));
        Assert.All(q.Buttons, b => Assert.Equal((Bbs757EfbKind.Button, true), (b.Kind, b.Clickable)));
        Assert.Equal((new System.Drawing.Rectangle(354, 380, 140, 48), new System.Drawing.Point(424, 404)), (q.Buttons[0].Box, q.Buttons[0].Hit));
        Assert.Equal((new System.Drawing.Rectangle(862, 380, 180, 48), new System.Drawing.Point(952, 404)), (q.Buttons[1].Box, q.Buttons[1].Hit));
        // The page under it is still the Fuel page, and nothing of the dialog is in it.
        Assert.Equal("Aircraft, Fuel", m.PageName);
        Assert.DoesNotContain(m.Items, i => i.Name is "fuel_input_prompt_label" or "fuel_input_textbox" or "fuel_input_unit_label" or "cancel_btn" or "insert_btn");

        // The defuel question's layout: text_label's words (its line break read as a space), YES and NO.
        var defuel = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import").Append(DialogApp(DefuelDialog())).ToList())!.Question!;
        Assert.Equal("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?", defuel.Text);
        Assert.Empty(defuel.Rows);
        Assert.Equal(new[] { ("Yes", "q:YES"), ("No", "q:NO") }, defuel.Buttons.Select(b => (b.Text, b.Key)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_dialog_reads_the_same_under_other_cultures(string culture)
    {
        var q = TestCulture.Run(culture, () => ReadCapture("11c-aircraft-fuel-input-dialog")).Question!;

        Assert.Equal(("Insert desired fuel level (0-2176 Gal)", "752", "Gal"), (q.Text, q.Rows[0].Value, q.Rows[0].Unit));
        Assert.Equal(new[] { "q:CANCEL", "q:INSERT" }, q.Buttons.Select(b => b.Key));
    }

    [Fact]
    public void A_closed_dialog_is_not_a_question()
    {
        // 11b was captured with BlueBird's answered defuel dialog still front in its app (its page and visible bytes kept, measured
        // 2026-11-07): BBOS's dialog byte read 0, so the walk left the dialog app out, and the page reads with no question, its
        // controls pressable.
        var stale = ReadCapture("11b-aircraft-fuel-simbrief-import");
        Assert.Null(stale.Question);
        Assert.Contains("button  Import from SIMBRIEF\n", ListingOf(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import")));

        // The fuel input dialog's capture with the dialog app gone (closed): no question, and its page's button presses again.
        var closed = Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog").Where(a => a.Class != Bbs757EfbMemory.DialogClass).ToList();
        Assert.Null(Bbs757EfbModelReader.Read(closed)!.Question);
        Assert.Contains("button  Import from SIMBRIEF\n", ListingOf(closed));

        // A dialog app with nothing visible in it says nothing.
        Assert.Null(Bbs757EfbModelReader.Read(closed.Append(new App(3, Bbs757EfbMemory.DialogClass, Array.Empty<Node>())).ToList())!.Question);
    }

    [Fact]
    public void The_dialog_reads_first_and_the_status_bar_and_notifications_after_the_page()
    {
        // D1: the question first (its words, its text box, its answers); the page under it, dimmed, as text; then "Status bar" with the
        // time line and KEYBOARD LOCKED, BlueBird's notifications under their heading, newest first; then the sections and Home.
        string listing = Bbs757EfbListing.Of(Bbs757EfbElements.Build(ReadCapture("11c-aircraft-fuel-input-dialog"), null, Array.Empty<Bbs757EfbExtraElement>()));

        Assert.StartsWith("title: Aircraft, Fuel\ntext  Insert desired fuel level (0-2176 Gal)\ntext  Fuel: 752 Gal\nbutton  Cancel\nbutton  Insert\n"
            + "heading 2  Left\ntext  Left: 34%\n", listing);
        Assert.Equal(2, listing.Split('\n').Count(l => l.StartsWith("button  ", StringComparison.Ordinal)));
        int page = listing.IndexOf("text  Import from SIMBRIEF\n", StringComparison.Ordinal);
        int status = listing.IndexOf("heading 2  Status bar\ntext  Sat, 7th Nov 2026 | 23:06L | 05:06Z | OAT 4 C\ntext  KEYBOARD LOCKED\n"
            + "heading 2  Notifications\ntext  05:06Z AFT cargo door closed.\ntext  05:05Z AFT cargo door opened.\ntext  03:16Z AFT cargo door closed.\n",
            StringComparison.Ordinal);
        int sections = listing.IndexOf("heading 2  Sections\n", StringComparison.Ordinal);
        Assert.True(page > 0 && status > page && sections > status, listing);

        // A page with no dialog: its own content, then the status bar and the notifications, then the sections.
        string ground = Bbs757EfbListing.Of(Bbs757EfbElements.Build(ReadCapture("13-aircraft-ground-services"), null, Array.Empty<Bbs757EfbExtraElement>()));
        Assert.Contains("heading 2  Status bar\ntext  Sat, 7th Nov 2026 | 23:06L | 05:06Z | OAT 4 C\nheading 2  Notifications\n"
            + "text  05:06Z Chocks attached.\ntext  05:06Z AFT cargo door closed.\n", ground);
        Assert.True(ground.IndexOf("heading 2  Status bar", StringComparison.Ordinal) < ground.IndexOf("heading 2  Sections", StringComparison.Ordinal));
    }

    // ---- R9 order, R12 the keyboard and the viewport -----------------------------

    [Fact]
    public void Weight_and_balance_reads_its_top_row_first()
    {
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("12-aircraft-weight-balance"))!.Items;

        Assert.Equal(new[] { "EMPTY", "ZFW", "ZFW CG", "BLK FUEL", "TAKE-OFF", "LANDING" }, items.Take(6).Select(i => i.Text).ToArray());
        // Everything else keeps tree order: the pax and cargo cards, then the gross weight and center of gravity drawn beside them. The
        // business pax title names its slider ("BUSINESS PAX", its count "16/16" the slider's value) and is no heading of its own.
        Assert.Equal((Bbs757EfbKind.Slider, "BUSINESS PAX", "16/16"), (items[6].Kind, items[6].Text, items[6].Value));
        var rest = items.Skip(6).Select(i => i.Text).ToList();
        Assert.True(rest.IndexOf("AFT CARGO") < rest.IndexOf("GROSS WEIGHT"));
        Assert.True(rest.IndexOf("GROSS WEIGHT") < rest.IndexOf("CENTER OF GRAVITY"));
    }

    [Fact]
    public void Fuel_reads_tank_by_tank()
    {
        // The Fuel page captured before plan 21 (no selected state, kept for its knob-less sliders), with FUEL marked as a live read
        // marks it: the section gives the order.
        var items = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("11-aircraft-fuel"), "btn_fuel"))!.Items;

        Assert.Equal(new[]
        {
            (Bbs757EfbKind.Heading, "LEFT", "fuel_left_slider_title"), (Bbs757EfbKind.Slider, "LEFT", "fuel_slider_left"),
            (Bbs757EfbKind.Field, "QUANTITY", "fuel_left_qty_box"), (Bbs757EfbKind.Field, "WEIGHT", "fuel_left_weight_box"),
            (Bbs757EfbKind.Heading, "CENTER", "fuel_center_slider_title"), (Bbs757EfbKind.Slider, "CENTER", "fuel_slider_center"),
            (Bbs757EfbKind.Field, "QUANTITY", "fuel_center_qty_box"), (Bbs757EfbKind.Field, "WEIGHT", "fuel_center_weight_box"),
            (Bbs757EfbKind.Heading, "RIGHT", "fuel_right_slider_title"), (Bbs757EfbKind.Slider, "RIGHT", "fuel_slider_right"),
            (Bbs757EfbKind.Field, "QUANTITY", "fuel_right_qty_box"), (Bbs757EfbKind.Field, "WEIGHT", "fuel_right_weight_box"),
            (Bbs757EfbKind.Heading, "TOTAL FUEL", "total_fuel_label"), (Bbs757EfbKind.Slider, "TOTAL FUEL", "fuel_slider_total"),
            (Bbs757EfbKind.Field, "QUANTITY", "total_fuel_qty_box"), (Bbs757EfbKind.Field, "WEIGHT", "total_fuel_weight_box"),
        }, items.Select(i => (i.Kind, i.Text, i.Name)).ToArray());
        Assert.All(items.Where(i => i.Kind == Bbs757EfbKind.Heading), h => Assert.Equal(2, h.Level));
    }

    [Fact]
    public void The_keyboard_keys_are_read_and_never_an_item()
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25-navigation-perf-calc"))!;

        var keyboard = Assert.IsType<Bbs757EfbKeyboard>(model.Keyboard);
        Assert.Equal(648, keyboard.Box.Top);
        Assert.Equal(1048, keyboard.Box.Bottom);
        var letters = keyboard.Keys.Where(k => k.Name.StartsWith("char_btn_", StringComparison.Ordinal)).ToList();
        Assert.Equal(26, letters.Count);
        var q = keyboard.Keys.Single(k => k.Name == "char_btn_q|Q|1");
        Assert.Equal(("q", "Q", "1", new System.Drawing.Point(203, 750)), (q.Lower, q.Upper, q.Third, q.Hit));
        var comma = keyboard.Keys.Single(k => k.Name == "btn_exclamation");
        Assert.Equal((",", ",", "!"), (comma.Lower, comma.Upper, comma.Third));
        var period = keyboard.Keys.Single(k => k.Name == "btn_question");
        Assert.Equal((".", ".", "?"), (period.Lower, period.Upper, period.Third));
        var enter = keyboard.Keys.Single(k => k.Name == "btn_enter");
        Assert.Equal(("", "", ""), (enter.Lower, enter.Upper, enter.Third));
        Assert.Contains(keyboard.Keys, k => k.Name == "btn_keyboard_down");
        Assert.DoesNotContain(model.Items.Concat(model.Menu), i => i.Key.StartsWith("char_btn_", StringComparison.Ordinal)
            || i.Name is "btn_enter" or "btn_keyboard_down" or "kb_grid");
    }

    [Fact]
    public void No_kb_grid_reads_as_null()
    {
        var apps = Bbs757EfbTreeJson.Load("25-navigation-perf-calc")
            .Where(a => a.Class != "app").ToList();
        Assert.Null(Bbs757EfbModelReader.Read(apps)!.Keyboard);
    }

    // ---- plan 22, Task 1b: the keyboard's state and a text box's caret ----

    /// <summary>The Airport box tapped and the keyboard settled, as the walk reads the keyboard app (measured 2026-11-07,
    /// [B757-23]): up, ready, the QWERTY keyboard, typing into the Airport box, the one-shot shift on.</summary>
    private static readonly Bbs757EfbMemory.KeyboardState AirportTyping =
        new(true, true, false, "planning_perf_calc_airport_input", false, true, false, 595.5f, 348, 360, 430);

    [Fact]
    public void The_keyboard_app_of_its_own_class_gives_the_keys_as_before()
    {
        // The layout knows the keyboard app's class now (N3bbs12app_keyboardE), so the walk reads it as "keyboard", no longer the fallback
        // "app": its kb_grid gives the same keys, and a tree captured before (class "app") still gives its own.
        var before = Bbs757EfbTreeJson.Load("25-navigation-perf-calc");
        var now = Bbs757EfbTreeJson.WithKeyboard(before, null);
        Assert.Contains(now, a => a.Class == Bbs757EfbMemory.KeyboardClass && a.Key == 2);
        Assert.DoesNotContain(now, a => a.Class == "app");
        var old = Bbs757EfbModelReader.Read(before)!.Keyboard!;
        var read = Bbs757EfbModelReader.Read(now)!.Keyboard!;
        Assert.Equal(old.Box, read.Box);
        Assert.Equal(old.Keys, read.Keys);
        Assert.True(read.Keys.Count > 26, $"{read.Keys.Count} keys");
    }

    [Fact]
    public void The_model_carries_the_keyboards_state()
    {
        var page = Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated");
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithKeyboard(page, AirportTyping))!;
        Assert.Equal(new Bbs757EfbKeyboardState(true, true, false, "planning_perf_calc_airport_input", false, true, false,
            new System.Drawing.Rectangle(595, 348, 360, 430)), model.KeyboardState);

        // Down: no target. None read (a tree captured before plan 22, or a keyboard app whose bytes were torn): none.
        var down = new Bbs757EfbMemory.KeyboardState(false, false, false, "", false, false, false, 595.5f, 348, 360, 430);
        Assert.Equal((false, false, ""), Down(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithKeyboard(page, down))!.KeyboardState!));
        Assert.Null(Bbs757EfbModelReader.Read(page)!.KeyboardState);
        Assert.Null(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithKeyboard(page, null))!.KeyboardState);

        // A keypad rectangle that is no number, or lies far off the tablet, is no box; the rest of the state still reads.
        foreach (var torn in new[] { AirportTyping with { KeypadX = float.NaN }, AirportTyping with { KeypadH = float.PositiveInfinity },
                     AirportTyping with { KeypadW = -360 }, AirportTyping with { KeypadY = 1e9f },
                     // The walk's torn rectangle (all four 0), and a width or height of 0: no keypad, as a notice panel of no size.
                     AirportTyping with { KeypadX = 0, KeypadY = 0, KeypadW = 0, KeypadH = 0 },
                     AirportTyping with { KeypadW = 0 }, AirportTyping with { KeypadH = 0 }, AirportTyping with { KeypadH = -430 } })
        {
            var state = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithKeyboard(page, torn))!.KeyboardState!;
            Assert.Equal((System.Drawing.Rectangle.Empty, true, "planning_perf_calc_airport_input"), (state.KeypadBox, state.Ready, state.Target));
        }

        // Nothing is said or shown differently: the page reads as it did without the state.
        Assert.Equal(Bbs757EfbPress.Fingerprint(Bbs757EfbModelReader.Read(page)!), Bbs757EfbPress.Fingerprint(model));
        Assert.Equal(ListingOf(page), ListingOf(Bbs757EfbTreeJson.WithKeyboard(page, AirportTyping)));

        static (bool, bool, string) Down(Bbs757EfbKeyboardState s) => (s.Shown, s.Ready, s.Target);
    }

    [Fact]
    public void A_text_box_field_carries_its_caret_maximum_length_and_keypad_flag()
    {
        // PERF calc's Airport box ("KSEA" in the scrubbed tree), the caret tapped to 2, its maximum length 4, the QWERTY keyboard.
        var page = Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated");
        var apps = Bbs757EfbTreeJson.WithTextState(page, "planning_perf_calc_airport_input", 2, 4, false);
        var model = Bbs757EfbModelReader.Read(apps)!;
        var airport = Assert.Single(model.Items, i => i.Name == "planning_perf_calc_airport_input");
        Assert.Equal((Bbs757EfbKind.Field, "KSEA"), (airport.Kind, airport.Value));
        Assert.Equal(new Bbs757EfbTextState(2, 4, false), airport.TextState);

        // The other text boxes carry none (their nodes have none), nor does any item that is no text box.
        Assert.All(model.Items.Concat(model.Menu).Concat(model.TopBar).Where(i => i.Name != "planning_perf_calc_airport_input"),
            i => Assert.Null(i.TextState));

        // A node missing any one of the three gives none: the state is all three or nothing.
        foreach (var (caret, max, keypad) in new (int?, int?, bool?)[] { (null, 4, false), (2, null, false), (2, 4, null) })
        {
            var partial = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithTextState(page, "planning_perf_calc_airport_input", caret, max, keypad))!;
            Assert.Null(Assert.Single(partial.Items, i => i.Name == "planning_perf_calc_airport_input").TextState);
        }

        // The window shows the box as an edit box (Task 5), the rest as before, so a read that gains the state is a new page to push (the
        // final review's M4); the caret moving, as it does while the pilot types, is none.
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(Bbs757EfbModelReader.Read(page)!), Bbs757EfbPress.Fingerprint(model));
        Assert.Equal(Bbs757EfbPress.Fingerprint(model),
            Bbs757EfbPress.Fingerprint(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithTextState(page, "planning_perf_calc_airport_input", 4, 4, false))!));
        Assert.Equal(ListingOf(page).Replace("text  Airport: KSEA\n", "edit [KSEA]  Airport\n", StringComparison.Ordinal), ListingOf(apps));
        Assert.NotEqual(ListingOf(page), ListingOf(apps));
    }

    [Fact]
    public void A_questions_text_box_carries_its_caret_maximum_length_and_keypad_flag()
    {
        // The fuel input dialog's box, pre-filled "752" with the caret at its end, its maximum length 8, opening the numeric keypad.
        var page = Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog");
        var apps = Bbs757EfbTreeJson.WithTextState(page, "fuel_input_textbox", 3, 8, true);
        var row = Assert.Single(Bbs757EfbModelReader.Read(apps)!.Question!.Rows);
        Assert.Equal(("fuel_input_textbox", "752", "Gal"), (row.Name, row.Value, row.Unit));
        Assert.Equal(new Bbs757EfbTextState(3, 8, true), row.TextState);
        Assert.Null(Assert.Single(Bbs757EfbModelReader.Read(page)!.Question!.Rows).TextState);
        // An edit box in the window, a line without the state: the read that gains it is pushed (the final review's M4).
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(Bbs757EfbModelReader.Read(page)!), Bbs757EfbPress.Fingerprint(Bbs757EfbModelReader.Read(apps)!));
    }

    // ---- a toggle's own state (plan 22, Task 1c; measured 2026-11-07) ----

    private static string LineOf(string listing, string word) =>
        Assert.Single(listing.Split('\n'), l => l.Contains(word, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_toggle_with_no_value_label_reads_its_own_on_byte()
    {
        // Settings > Aircraft's TOD pause row has no value label: the window said "not checked" while it was on. BlueBird's byte on the
        // toggle (+0x188) is its state; without one read (every committed tree) the row reads as before.
        var apps = Bbs757EfbTreeJson.Load("51-settings-aircraft");
        var before = Assert.Single(Bbs757EfbModelReader.Read(apps)!.Items, i => i.Key == "tod_pause_toggle");
        Assert.Null(before.On);
        Assert.Contains("checkbox not checked", LineOf(ListingOf(apps), "TOD PAUSE"));

        var on = Bbs757EfbTreeJson.WithToggleOn(apps, "tod_pause_toggle", true);
        var row = Assert.Single(Bbs757EfbModelReader.Read(on)!.Items, i => i.Key == "tod_pause_toggle");
        Assert.Equal(true, row.On);
        Assert.Contains("checkbox checked", LineOf(ListingOf(on), "TOD PAUSE"));
        Assert.DoesNotContain("not checked", LineOf(ListingOf(on), "TOD PAUSE"));

        var off = Bbs757EfbTreeJson.WithToggleOn(apps, "tod_pause_toggle", false);
        Assert.Equal(false, Assert.Single(Bbs757EfbModelReader.Read(off)!.Items, i => i.Key == "tod_pause_toggle").On);
        Assert.Contains("checkbox not checked", LineOf(ListingOf(off), "TOD PAUSE"));
    }

    [Fact]
    public void A_toggles_own_byte_wins_over_its_value_label()
    {
        // The label is a picture of BlueBird's state: when they disagree the byte is read, in the row (a value label ON) and in the
        // toggle that stands alone (its own text). Settings > Aircraft's first value label reads ON or OFF on the committed tree.
        var apps = Bbs757EfbTreeJson.Load("51-settings-aircraft");
        var toggles = Bbs757EfbModelReader.Read(apps)!.Items.Where(i => i.Kind == Bbs757EfbKind.Toggle && i.On is not null).ToList();
        Assert.NotEmpty(toggles);
        foreach (var t in toggles)
        {
            var flipped = Bbs757EfbTreeJson.WithToggleOn(apps, t.Key, t.On != true);
            var item = Assert.Single(Bbs757EfbModelReader.Read(flipped)!.Items, i => i.Key == t.Key);
            Assert.Equal(t.On != true, item.On);
            Assert.Equal(t.Value, item.Value); // the label's words still read as they are
        }
        // Standing alone (in a stack, no settings row): a toggle with its text "OFF" and the byte on.
        var alone = new Bbs757EfbMemory.Node("toggle", "lone_toggle", "OFF", 300, 300, 60, 35, 0, Array.Empty<Bbs757EfbMemory.Node>(), On: true);
        var app = new Bbs757EfbMemory.App(7, "SYSTEMS", new[]
        {
            new Bbs757EfbMemory.Node("stack", "g", "", 250, 82, 1147, 966, 0, new[] { alone }),
        });
        Assert.Equal(true, Assert.Single(Bbs757EfbModelReader.Read(new[] { app })!.Items, i => i.Key == "lone_toggle").On);
        var noByte = app with { Controls = new[] { app.Controls[0] with { Children = new[] { alone with { On = null } } } } };
        Assert.Equal(false, Assert.Single(Bbs757EfbModelReader.Read(new[] { noByte })!.Items, i => i.Key == "lone_toggle").On);
    }

    [Fact]
    public void The_viewport_is_the_content_scrollbox()
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        Assert.Equal(new System.Drawing.Rectangle(250, 82, 1147, 966), model.Viewport);

        // No scrollbox on the page: the whole content area.
        var fuel = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11-aircraft-fuel"))!;
        Assert.Equal(Bbs757EfbScreen.ContentArea, fuel.Viewport);

        // Several scrollboxes: the page's is the one wrapping a taller stackbox, whatever its size; the larger one holding only a text
        // makes nothing scroll. Scrollable and the viewport name the same box.
        var small = N("scrollbox", "side_scrollbox", "", 260, 100, 300, 400, N("other", "side_stackbox", "", 260, 100, 300, 900));
        var big = N("scrollbox", "main_scrollbox", "", 300, 90, 900, 800);
        var label = N("label", "x_label", "Hello", 320, 100, 80, 20);
        var app = new Bbs757EfbMemory.App(7, "SYSTEMS", new[] { N("grid", "g", "", 250, 82, 1147, 966, label, big, small) });
        var both = Bbs757EfbModelReader.Read(new[] { app })!;
        Assert.True(both.Scrollable);
        Assert.Equal(new System.Drawing.Rectangle(260, 100, 300, 400), both.Viewport);

        // An inner scrollbox and no taller stackbox (the ACARS route, Weather's report, the checklist's lines): the content area.
        foreach (var page in new[] { "20-navigation-acars-dashboard", "22-navigation-weather", "40-checklist" })
        {
            var m = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(page))!;
            Assert.False(m.Scrollable, page);
            Assert.Equal(Bbs757EfbScreen.ContentArea, m.Viewport);
        }
    }

    [Fact]
    public void Each_control_is_clipped_to_the_scrollbox_it_sits_in()
    {
        var acars = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("20-navigation-acars-dashboard"))!;
        // A control in no scrollbox: the page area, the content area with the app's title row above it (plan 21, Task 7).
        Assert.Equal(Bbs757EfbScreen.PageArea, acars.Items.Single(i => i.Name == "planning_acars_fetch_button").Clip);
        var route = acars.Items.Single(i => i.Name == "planning_acars_route_value");
        Assert.Equal(new System.Drawing.Rectangle(323, 820, 1000, 120), route.Clip);   // planning_acars_route_scrollbox

        var settings = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        Assert.All(settings.Items, i => Assert.Equal(settings.Viewport, i.Clip));
    }
}
