using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 18, Task 5: the press pipeline's pure decisions (<see cref="Bbs757EfbPress"/>): finding a pressed control again in a fresh
/// read of the tablet, whether a switch needs a tap, where to tap a slider, and what is said after a press. Models are built in
/// memory with the keys and boxes the reader gives (dumped from the 2026-10-29 captures); a few tests read the captures themselves.
/// Everything spoken is checked under en-US, de-DE and sv-SE (CI runs under en-US).
/// </summary>
public class Bbs757EfbPressTests
{
    private const string Ellipsis = "…";

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    // ---- models --------------------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Item(Bbs757EfbKind kind, string text, string key, Rectangle box, bool? on = null, string value = "", bool selected = false) =>
        new(kind, text, box, new Point(box.Left + box.Width / 2, box.Top + box.Height / 2), true, key, On: on, Selected: selected, Value: value);

    private static Bbs757EfbItem Tile(string text, string key, Rectangle box) => Item(Bbs757EfbKind.Button, text, key, box);

    private static Bbs757EfbScreenModel PageOf(string app, string section, params Bbs757EfbItem[] items) =>
        new(app, section, Array.Empty<Bbs757EfbItem>(), items);

    private static Bbs757EfbScreenModel Ground(params Bbs757EfbItem[] items) => PageOf("AIRCRAFT", "GROUND SERVICES", items);

    // The Ground services tiles as the reader gives them on the 2026-10-29 captures (bright/18, in MSFS mode).
    private static Bbs757EfbItem ReqFuelTruck() => Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1132, 577, 196, 42));
    private static Bbs757EfbItem ReqCatering() => Tile("Req catering", "btn:|REQ CATERING@45,36", new Rectangle(912, 723, 196, 42));
    private static Bbs757EfbItem Requesting() =>
        Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@56,28", new Rectangle(1131, 576, 197, 43));

    private static Bbs757EfbItem StartBoarding() =>
        Item(Bbs757EfbKind.Button, "Start boarding", Bbs757EfbTopBar.BoardingKey, new Rectangle(1142, 3, 180, 28));

    private static Bbs757EfbItem Boarding() =>
        new(Bbs757EfbKind.Text, "Boarding" + Ellipsis + " 10 min", new Rectangle(1142, 3, 180, 28), new Point(1232, 17), false, Bbs757EfbTopBar.BoardingKey);

    private static Bbs757EfbItem Notifications() =>
        Item(Bbs757EfbKind.Button, "Notifications", Bbs757EfbTopBar.NotificationsKey, new Rectangle(1364, 6, 19, 19));

    private static readonly Bbs757EfbNotice GpuConnected = new(1, "GPU connected.", "14:17Z");

    private static Bbs757EfbItem ItemNamed(Bbs757EfbScreenModel m, string text) =>
        m.Menu.Concat(m.Items).Concat(m.TopBar).FirstOrDefault(i => i.Text == text)
        ?? throw new Xunit.Sdk.XunitException($"No control '{text}' on {m.PageName}: " + string.Join(" | ", m.Items.Select(i => i.Text)));

    // ---- TapPoint and DragFor ------------------------------------------------------------------------------------------

    private static Bbs757EfbScreenModel InViewport(params Bbs757EfbItem[] items) =>
        PageOf("SETTINGS", "AIRCRAFT", items) with { Viewport = new Rectangle(250, 82, 1147, 966), Scrollable = true };

    [Fact]
    public void TapPoint_is_the_middle_of_the_shown_part_and_null_under_six_pixels()
    {
        var whole = Item(Bbs757EfbKind.Button, "Whole", "btn:W", new Rectangle(300, 200, 400, 60));
        var last = Item(Bbs757EfbKind.Button, "Last", "btn:L", new Rectangle(273, 1027, 600, 60));      // shows 1027-1048: 21 px
        var strip = Item(Bbs757EfbKind.Button, "Strip", "btn:S", new Rectangle(273, 1040, 600, 60));    // 8 px
        var edge = Item(Bbs757EfbKind.Button, "Edge", "btn:E", new Rectangle(273, 1043, 600, 60));      // 5 px
        var below = Item(Bbs757EfbKind.Button, "Below", "btn:B", new Rectangle(273, 1202, 600, 60));
        var cut = Item(Bbs757EfbKind.Button, "Cut", "btn:C", new Rectangle(273, 1000, 600, 60));        // 48 px, its middle (1030) shows
        var m = InViewport(whole, last, strip, edge, below, cut);

        Assert.Equal(whole.Hit, Bbs757EfbPress.TapPoint(m, whole));
        Assert.Equal(new Point(573, 1037), Bbs757EfbPress.TapPoint(m, last));
        Assert.Equal(new Point(573, 1044), Bbs757EfbPress.TapPoint(m, strip));
        Assert.Null(Bbs757EfbPress.TapPoint(m, edge));
        Assert.Null(Bbs757EfbPress.TapPoint(m, below));
        Assert.Equal(new Point(573, 1024), Bbs757EfbPress.TapPoint(m, cut));   // the middle of the strip, not the box's middle near its edge
    }

    [Fact]
    public void TapPoint_clips_a_control_to_its_own_scrollbox_not_to_the_pages()
    {
        // A control in an inner scrollbox (Weather's report box at 292-1022) is clipped to it; one outside it to the content area.
        var inner = new Rectangle(344, 292, 960, 730);
        var tab = Item(Bbs757EfbKind.Tab, "Metar", "tab:METAR", new Rectangle(324, 250, 250, 34)) with { Clip = Bbs757EfbScreen.ContentArea };
        var inside = Item(Bbs757EfbKind.Button, "Inside", "btn:I", new Rectangle(400, 270, 100, 40)) with { Clip = inner };   // 18 px show
        var hidden = Item(Bbs757EfbKind.Button, "Hidden", "btn:H", new Rectangle(400, 250, 100, 40)) with { Clip = inner };   // none shows
        var m = PageOf("NAVIGATION", "WEATHER & NOTAM", tab, inside, hidden);

        Assert.Equal(tab.Hit, Bbs757EfbPress.TapPoint(m, tab));
        Assert.Equal(new Point(450, 301), Bbs757EfbPress.TapPoint(m, inside));
        Assert.Null(Bbs757EfbPress.TapPoint(m, hidden));
        Assert.Null(Bbs757EfbPress.DragFor(m, hidden));   // the page does not scroll
    }

    [Fact]
    public void DragFor_drags_only_a_control_of_the_scrollbox_that_scrolls()
    {
        var other = new Rectangle(324, 820, 1000, 120);
        var row = Item(Bbs757EfbKind.Button, "Row", "btn:R", new Rectangle(273, 1202, 600, 60));                         // the viewport's
        var boxed = Item(Bbs757EfbKind.Button, "Boxed", "btn:X", new Rectangle(400, 960, 100, 40)) with { Clip = other }; // another box's
        var m = InViewport(row, boxed);

        Assert.NotNull(Bbs757EfbPress.DragFor(m, row));
        Assert.Null(Bbs757EfbPress.TapPoint(m, boxed));
        Assert.Null(Bbs757EfbPress.DragFor(m, boxed));
    }

    [Fact]
    public void TapPoint_leaves_the_side_menu_and_the_top_bar_where_the_reader_found_them()
    {
        var menu = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var m = new Bbs757EfbScreenModel("AIRCRAFT", "FUEL", new[] { menu }, Array.Empty<Bbs757EfbItem>());

        Assert.Equal(menu.Hit, Bbs757EfbPress.TapPoint(m, menu));
        Assert.Null(Bbs757EfbPress.DragFor(m, menu));
    }

    [Fact]
    public void DragFor_moves_the_drawn_middle_to_the_viewports_middle_in_one_drag_of_at_most_height_less_200()
    {
        var below = Item(Bbs757EfbKind.Button, "Below", "btn:B", new Rectangle(273, 1202, 600, 60));   // middle 1232; viewport middle 565
        var above = Item(Bbs757EfbKind.Button, "Above", "btn:A", new Rectangle(273, -40, 600, 60));    // middle -10
        var near = Item(Bbs757EfbKind.Button, "Near", "btn:N", new Rectangle(273, 1000, 600, 60));     // shows 48 px: in view
        var barely = Item(Bbs757EfbKind.Button, "Barely", "btn:R", new Rectangle(273, 1043, 600, 60)); // shows 5 px: 508 to go
        var far = Item(Bbs757EfbKind.Button, "Far", "btn:F", new Rectangle(273, 3000, 600, 60));       // 2465 to go: capped
        var m = InViewport(below, above, near, barely, far);

        var down = Bbs757EfbPress.DragFor(m, below)!.Value;
        Assert.Equal(500, down.X);
        Assert.Equal(667, down.Y0 - down.Y1);              // 1232 - 565, under the 766 cap: y0 below y1, the content moves up
        var up = Bbs757EfbPress.DragFor(m, above)!.Value;
        Assert.Equal(-575, up.Y0 - up.Y1);                 // the other way: y0 above y1
        Assert.Null(Bbs757EfbPress.DragFor(m, near));
        var small = Bbs757EfbPress.DragFor(m, barely)!.Value;
        Assert.Equal(508, small.Y0 - small.Y1);            // 1073 - 565
        var capped = Bbs757EfbPress.DragFor(m, far)!.Value;
        Assert.Equal(966 - 200, capped.Y0 - capped.Y1);
        foreach (var d in new[] { down, up, small, capped })
        {
            Assert.InRange(d.Y0, 82, 1048);
            Assert.InRange(d.Y1, 82, 1048);
        }
    }

    // ---- an open drop-down's list (plan 21, Task 7) -------------------------------------------------------------------

    /// <summary>The runway list as captured open (25c, 2026-11-07): its scrollbox at 519,189 150x228, six rows 38 px apart from 189.</summary>
    private static readonly Rectangle RunwayList = new(519, 189, 150, 228);

    private static Rectangle RunwayRow(int index, int scroll = 0) => new(519, 189 + 38 * index - scroll, 150, 38);

    [Fact]
    public void An_open_lists_row_is_tapped_at_its_middle_and_a_strip_of_it_only_from_six_pixels()
    {
        Assert.True(Bbs757EfbPress.RowShowsWhole(RunwayList, RunwayRow(5)));                     // 379-417: the list's bottom is 417
        Assert.Equal(new Point(594, 398), Bbs757EfbPress.RowTapPoint(RunwayList, RunwayRow(5)));
        Assert.Equal(new Point(594, 208), Bbs757EfbPress.RowTapPoint(RunwayList, RunwayRow(0)));

        var view = RunwayList with { Height = 152 };                                              // four rows show: 189-341
        Assert.False(Bbs757EfbPress.RowShowsWhole(view, RunwayRow(4)));                           // 341-379: none of it shows
        Assert.Null(Bbs757EfbPress.RowTapPoint(view, RunwayRow(4)));
        Assert.False(Bbs757EfbPress.RowShowsWhole(view, RunwayRow(4, scroll: 20)));               // 321-359: 20 px show
        Assert.Equal(new Point(594, 331), Bbs757EfbPress.RowTapPoint(view, RunwayRow(4, scroll: 20)));
        Assert.Null(Bbs757EfbPress.RowTapPoint(view, RunwayRow(4, scroll: 5)));                   // 336-374: 5 px show
        Assert.Equal(new Point(594, 192), Bbs757EfbPress.RowTapPoint(view, RunwayRow(0, scroll: 32)));   // 157-195: 6 px show
    }

    [Fact]
    public void The_surface_lists_rows_are_tapped_in_the_strip_that_shows_and_none_past_its_box()
    {
        // Measured live 2026-11-07 (the coordinator's "Task 7 live"): the Surface list box [977.9,357.2 150x180] shows four and a half of
        // its six 130 px rows. Snow's 28 px strip (509.2-537.2) took a tap and chose Snow; a tap on Wet's row below the box closed the list
        // without choosing (rows past the box are clipped). An open list scrolls by no input, so no row is ever dragged into view.
        var view = new Rectangle(977, 357, 150, 180);   // as the reader boxes it

        Assert.Equal(new Point(1042, 376), Bbs757EfbPress.RowTapPoint(view, new Rectangle(977, 357, 130, 38)));   // Dry, whole
        Assert.Equal(new Point(1042, 523), Bbs757EfbPress.RowTapPoint(view, new Rectangle(977, 509, 130, 38)));   // Snow, 509-537 shows
        Assert.Null(Bbs757EfbPress.RowTapPoint(view, new Rectangle(977, 547, 130, 38)));                          // Wet, none shows
    }

    [Fact]
    public void An_open_list_is_read_from_the_capture_and_its_rows_are_its_entries()
    {
        var open = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25c-navigation-perf-calc-runway-open"))!;
        var runway = Assert.Single(open.Items, i => i.Name == "planning_perf_calc_runway_dropdown");

        Assert.True(runway.Open);
        Assert.Equal(RunwayList, Bbs757EfbPress.ListViewOf(runway));
        Assert.Equal(runway.Entries, runway.List!.Rows.Select(r => r.Text));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => RunwayRow(i)), runway.List.Rows.Select(r => r.Box));
        Assert.Equal(RunwayRow(5), Bbs757EfbPress.RowOf(runway, "RW26")!.Box);
        Assert.Null(Bbs757EfbPress.RowOf(runway, "RW99"));

        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"))!;
        var shut = Assert.Single(closed.Items, i => i.Name == "planning_perf_calc_runway_dropdown");
        Assert.False(shut.Open);
        Assert.Null(shut.List);
        Assert.Null(Bbs757EfbPress.ListViewOf(shut));
        Assert.Null(Bbs757EfbPress.RowOf(shut, "RW26"));
    }

    [Fact]
    public void A_control_under_another_drop_downs_open_list_is_covered()
    {
        // 25c: the runway list (519-669, 189-417) covers the Flap Setting drop-down's face (519,240 150x34); the Thrust Setting drop-down
        // beside it (748,240) and the runway drop-down's own face (above its list) are not covered.
        var open = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25c-navigation-perf-calc-runway-open"))!;
        var runway = open.Items.Single(i => i.Name == "planning_perf_calc_runway_dropdown");
        var flap = open.Items.Single(i => i.Name == "planning_perf_calc_flap_dropdown");
        var thrust = open.Items.Single(i => i.Name == "planning_perf_calc_thrust_dropdown");

        Assert.Same(runway, Bbs757EfbPress.CoveringList(open, flap, Bbs757EfbPress.TapPoint(open, flap)!.Value));
        Assert.Null(Bbs757EfbPress.CoveringList(open, thrust, Bbs757EfbPress.TapPoint(open, thrust)!.Value));
        Assert.Null(Bbs757EfbPress.CoveringList(open, runway, Bbs757EfbPress.TapPoint(open, runway)!.Value));

        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"))!;
        var shutFlap = closed.Items.Single(i => i.Name == "planning_perf_calc_flap_dropdown");
        Assert.Null(Bbs757EfbPress.CoveringList(closed, shutFlap, Bbs757EfbPress.TapPoint(closed, shutFlap)!.Value));
    }

    [Fact]
    public void The_notification_panel_is_read_while_it_shows_and_covers_what_lies_under_it()
    {
        // BlueBird's pop-up after a new notification ([1039.75,38 349.25x118], measured 2026-11-07) is drawn from the top bar app's own
        // fields, so the fresh read still finds Livery options' first switch under it (the final review's I1). The panel is the part of the
        // tablet it covers, rounded outward; the switch below its bottom (156), and every control with no panel up, are clear.
        var apps = Bbs757EfbTreeJson.Load("52-settings-livery-options");
        var popped = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(apps))!;
        Assert.Equal(new Rectangle(1039, 38, 350, 118), popped.NoticePanel);
        var units = popped.Items.Single(i => i.Key == "weight_units_toggle");
        var ehsi = popped.Items.Single(i => i.Key == "ehsi_up_reference_toggle");
        Assert.True(Bbs757EfbPress.UnderNotice(popped, units, Bbs757EfbPress.TapPoint(popped, units)!.Value));
        Assert.False(Bbs757EfbPress.UnderNotice(popped, ehsi, Bbs757EfbPress.TapPoint(popped, ehsi)!.Value));
        Assert.True(Bbs757EfbPress.UnderNotice(popped, null, new Point(1300, 120)));   // a page drag's press point

        // A control that is no page control (the top bar, a question's answer) is never held back by it.
        var bar = new Bbs757EfbItem(Bbs757EfbKind.Button, "Start boarding", new Rectangle(1142, 3, 180, 28), new Point(1231, 17), true, "top:boarding");
        Assert.False(Bbs757EfbPress.UnderNotice(popped with { TopBar = new[] { bar } }, bar, new Point(1300, 120)));

        // With no panel up, or none read (every committed tree), nothing is covered.
        var quiet = Bbs757EfbModelReader.Read(apps)!;
        Assert.Null(quiet.NoticePanel);
        Assert.False(Bbs757EfbPress.UnderNotice(quiet, quiet.Items.Single(i => i.Key == "weight_units_toggle"), new Point(1167, 112)));
        var hidden = apps.Select(a => a.Class == Bbs757EfbMemory.TopBarClass
            ? a with { Panel = new Bbs757EfbMemory.NoticePanel(false, false, 1039.75f, 38, 349.25f, 118) } : a).ToList();
        Assert.Null(Bbs757EfbModelReader.Read(hidden)!.NoticePanel);
        Assert.Null(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(apps, h: 0))!.NoticePanel);   // closed, its height 0
        Assert.Null(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(apps, x: float.NaN))!.NoticePanel);
        Assert.Null(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(apps, y: 3e9f))!.NoticePanel);

        // The full list open: 480 tall, over PERF calc's Surface drop-down (977.9,324.2 150x34, its middle at x 1052) too.
        var listed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"), h: 480, list: true))!;
        Assert.Equal(new Rectangle(1039, 38, 350, 480), listed.NoticePanel);
        var surface = listed.Items.Single(i => i.Name == "planning_perf_calc_surface_dropdown");
        Assert.True(Bbs757EfbPress.UnderNotice(listed, surface, Bbs757EfbPress.TapPoint(listed, surface)!.Value));
    }

    [Fact]
    public void The_notification_panel_round_trips_through_the_tree_format_and_is_no_page_change()
    {
        var apps = Bbs757EfbTreeJson.WithNoticePanel(Bbs757EfbTreeJson.Load("52-settings-livery-options"), list: true, h: 480);
        var again = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(apps));
        Assert.Equal(new Bbs757EfbMemory.NoticePanel(true, true, 1039.75f, 38, 349.25f, 480), again.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Panel);
        Assert.All(again.Where(a => a.Class != Bbs757EfbMemory.TopBarClass), a => Assert.Null(a.Panel));

        // A pop-up comes and goes over any page, as the notifications do: not in the fingerprint.
        var quiet = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("52-settings-livery-options"))!;
        var popped = Bbs757EfbModelReader.Read(apps)!;
        Assert.Equal(Bbs757EfbPress.Fingerprint(quiet), Bbs757EfbPress.Fingerprint(popped));
    }

    [Fact]
    public void The_systems_page_menu_in_the_title_row_is_tapped_where_it_is_drawn()
    {
        // Systems keeps its page menu in the app's title row (1097,32 300x50), above the content area (82): a control in no scrollbox
        // is clipped to the page area, so it is tapped at its own point, not refused as out of view.
        var systems = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("30-systems"))!;
        var menu = systems.Items.Single(i => i.Name == "pb_menu1");

        Assert.Equal(Bbs757EfbScreen.PageArea, menu.Clip);
        Assert.Equal(new Point(1247, 57), Bbs757EfbPress.TapPoint(systems, menu));
    }

    [Theory]
    [InlineData("51-settings-aircraft", true)]
    [InlineData("30-systems", false)]
    [InlineData("24-navigation-map", false)]
    public void Only_a_page_with_a_content_scrollbox_reads_scrollable(string fixture, bool scrollable)
    {
        Assert.Equal(scrollable, Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!.Scrollable);
    }

    [Fact]
    public void The_last_switch_of_settings_aircraft_mid_scroll_is_tapped_in_the_strip_that_shows()
    {
        // Geometry: Settings, Aircraft mid-scroll, at the offset where a strip of its last switch was measured to take a tap (176.7,
        // 2026-11-07). The row's control is its toggle, [1137,1214.5 60x35]: drawn from 1037.8, of which about 10 px show above the
        // viewport's bottom (1048). It is tapped in the middle of that strip.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Scrolled(Bbs757EfbTreeJson.Load("51-settings-aircraft"), "aircraft_scrollbox", 176.7f))!;
        var dirty = page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        Assert.Equal("DIRTY PANEL STATES", dirty.Text);
        Assert.Equal(1048, page.Viewport.Bottom);
        Assert.InRange(dirty.Box.Top, 1037, 1038);

        var tap = Bbs757EfbPress.TapPoint(page, dirty);

        Assert.NotNull(tap);
        Assert.Equal(dirty.Hit.X, tap!.Value.X);
        Assert.Equal(dirty.Box.Top + (1048 - dirty.Box.Top) / 2, tap.Value.Y);
        Assert.Null(Bbs757EfbPress.DragFor(page, dirty));
    }

    [Fact]
    public void The_last_switch_of_settings_aircraft_shows_whole_at_the_end_of_its_scroll()
    {
        // The end of the scroll is the stackbox's 1,180 less the scrollbox's 966: 214, read at +0x134 (B757-26). The last switch is
        // drawn at 1000.5, whole inside the viewport, and tapped at its own point.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Scrolled(Bbs757EfbTreeJson.Load("51-settings-aircraft"), "aircraft_scrollbox", 214f))!;
        var dirty = page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        Assert.Equal(new Rectangle(1137, 1000, 60, 35), dirty.Box);

        Assert.Equal(dirty.Hit, Bbs757EfbPress.TapPoint(page, dirty));
    }

    /// <summary>Every page item a pilot can press, of every committed tree.</summary>
    public static IEnumerable<object[]> PressablesOfEveryFixture()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(FixtureRoot(), "bbs757-efb-memory"), "*.tree.json").OrderBy(p => p, StringComparer.Ordinal))
            yield return new object[] { Path.GetFileName(path)[..^".tree.json".Length] };
    }

    private static string FixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "Fixtures");
    }

    [Theory]
    [MemberData(nameof(PressablesOfEveryFixture))]
    public void Every_control_of_every_page_is_tapped_where_it_shows_or_scrolled_to(string fixture)
    {
        // At offset 0, as captured: a page that does not scroll has every control tappable inside its own clip; a page that scrolls has
        // each one tappable or dragged into view. The final review's C1: the ACARS dashboard, Weather & NOTAM and the Checklist had their
        // only (inner) scrollbox as the viewport, so every control outside it could never be pressed.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture));
        if (page == null) return;
        var pressables = page.Items.Where(i => i.Kind is Bbs757EfbKind.Button or Bbs757EfbKind.Tab or Bbs757EfbKind.Toggle
            or Bbs757EfbKind.Checkbox or Bbs757EfbKind.Slider).ToList();
        Assert.All(pressables, i =>
        {
            var tap = Bbs757EfbPress.TapPoint(page, i);
            if (tap is { } p)
            {
                var clip = Bbs757EfbPress.ClipOf(i);
                Assert.True(p.Y >= i.Box.Top && p.Y < i.Box.Bottom && p.Y >= clip.Top && p.Y < clip.Bottom,
                    $"{fixture}: {i.Kind} '{i.Text}' tapped at {p}, outside its box {i.Box} or its clip {clip}");
                return;
            }
            Assert.True(page.Scrollable && Bbs757EfbPress.DragFor(page, i) != null,
                $"{fixture}: {i.Kind} '{i.Text}' {i.Box} can neither be tapped nor scrolled to (clip {i.Clip}, viewport {page.Viewport})");
        });
    }

    // ---- Relocate ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Relocate_finds_a_moved_control_by_its_key()
    {
        // The page scrolled between the read and the press: the switch kept its key and lost its place.
        var pressed = Item(Bbs757EfbKind.Toggle, "Sync barometers", "tgl:SYNC BAROMETERS", new Rectangle(273, 333, 237, 33), on: false);
        var before = PageOf("SETTINGS", "AIRCRAFT", pressed);
        var scrolled = Item(Bbs757EfbKind.Toggle, "Sync barometers", "tgl:SYNC BAROMETERS", new Rectangle(273, 203, 237, 33), on: false);
        var other = Item(Bbs757EfbKind.Toggle, "Automatic boarding", "tgl:AUTOMATIC BOARDING", new Rectangle(273, 504, 275, 33), on: false);

        Assert.Same(scrolled, Bbs757EfbPress.Relocate(before, PageOf("SETTINGS", "AIRCRAFT", other, scrolled), pressed));
    }

    [Fact]
    public void Relocate_finds_a_tile_whose_state_words_changed()
    {
        // Req fuel truck, pressed: the tile now says Requesting... in the same place (another word, so another key).
        var pressed = ReqFuelTruck();
        var requesting = Requesting();

        Assert.Same(requesting, Bbs757EfbPress.Relocate(Ground(pressed, ReqCatering()), Ground(ReqCatering(), requesting), pressed));
    }

    [Theory]
    [InlineData(60, 0)]     // IoU 0.53: a neighbor sliding over the tile's place
    [InlineData(100, 0)]    // 0.32
    [InlineData(190, 0)]    // a sliver
    [InlineData(0, 21)]     // half the height: 0.33
    [InlineData(-80, 10)]   // from the other side, a little lower
    public void Relocate_never_takes_a_control_with_other_words(int dx, int dy)
    {
        // The tile is gone (a notification covers it). A neighbor covers its place in part, with other words: tapping it would
        // press another control than the one asked for, so nothing is found.
        var pressed = ReqFuelTruck();
        var neighbor = Tile("Req catering", "btn:|REQ CATERING@62,29", new Rectangle(pressed.Box.X + dx, pressed.Box.Y + dy, pressed.Box.Width, pressed.Box.Height));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(neighbor), pressed));
    }

    [Fact]
    public void Relocate_returns_null_on_another_page()
    {
        // The same key on a different page is another control.
        var pressed = Item(Bbs757EfbKind.Button, "Close all", "btn:|CLOSE ALL@16,9", new Rectangle(326, 189, 195, 29));
        var elsewhere = Item(Bbs757EfbKind.Button, "Close all", "btn:|CLOSE ALL@16,9", new Rectangle(326, 189, 195, 29));

        Assert.Null(Bbs757EfbPress.Relocate(PageOf("AIRCRAFT", "CABIN & DOORS", pressed), PageOf("AIRCRAFT", "GROUND SERVICES", elsewhere), pressed));
    }

    [Theory]
    [InlineData("Fuel truck off", "Fuel truck")]
    [InlineData("Chocks, on", "Chocks")]
    [InlineData("GPU removed", "GPU connected")]
    [InlineData("Left FWD: Closed", "Left FWD: Open")]
    [InlineData("Cockpit open", "cockpit CLOSED")]
    public void Relocate_follows_a_tile_that_moved_and_changed_only_its_state_word(string pressedText, string nowText)
    {
        // Moved down 25 px (a heading appeared above it): another 20 px bucket, so another key, and too little overlap to be the
        // same box (IoU 0.27). Only its words, without the state word, say it is the same tile.
        var pressed = Tile(pressedText, "btn:|TILE@45,9", new Rectangle(911, 193, 197, 43));
        var moved = Tile(nowText, "btn:|TILE@45,10", new Rectangle(911, 218, 197, 43));

        Assert.Same(moved, Bbs757EfbPress.Relocate(Ground(pressed), Ground(moved), pressed));
    }

    [Theory]
    [InlineData("Fuel truck off", "Req fuel truck")]       // other words
    [InlineData("Left FWD: Closed", "Right FWD: Closed")]   // the neighbor door
    [InlineData("Open", "Closed")]                          // a state word alone is no words
    [InlineData("Common", "Comm")]                          // "on" is a state word only as a word of its own
    public void Relocate_does_not_follow_a_moved_tile_with_other_words(string pressedText, string nowText)
    {
        var pressed = Tile(pressedText, "btn:|TILE@45,9", new Rectangle(911, 193, 197, 43));
        var moved = Tile(nowText, "btn:|TILE@45,10", new Rectangle(911, 218, 197, 43));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(moved), pressed));
    }

    [Fact]
    public void Relocate_does_not_follow_the_same_words_that_do_not_overlap_the_pressed_place()
    {
        // Same words, another place altogether (the page re-laid out): not provably the same control.
        var pressed = Tile("Chocks, on", "btn:|CHOCKS@15,25", new Rectangle(312, 513, 196, 42));
        var elsewhere = Tile("Chocks", "btn:|CHOCKS@15,45", new Rectangle(312, 913, 196, 42));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(elsewhere), pressed));
    }

    [Fact]
    public void Relocate_never_takes_a_control_of_another_kind()
    {
        var pressed = ReqFuelTruck();
        var page = Ground(
            Item(Bbs757EfbKind.Toggle, "Req fuel truck", "tgl:REQ FUEL TRUCK", pressed.Box, on: true),
            Item(Bbs757EfbKind.Text, "Req fuel truck", "txt:Req fuel truck", pressed.Box),
            Item(Bbs757EfbKind.Slider, "Req fuel truck", "sld:REQ FUEL TRUCK", pressed.Box));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), page, pressed));
    }

    [Fact]
    public void Relocate_takes_the_closest_box_when_several_cover_the_place()
    {
        var pressed = Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(0, 0, 100, 100));
        var weaker = Tile("Requesting" + Ellipsis, "btn:|A@0,0", new Rectangle(0, 0, 100, 65));     // IoU 0.65
        var closer = Tile("Req catering", "btn:|B@0,0", new Rectangle(0, 0, 100, 90));               // IoU 0.90

        Assert.Same(closer, Bbs757EfbPress.Relocate(Ground(pressed), Ground(weaker, closer), pressed));
    }

    [Fact]
    public void Relocate_takes_a_box_at_three_fifths_overlap_and_not_below()
    {
        var pressed = Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(0, 0, 100, 100));
        var exactly = Tile("Requesting" + Ellipsis, "btn:|A@0,0", new Rectangle(0, 0, 100, 60));     // IoU exactly 0.60
        var below = Tile("Requesting" + Ellipsis, "btn:|A@0,0", new Rectangle(0, 0, 100, 59));       // 0.59

        Assert.Same(exactly, Bbs757EfbPress.Relocate(Ground(pressed), Ground(exactly), pressed));
        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(below), pressed));
    }

    [Fact]
    public void Relocate_gives_a_control_with_no_box_only_by_its_key()
    {
        // The window's own Home button has no box. Two empty boxes cover each other in no place at all.
        var home = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", Rectangle.Empty, Bbs757EfbScreen.HomeButton, true, "btn:HOME");
        var other = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", Rectangle.Empty, Bbs757EfbScreen.HomeButton, true, "btn:OTHER");

        Assert.Null(Bbs757EfbPress.Relocate(Ground(home), Ground(other), home));
        Assert.Same(home, Bbs757EfbPress.Relocate(Ground(home), Ground(other, home), home));
    }

    [Fact]
    public void Relocate_looks_in_the_menu_the_top_bar_and_the_question()
    {
        var menu = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var yes = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var boarding = StartBoarding();
        var before = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { menu }, new[] { ReqFuelTruck() })
        {
            TopBar = new[] { boarding },
            Question = new Bbs757EfbQuestion("Are you sure?", new[] { yes }),
        };
        var menuNow = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var yesNow = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var boardingNow = Boarding();
        var now = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { menuNow }, new[] { ReqFuelTruck() })
        {
            TopBar = new[] { boardingNow },
            Question = new Bbs757EfbQuestion("Are you sure?", new[] { yesNow }),
        };

        Assert.Same(menuNow, Bbs757EfbPress.Relocate(before, now, menu));
        Assert.Same(yesNow, Bbs757EfbPress.Relocate(before, now, yes));
        // The boarding control keeps its key as its words change (it is text while boarding runs).
        Assert.Same(boardingNow, Bbs757EfbPress.Relocate(before, now, boarding));
    }

    [Fact]
    public void A_control_the_notification_list_covers_is_gone_and_the_others_are_found()
    {
        // bright/18 shows a notification, bright/19 the list the top bar's arrow opens: the list covers four of the page's controls,
        // which the reader drops (it would read them cut). Nothing else may take their place.
        var before = Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification");
        var now = Bbs757EfbScreenTests.Load("bright", "19-notification-list");

        foreach (var covered in new[] { "Req cargo SVC", "Right air start", "GPU connected", "Remove all" })
            Assert.Null(Bbs757EfbPress.Relocate(before, now, ItemNamed(before, covered)));
        foreach (var clear in new[] { "Req fuel truck", "Chocks", "Left air start", "Req catering" })
        {
            var pressed = ItemNamed(before, clear);
            Assert.Equal(pressed.Key, Bbs757EfbPress.Relocate(before, now, pressed)?.Key);
        }
    }

    [Fact]
    public void Every_control_of_a_real_page_is_found_again_on_another_capture_of_it()
    {
        // The same page at 100 % and dimmed: the keys hold (they are what Relocate leans on), so each control comes back as itself.
        var before = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var now = Bbs757EfbScreenTests.Load("dim", "13-aircraft-ground-services");

        var controls = before.Menu.Concat(before.Items).Concat(before.TopBar).ToList();
        Assert.True(controls.Count > 15, "the page reads its menu and tiles");
        foreach (var pressed in controls)
            Assert.Equal(pressed.Key, Bbs757EfbPress.Relocate(before, now, pressed)?.Key);
    }

    [Theory]
    [InlineData("Req fuel truck", "Fuel truck off")]
    [InlineData("Req catering", "Catering off")]
    [InlineData("Req cargo SVC", "Cargo SVC off")]
    [InlineData("Req FWD stair", "FWD stair off")]
    [InlineData("Stairs disabled", "AFT stairs off")]
    [InlineData("GPU connected", "GPU removed")]
    public void Relocate_follows_a_real_tile_whose_words_changed_in_place(string pressedText, string nowText)
    {
        // GND SRVC Mode MSFS (bright/18) and BBS (bright/13) word the same tiles differently, in the same boxes.
        var before = Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification");
        var now = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");

        Assert.Equal(nowText, Bbs757EfbPress.Relocate(before, now, ItemNamed(before, pressedText))?.Text);
    }

    // ---- NeedsTap ------------------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Switch(bool? on) =>
        Item(Bbs757EfbKind.Toggle, "Sync barometers", "tgl:SYNC BAROMETERS", new Rectangle(273, 333, 237, 33), on: on);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void A_switch_already_as_wished_needs_no_tap(bool on, bool wantOn)
    {
        // It changed since the read (BlueBird, or the pilot's other hand): tapping would undo the wish.
        Assert.False(Bbs757EfbPress.NeedsTap(Switch(on), wantOn));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_switch_that_differs_from_the_wish_is_tapped(bool on, bool wantOn)
    {
        Assert.True(Bbs757EfbPress.NeedsTap(Switch(on), wantOn));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unread_switch_is_tapped(bool wantOn)
    {
        // The reader could not tell its state: it is not known to be as wished.
        Assert.True(Bbs757EfbPress.NeedsTap(Switch(null), wantOn));
    }

    // ---- ChoiceX -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(895, 1190, 5)]     // IRS alignment time
    [InlineData(895, 1190, 4)]     // Boarding & load rate
    [InlineData(895, 1190, 16)]    // Notifications fade-out timer
    [InlineData(795, 1100, 2)]     // two choices: the two ends
    public void The_first_choice_is_at_the_start_of_the_track_and_the_last_at_its_end(int left, int right, int count)
    {
        var slider = new Bbs757EfbSlider(left, right, left);

        Assert.Equal(left, Bbs757EfbPress.ChoiceX(slider, 0, count));
        Assert.Equal(right, Bbs757EfbPress.ChoiceX(slider, count - 1, count));
    }

    [Theory]
    [InlineData(100, 500, 5, 2, 300)]       // the middle of five on a round track
    [InlineData(100, 500, 5, 1, 200)]
    [InlineData(895, 1190, 5, 1, 969)]      // IRS alignment time: 73.75 px apart
    [InlineData(895, 1190, 5, 2, 1043)]     // 147.5: to the even
    [InlineData(895, 1190, 5, 3, 1116)]
    [InlineData(895, 1190, 4, 1, 993)]      // Boarding & load rate
    [InlineData(895, 1190, 4, 2, 1092)]
    [InlineData(895, 1190, 16, 7, 1033)]    // Notifications fade-out timer
    [InlineData(895, 1190, 16, 8, 1052)]
    public void The_choices_are_spread_evenly_along_the_track(int left, int right, int count, int index, int expected)
    {
        // The places measured on the three settings sliders (2026-10-26) are unchanged by the move out of the window's elements.
        Assert.Equal(expected, Bbs757EfbPress.ChoiceX(new Bbs757EfbSlider(left, right, left), index, count));
    }

    [Fact]
    public void A_slider_with_one_choice_is_tapped_at_the_start_of_its_track()
    {
        Assert.Equal(895, Bbs757EfbPress.ChoiceX(new Bbs757EfbSlider(895, 1190, 1000), 0, 1));
    }

    // ---- NextChoiceX ---------------------------------------------------------------------------------------------------

    private static readonly Bbs757EfbSlider Track = new(895, 1190, 1000);

    [Theory]
    // lastX, target, read, count, taps, expected
    [InlineData(1116, 3, 2, 5, 1, 1141)]   // IRS: a choice is 73.75 px, a third 24.6 -> 25; the knob landed one short: right
    [InlineData(969, 1, 2, 5, 1, 944)]     // the other side: the knob landed one past: left
    [InlineData(1033, 7, 6, 16, 2, 1040)]  // fade-out timer: 19.7 px a choice, a third 6.6 -> 7
    [InlineData(1033, 7, 8, 16, 2, 1026)]
    [InlineData(993, 1, 0, 4, 3, 1026)]    // boarding rate: 98.3 px a choice, a third 32.8 -> 33
    [InlineData(1000, 30, 29, 60, 1, 1004)]  // 5 px a choice: a third is under 4 px, so 4 px
    [InlineData(1000, 30, 31, 60, 1, 996)]
    public void The_next_tap_steps_a_third_of_a_choice_toward_the_target(int lastX, int target, int read, int count, int taps, int expected)
    {
        Assert.Equal(expected, Bbs757EfbPress.NextChoiceX(Track, lastX, target, read, count, taps));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Stepping_goes_on_below_four_taps(int taps)
    {
        Assert.NotNull(Bbs757EfbPress.NextChoiceX(Track, 1000, 3, 2, 5, taps));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Stepping_stops_after_four_taps(int taps)
    {
        Assert.Equal(4, Bbs757EfbPress.MaxSliderTaps);
        Assert.Null(Bbs757EfbPress.NextChoiceX(Track, 1000, 3, 2, 5, taps));
    }

    [Fact]
    public void A_slider_whose_value_was_not_read_is_not_stepped()
    {
        Assert.Null(Bbs757EfbPress.NextChoiceX(Track, 1000, 3, -1, 5, 1));
    }

    [Fact]
    public void A_slider_already_at_the_target_has_no_next_tap()
    {
        Assert.Null(Bbs757EfbPress.NextChoiceX(Track, 1116, 3, 3, 5, 1));
    }

    [Theory]
    [InlineData(1190, 15, 14, 1190)]   // a step past the track's right end would tap beside it (another control's place): the end
    [InlineData(895, 0, 1, 895)]       // and past its left end
    public void A_step_never_leaves_the_track(int lastX, int target, int read, int expected)
    {
        Assert.Equal(expected, Bbs757EfbPress.NextChoiceX(Track, lastX, target, read, 16, 1));
    }

    // ---- Fingerprint and PageUnchanged ---------------------------------------------------------------------------------

    [Fact]
    public void Two_reads_that_say_the_same_have_the_same_fingerprint()
    {
        // A refresh of an unchanged tablet must say the same thing: a pixel of wobble in a box moves nothing the pilot would hear, a misread does.
        var a = Ground(Tile("GPU", "btn:GPU", new Rectangle(300, 300, 80, 40)));
        var b = Ground(Tile("GPU", "btn:GPU", new Rectangle(301, 299, 80, 40)));
        var c = Ground(Tile("GPV", "btn:GPV", new Rectangle(300, 300, 80, 40)));

        Assert.Equal(Bbs757EfbPress.Fingerprint(a), Bbs757EfbPress.Fingerprint(b));
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(a), Bbs757EfbPress.Fingerprint(c));
    }

    [Fact]
    public void A_notification_appearing_over_an_unchanged_page_changes_the_fingerprint()
    {
        // The bridge pushes a read only when its fingerprint moved: a notification over a page that did not change must count.
        var quiet = Ground(Tile("GPU", "btn:GPU", new Rectangle(300, 300, 80, 40)));
        var toast = quiet with { Notices = new[] { GpuConnected } };

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(quiet), Bbs757EfbPress.Fingerprint(toast));
    }

    [Fact]
    public void Wordless_dialogs_are_told_apart_by_their_answers()
    {
        // Every question's answers are keyed alike (q:OK): a question is the same question by its words, and one with none (none was
        // measured) by its text boxes' names and its answers' words, so an OK pressed on one is never tapped on another.
        static Bbs757EfbItem Answer(string text) => Item(Bbs757EfbKind.Button, text, "q:" + text.ToUpperInvariant(), new Rectangle(608, 589, 180, 60));
        var ok = new Bbs757EfbQuestion("", new[] { Answer("OK") });
        var insert = new Bbs757EfbQuestion("", new[] { Answer("Cancel"), Answer("Insert") });
        var fuel = insert with { Rows = new[] { Item(Bbs757EfbKind.Field, "FUEL", "fuel_input_textbox", new Rectangle(595, 284, 250, 52)) } };

        Assert.False(Bbs757EfbPress.SameQuestion(ok, insert));
        Assert.False(Bbs757EfbPress.SameQuestion(insert, fuel));
        Assert.True(Bbs757EfbPress.SameQuestion(ok, ok with { }));
        // Words decide whenever there are any, however the answers read.
        Assert.True(Bbs757EfbPress.SameQuestion(new Bbs757EfbQuestion("Are you sure?", new[] { Answer("Yes") }),
            new Bbs757EfbQuestion("Are you sure ?", new[] { Answer("OK") })));
        Assert.False(Bbs757EfbPress.SameQuestion(ok, new Bbs757EfbQuestion("OK", new[] { Answer("OK") })));
    }

    [Fact]
    public void The_fingerprint_covers_the_top_bar_the_notices_and_the_question()
    {
        var page = Ground(ReqFuelTruck());
        var bar = page with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:46", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var countdown = page with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:45", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var toast = page with { Notices = new[] { GpuConnected } };
        var later = page with { Notices = new[] { new Bbs757EfbNotice(1, "GPU connected.", "14:19Z") } };
        var question = page with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }) };
        var other = page with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "OK", "q:OK", new Rectangle(336, 591, 177, 57)) }) };

        var prints = new[] { page, bar, countdown, toast, later, question, other }.Select(Bbs757EfbPress.Fingerprint).ToList();

        Assert.Equal(prints.Count, prints.Distinct().Count());
    }

    [Fact]
    public void A_label_change_is_a_new_page_to_push()
    {
        var box = new Rectangle(300, 300, 80, 40);
        var plain = Ground(Tile("OPEN", "btn:OPEN", box));
        var labeled = Ground(Tile("OPEN", "btn:OPEN", box) with { Label = "NOSE DOOR" });
        var heading = Ground(Item(Bbs757EfbKind.Heading, "Doors", "h:Doors", box) with { Level = 2 });
        var heading3 = Ground(Item(Bbs757EfbKind.Heading, "Doors", "h:Doors", box) with { Level = 3 });

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(plain), Bbs757EfbPress.Fingerprint(labeled));
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(heading), Bbs757EfbPress.Fingerprint(heading3));
    }

    [Fact]
    public void A_drop_downs_entries_changing_is_a_new_page_to_push()
    {
        // Plan 21: the window's choice list offers a drop-down's entries, so a read in which only they changed (another airport's
        // runways, the same first one shown) is pushed; the same entries in a new list are no change.
        var box = new Rectangle(519, 156, 150, 34);
        Bbs757EfbScreenModel Runways(params string[] entries) => Ground(Item(Bbs757EfbKind.Choice, "Runway", "planning_perf_calc_runway_dropdown", box)
            with { Value = "RW07L", Entries = entries });

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(Runways("RW07L", "RW25R")), Bbs757EfbPress.Fingerprint(Runways("RW07L", "RW26")));
        Assert.True(Bbs757EfbPress.PageUnchanged(Runways("RW07L", "RW26"), Runways("RW07L", "RW26")));
        Assert.False(Bbs757EfbPress.PageUnchanged(Runways("RW07L"), Runways("RW07L", "RW26")));
    }

    [Fact]
    public void A_field_becoming_an_edit_box_or_a_line_is_a_new_page_to_push_and_its_caret_is_not()
    {
        // The final review's M4: the window shows a field with its caret read as an edit box and one without as a line, so a read where a
        // field gains or loses its state, its text the same, is pushed; the caret moving (it does as the pilot types) and the box's other
        // state are no change.
        var box = new Rectangle(289, 156, 150, 34);
        Bbs757EfbScreenModel Airport(Bbs757EfbTextState? state) => PageOf("NAVIGATION", "PERF CALC",
            Item(Bbs757EfbKind.Field, "AIRPORT", "planning_perf_calc_airport_input", box, value: "KSEA") with { TextState = state });

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(Airport(null)), Bbs757EfbPress.Fingerprint(Airport(new Bbs757EfbTextState(4, 4, false))));
        Assert.False(Bbs757EfbPress.PageUnchanged(Airport(new Bbs757EfbTextState(4, 4, false)), Airport(null)));
        Assert.Equal(Bbs757EfbPress.Fingerprint(Airport(new Bbs757EfbTextState(4, 4, false))),
            Bbs757EfbPress.Fingerprint(Airport(new Bbs757EfbTextState(1, 4, false))));
    }

    [Fact]
    public void The_keyboard_coming_up_is_no_page_change()
    {
        var page = Ground(ReqFuelTruck());
        var withKeys = page with
        {
            Keyboard = new Bbs757EfbKeyboard(new Rectangle(0, 648, 1397, 400),
                new[] { new Bbs757EfbKey("char_btn_q|Q|1", "q", "Q", "1", new Point(40, 700)) }),
        };

        Assert.True(Bbs757EfbPress.PageUnchanged(page, withKeys));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_fingerprint_does_not_depend_on_the_Windows_culture(string culture)
    {
        var page = Ground(ReqFuelTruck(), Switch(true)) with
        {
            TopBar = new[] { Notifications() },
            Notices = new[] { GpuConnected },
            Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }),
        };

        Assert.Equal(Bbs757EfbPress.Fingerprint(page), TestCulture.Run(culture, () => Bbs757EfbPress.Fingerprint(page)));
    }

    [Fact]
    public void The_page_is_unchanged_when_only_the_top_bar_or_the_notices_differ()
    {
        var before = Ground(ReqFuelTruck(), ReqCatering());
        var countdown = before with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:46", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var toast = before with { Notices = new[] { GpuConnected }, TopBar = new[] { Notifications() } };

        Assert.True(Bbs757EfbPress.PageUnchanged(before, before));
        Assert.True(Bbs757EfbPress.PageUnchanged(before, countdown));
        Assert.True(Bbs757EfbPress.PageUnchanged(before, toast));
        Assert.True(Bbs757EfbPress.PageUnchanged(countdown, toast));
        // ...but the bridge still sees them as a change.
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(before), Bbs757EfbPress.Fingerprint(toast));
    }

    [Fact]
    public void The_page_is_unchanged_when_things_only_wobble_by_a_pixel()
    {
        var a = Ground(Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1132, 577, 196, 42)));
        var b = Ground(Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1131, 576, 197, 43)));

        Assert.True(Bbs757EfbPress.PageUnchanged(a, b));
    }

    [Fact]
    public void A_knob_bluebird_moved_is_a_change_of_the_page_in_whole_percent()
    {
        // The review's I2: the window's slider value is the knob's place, so two reads that differ only in BlueBird's knob are two pages
        // (the bridge pushes the second, a press sees its change). In whole percent, as the window shows it, so a pixel of wobble that
        // stays at the same percent is none.
        static Bbs757EfbScreenModel At(int knobX, bool read = true) => PageOf("AIRCRAFT", "WEIGHT & BALANCE",
            Item(Bbs757EfbKind.Slider, "FORWARD CARGO", "wb_baggage_fwd_slider", new Rectangle(1162, 462, 188, 18))
                with { Slider = new Bbs757EfbSlider(1168, 1344, knobX) { KnobRead = read } });

        Assert.False(Bbs757EfbPress.PageUnchanged(At(1273), At(1300)));                     // 60 % and 75 %
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(At(1273)), Bbs757EfbPress.Fingerprint(At(1300)));
        Assert.True(Bbs757EfbPress.PageUnchanged(At(1273), At(1274)));                      // 59.66 % and 60.23 %: both 60
        // A knob placed from the value shown (not BlueBird's) moves only with that value, which the page already compares.
        Assert.True(Bbs757EfbPress.PageUnchanged(At(1273, read: false), At(1300, read: false)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_knobs_place_in_the_fingerprint_is_the_same_in_any_culture(string culture)
    {
        var page = PageOf("SYSTEMS", "", Item(Bbs757EfbKind.Slider, "ELEC ZOOM", "elec_zoom_slider", new Rectangle(16, 148, 42, 154))
            with { Slider = new Bbs757EfbSlider(296, 154, 281) { Vertical = true, AcrossX = 37, KnobRead = true } });
        Assert.Equal(TestCulture.Run("en-US", () => Bbs757EfbPress.Fingerprint(page)), TestCulture.Run(culture, () => Bbs757EfbPress.Fingerprint(page)));
        Assert.Contains("|11\n", Bbs757EfbPress.Fingerprint(page));
    }

    [Fact]
    public void A_question_appearing_is_a_change_of_the_page()
    {
        var before = Ground(ReqFuelTruck());
        var after = before with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }) };

        Assert.False(Bbs757EfbPress.PageUnchanged(before, after));
        Assert.False(Bbs757EfbPress.PageUnchanged(after, before));
    }

    [Fact]
    public void Anything_on_the_page_itself_changing_is_a_change_of_the_page()
    {
        var tile = ReqFuelTruck();
        var before = Ground(tile);
        var changedPages = new[]
        {
            Ground(tile with { Text = "Requesting" + Ellipsis }),
            Ground(tile with { Value = "5 percent" }),
            Ground(tile with { On = true }),
            Ground(tile with { Selected = true }),
            Ground(tile with { Key = "btn:|REQ FUEL TRUCK@57,28" }),
            Ground(tile, ReqCatering()),
            PageOf("AIRCRAFT", "CABIN & DOORS", tile),
            new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29)) }, new[] { tile }),
        };

        Assert.All(changedPages, after => Assert.False(Bbs757EfbPress.PageUnchanged(before, after)));
    }

    // ---- ButtonOutcome -------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_press_that_changes_the_page_says_nothing(string culture)
    {
        var fuel = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var before = Ground(ReqFuelTruck());
        var after = PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@18,46", new Rectangle(377, 929, 280, 45)));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, fuel)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_tile_whose_words_changed_under_a_new_key_says_its_new_words(string culture)
    {
        // Req fuel truck, pressed: the tile says Requesting... in the same box, under another key. The window rebuilds a control
        // whose key changed and says nothing of it, so the press says the new words, once.
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = Ground(ReqCatering(), Requesting());

        Assert.Equal("Requesting" + Ellipsis, TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_control_whose_words_changed_says_them_whatever_its_key_did(string culture)
    {
        var box = new Rectangle(700, 780, 280, 45);
        var mode = new Rectangle(258, 864, 196, 42);
        var truck = new Rectangle(1132, 577, 196, 42);
        var cases = new (string Section, Bbs757EfbItem Pressed, Bbs757EfbItem Now)[]
        {
            // The ACARS dashboard's fetch becomes its refresh in the same box: another key.
            ("ACARS DASHBOARD", Tile("Fetch SIMBRIEF OFP", "btn:|FETCH SIMBRIEF OFP@42,40", box),
                Tile("Refresh SIMBRIEF OFP", "btn:|REFRESH SIMBRIEF OFP@42,40", box)),
            // GND SRVC Mode under its caption (bright/13 and bright/18): btn:GND SRVC Mode|BBS@16,44 becomes ...|MSFS@16,44.
            ("GROUND SERVICES", Tile("GND SRVC Mode: BBS", "btn:GND SRVC Mode|BBS@16,44", mode),
                Tile("GND SRVC Mode: MSFS", "btn:GND SRVC Mode|MSFS@16,44", mode)),
            // BBS mode's tile: its state word changed and its key, which leaves the state word out, did not.
            ("GROUND SERVICES", Tile("Fuel truck off", "btn:|FUEL TRUCK@56,28", truck), Tile("Fuel truck on", "btn:|FUEL TRUCK@56,28", truck)),
        };

        foreach (var (section, pressed, now) in cases)
        {
            var before = PageOf("AIRCRAFT", section, pressed);
            var after = PageOf("AIRCRAFT", section, now);
            Assert.Equal(now.Text, TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_question_the_press_brought_up_is_its_outcome_whatever_the_dimmed_page_reads(string culture)
    {
        // IMPORT FROM SIMBRIEF, then the defuel question over the dimmed page: under the dimming the reader names the button by the
        // TOTAL FUEL label above it (bright/20 reads btn:|IMPORT FROM SIMBRIEF@18,46, bright/21 btn:TOTAL FUEL|IMPORT FROM
        // SIMBRIEF@18,46), in the same box. The read that found the question said it; the button's other words would talk over it.
        var box = new Rectangle(377, 929, 280, 45);
        var import = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@18,46", box);
        var before = PageOf("AIRCRAFT", "FUEL", import);
        var dimmed = Tile("Total fuel: Import from SIMBRIEF", "btn:TOTAL FUEL|IMPORT FROM SIMBRIEF@18,46", box);
        var yes = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var after = PageOf("AIRCRAFT", "FUEL", dimmed) with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { yes }) };
        Assert.Same(dimmed, Bbs757EfbPress.Relocate(before, after, import));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, import)));
        // Under a question that was up before the press (a top bar control pressed under it), the control's new words are said.
        var asked = before with { Question = after.Question, TopBar = new[] { StartBoarding() } };
        var boarding = asked with { TopBar = new[] { Boarding() } };
        Assert.Equal("Boarding" + Ellipsis + " 10 min", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(asked, boarding, StartBoarding())));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_page_with_only_a_new_notification_is_still_no_change(string culture)
    {
        // The press did nothing the page shows; the notification (BlueBird's, or another press's) is not the press's outcome.
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = before with { Notices = new[] { GpuConnected }, TopBar = new[] { Notifications() } };

        Assert.Equal("Req fuel truck: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void An_unchanged_page_is_no_change(string culture)
    {
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = Ground(ReqFuelTruck(), ReqCatering());

        Assert.Equal("Req fuel truck: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_press_that_changed_another_control_says_nothing(string culture)
    {
        // The pressed tile reads as before, but something else on the page moved: the press did something.
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = Ground(ReqFuelTruck(), Tile("Requesting" + Ellipsis, "btn:|REQ CATERING@45,36", ReqCatering().Box));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_question_appearing_is_the_outcome_not_a_no_change(string culture)
    {
        var pressed = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@18,46", new Rectangle(377, 929, 280, 45));
        var before = PageOf("AIRCRAFT", "FUEL", pressed);
        var after = before with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }) };

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void An_answer_whose_question_went_says_nothing_whatever_lies_under_it(string culture)
    {
        // YES pressed: the question went, and the page it covered has a button where the YES was. By its box alone it would be
        // found as the YES with other words; it is another control, and the question's going is the press's outcome.
        var yes = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var before = PageOf("AIRCRAFT", "FUEL") with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { yes }) };
        var under = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@21,30", new Rectangle(338, 592, 177, 57));
        var after = PageOf("AIRCRAFT", "FUEL", under);
        Assert.Same(under, Bbs757EfbPress.Relocate(before, after, yes));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, yes)));
        // The question still up, the answer not taken: that is no change.
        Assert.Equal("Yes: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, before with { }, yes)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_top_bar_button_that_became_text_says_its_new_words_though_the_top_bar_is_not_the_page(string culture)
    {
        // Start boarding becomes Boarding... 10 min, text under the same key: the page below the bar is untouched, but the pressed
        // control changed, and its new words say the press took.
        var pressed = StartBoarding();
        var before = Ground(ReqFuelTruck()) with { TopBar = new[] { pressed } };
        var after = Ground(ReqFuelTruck()) with { TopBar = new[] { Boarding() } };

        Assert.True(Bbs757EfbPress.PageUnchanged(before, after));
        Assert.Equal("Boarding" + Ellipsis + " 10 min", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_notification_button_says_its_new_words(string culture)
    {
        // Pressed, it opens BlueBird's list (bright/18 to bright/19): "Notifications: 55" becomes "Close notifications", its key
        // the same, while the list covers part of the page.
        var before = Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification");
        var after = Bbs757EfbScreenTests.Load("bright", "19-notification-list");
        var pressed = Assert.Single(before.TopBar, i => i.Key == Bbs757EfbTopBar.NotificationsKey);

        Assert.Equal("Close notifications", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_top_bar_control_that_vanished_says_nothing(string culture)
    {
        var pressed = Notifications();
        var before = Ground(ReqFuelTruck()) with { TopBar = new[] { pressed } };
        var after = Ground(ReqFuelTruck());

        Assert.True(Bbs757EfbPress.PageUnchanged(before, after));
        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_top_bar_control_that_did_nothing_is_no_change(string culture)
    {
        var pressed = StartBoarding();
        var before = Ground(ReqFuelTruck()) with { TopBar = new[] { pressed } };
        var after = Ground(ReqFuelTruck()) with { TopBar = new[] { StartBoarding() } };

        Assert.Equal("Start boarding: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    // ---- the texts -----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_spoken_texts_are_the_same_in_any_Windows_culture(string culture)
    {
        // Verbatim from the plan's speech rules. The label is the element's text; the value is already the words said.
        var texts = TestCulture.Run(culture, () => new[]
        {
            Bbs757EfbPress.GoneText("Req fuel truck"),
            Bbs757EfbPress.NoChangeText("Req fuel truck"),
            Bbs757EfbPress.NotChangedText("Sync barometers"),
            Bbs757EfbPress.ChoiceText("CPDLC network", "SayIntentions"),
            Bbs757EfbPress.ChoiceText("Displays bootup speed", "Realistic"),
            Bbs757EfbPress.CouldNotSetText("IRS alignment time", "Full (7 min+)"),
            Bbs757EfbPress.CouldNotSetText("Ceiling", "30,5 percent"),
            Bbs757EfbPress.UnreachableText,
            Bbs757EfbPress.UnderKeyboardText("Req fuel truck"),
        });

        Assert.Equal(new[]
        {
            "Req fuel truck is no longer on the tablet.",
            "Req fuel truck: no change on the tablet.",
            "Sync barometers did not change.",
            "CPDLC network: SayIntentions",
            "Displays bootup speed: Realistic",
            "IRS alignment time could not be set to Full (7 min+).",
            "Ceiling could not be set to 30,5 percent.",
            "The tablet cannot be reached, so nothing was tapped.",
            "Req fuel truck is under the tablet's keyboard, not pressed.",
        }, texts);
    }

    // ---- the new word of a switch of two choices ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Hoppie", "SayIntentions", "SayIntentions")]        // the word changed: the new word, as the page shows it
    [InlineData("Hoppie", "Sayintentions", "Sayintentions")]        // the reader's spelling is kept
    [InlineData("Option 1", "Option 2", "Option 2")]                // a digit is part of the word
    [InlineData("HDG", "Trk", "Trk")]
    [InlineData("100%", "Auto", "Auto")]                            // a percent is a word to compare, whatever the tablet shows
    [InlineData("Hoppie", "Hoppie", null)]                          // the pill moved, the word did not
    [InlineData("Hoppie", "HOPPIE", null)]                          // case wobbles from one read to the next
    [InlineData("Imperial", "IMPERIAL", null)]                      // in any culture (tr-TR upper-cases an i to a dotted capital)
    [InlineData("Option 1", " option  1. ", null)]                  // so do spaces and punctuation
    [InlineData("", "SayIntentions", null)]                         // no word before: a plain switch, or one the reader gave none
    [InlineData("On", "SayIntentions", null)]                       // On and Off are the plain switch's states, not words
    [InlineData("off", "SayIntentions", null)]
    [InlineData("Hoppie", "", null)]                                // no word now
    [InlineData("Hoppie", "On", null)]
    [InlineData("Hoppie", "OFF", null)]
    [InlineData("Hoppie", "...", null)]                             // punctuation alone is no word
    [InlineData("40", "", null)]                                    // the TOD pause distance switched off
    [InlineData("Hoppie", null, null)]                              // the control was not found again
    public void The_new_word_of_a_two_choice_switch_is_the_word_it_shows_now_when_it_is_another(string before, string? now, string? expected)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE", "tr-TR" })
            Assert.Equal(expected, TestCulture.Run(culture, () => Bbs757EfbPress.NewChoiceWord(before, now)));
    }

    // ---- plan 20, Task 6: named controls are still found, and a settings slider has its track --------------------------------

    [Fact]
    public void A_labelled_door_button_is_relocated_by_its_key()
    {
        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("14-aircraft-cabin-doors"))!;
        var door = closed.Items.Single(i => i.Name == "btn_l1_door");
        Assert.Equal("LEFT FWD", door.Label);
        Assert.Equal(8, closed.Items.Count(i => i.Kind == Bbs757EfbKind.Button && i.Label.Length > 0));

        // The door opened: its words change, its label and its key do not, and the read finds the same control again.
        var opened = new Bbs757EfbScreenModel(closed.App, closed.Section, closed.Menu,
            closed.Items.Select(i => i.Name == "btn_l1_door" ? i with { Text = "Open" } : i).ToList());
        var found = Bbs757EfbPress.Relocate(closed, opened, door);

        Assert.Equal("btn_l1_door", found?.Name);
        Assert.Equal("Open", found?.Text);
        Assert.Equal(door.Key, found?.Key);
        var doors = closed.Items.Where(i => i.Name.EndsWith("_door", StringComparison.Ordinal)).ToList();
        Assert.Equal(doors.Count, doors.Select(i => i.Key).Distinct().Count()); // no two share a key
    }

    [Fact]
    public void A_settings_choice_slider_taps_its_choice_from_the_track()
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        var boarding = Assert.Single(model.Items, i => i.Text == "BOARDING & LOAD RATE");
        var options = Bbs757EfbElements.OptionsFor(boarding, model.PageName)!;
        Assert.Equal(new[] { "Realistic", "Standard", "Quick", "Instant" }, options, StringComparer.OrdinalIgnoreCase);

        var track = Assert.IsType<Bbs757EfbSlider>(boarding.Slider);
        // The committed page has BlueBird's 15 px knob (captured 2026-11-07), and a tap centers the knob on the tap point (measured), so
        // the track is the knob center's range, 897 + 7.5 to 1197 - 7.5: the choices sit at the knob centers, the last one inside the box.
        Assert.True(track.KnobRead);
        Assert.Equal((904, 1189), (track.TrackLeft, track.TrackRight));
        // The 2026-10-27 measurement: x 1095 set QUICK and x 945 set REALISTIC; each choice is 100 px wide, so the tap lands within half of that.
        Assert.InRange(Bbs757EfbPress.ChoiceX(track, 2, options.Count), 1095 - 50, 1095 + 50);
        Assert.InRange(Bbs757EfbPress.ChoiceX(track, 0, options.Count), 945 - 50, 945 + 50);

        // With no knob read (a layout without the slider knob), the track is the slider's box, as before.
        var bare = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithoutKnobs(Bbs757EfbTreeJson.Load("51-settings-aircraft")))!;
        var fallback = Assert.Single(bare.Items, i => i.Text == "BOARDING & LOAD RATE").Slider!;
        Assert.Equal((897, 1197, false), (fallback.TrackLeft, fallback.TrackRight, fallback.KnobRead));
    }

    // ---- plan 22, Task 3: typing into a field on BlueBird's keyboard (TypeAsync) ----------------------------------------------

    /// <summary>BlueBird's keyboard where the reader finds it on PERF calc (25): its keys are where they are whether it shows or not.</summary>
    private static readonly Bbs757EfbKeyboard Kb = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25-navigation-perf-calc"))!.Keyboard!;

    private static Point KeyAt(string name) => Kb.Keys.First(k => k.Name == name).Hit;

    /// <summary>The TOD pause box's keypad where it was measured (Settings, Aircraft, 2026-11-07) and the fuel input dialog's: the keypad
    /// moves with its box.</summary>
    private static readonly Rectangle TodKeypad = new(1037, 266, 360, 430), FuelKeypad = new(595, 348, 360, 430);

    /// <summary>PERF calc's Airport box takes letters only and upper-cases them (measured).</summary>
    private static char? AirportTakes(char c) => char.IsLetter(c) ? char.ToUpperInvariant(c) : null;

    /// <summary>
    /// One of BlueBird's text boxes behind the press runner, typed as the tablet types it (measured 2026-11-07, [B757-29]). The box's tap
    /// brings the keyboard up for it, shown at once and settled <see cref="ReadyAfterMs"/> later, and puts the caret at
    /// <see cref="TapCaret"/>; a key tapped before it settled lands on the page beneath and the keyboard goes down
    /// (<see cref="KeysOnThePage"/>). A character key types its shown layer at the caret, then the caret goes to the end; backspace
    /// deletes before the caret, delete after it; the shift is one-shot; the box takes what <see cref="Takes"/> gives, and drops a key
    /// once full. The keypad's DEL clears the text and its OK closes it; it shows at <see cref="KeypadAt"/> while up, and its rectangle
    /// reads where it was last while down. A box below the viewport is drawn higher by each drag.
    /// </summary>
    private sealed class TypingTablet
    {
        public string Label { get; init; } = "AIRPORT";
        public string Name { get; init; } = "planning_perf_calc_airport_input";
        public string Text { get; set; } = "";
        public int MaxLength { get; init; } = 64;
        public bool Keypad { get; init; }
        public Rectangle KeypadAt { get; init; } = TodKeypad;
        public Func<char, char?> Takes { get; init; } = c => c;
        public int TapCaret { get; init; } = int.MaxValue;
        public int ReadyAfterMs { get; init; } = 1200;
        public bool OpensKeyboard { get; init; } = true;
        /// <summary>The box the keyboard comes up typing into, when another than this one, and whether that box opens the keypad.</summary>
        public string? UpFor { get; init; }
        public bool UpForKeypad { get; init; }
        /// <summary>The keyboard's keys read only while it is up (a torn read before the tap cannot tell which characters have a key).</summary>
        public bool KeysUnreadWhileDown { get; init; }
        /// <summary>A value box (fuel_left_qty_box): a field with no caret, maximum length or keypad flag in any read.</summary>
        public bool ValueBox { get; init; }
        /// <summary>The unit BlueBird draws beside a dialog's box ("Gal"), "" for none.</summary>
        public string Unit { get; init; } = "";
        /// <summary>After this many runs of typing taps the keyboard goes down (the pilot's own mouse, or BlueBird), null for never.</summary>
        public int? DownAfterRuns { get; init; }
        /// <summary>The box the keyboard is up and settled for before the press (the pilot's own mouse, or a typing that stopped), and
        /// whether that is the keypad (drawn at <see cref="KeypadAt"/>); null: down. The field's own tap brings it up for the field.</summary>
        public string? UpAtStartFor { get; init; }
        public bool UpAtStartKeypad { get; init; }
        /// <summary>Keys a press landed on while the keyboard was up for another box: typed into that box.</summary>
        public int KeysIntoAnother { get; private set; }
        private bool? _keypadNow;
        private bool _started;
        private bool KeypadUp => _keypadNow ?? (UpFor != null ? UpForKeypad : Keypad);

        /// <summary>The keyboard as it is before the press (<see cref="UpAtStartFor"/>), once.</summary>
        private void Start()
        {
            if (_started) return;
            _started = true;
            if (UpAtStartFor is not { } other) return;
            Shown = Ready = true;
            Target = other;
            _keypadNow = UpAtStartKeypad;
        }
        public bool RunsGoOut { get; init; } = true;
        public int ContentY { get; init; } = 156;
        public bool Scrollable { get; init; }
        /// <summary>The box is a dialog's (the fuel input dialog's), shown while <see cref="DialogUp"/>.</summary>
        public bool InQuestion { get; init; }
        public bool DialogUp { get; set; } = true;
        /// <summary>The page the box is on, built from this tablet's state (the box's text and caret, <see cref="Saved"/>) in place of
        /// PERF calc's one box: a capture's page (the AIRAC page's SimBrief row). The box is found in it by <see cref="Name"/>. Null:
        /// PERF calc's.</summary>
        public Func<TypingTablet, Bbs757EfbScreenModel>? PageFrom { get; init; }
        /// <summary>The keypad's OK saves the box, as it commits TOD pause's: <see cref="Saved"/> from then on.</summary>
        public bool OkSaves { get; init; }
        public bool Saved { get; private set; }

        public int Caret { get; private set; }
        public double Offset { get; private set; }
        public bool Shown { get; private set; }
        public bool Ready { get; private set; }
        public string Target { get; private set; } = "";
        public bool Numeric { get; private set; }
        public bool Shift { get; private set; }
        public bool Caps { get; private set; }
        public long Now { get; private set; }
        public int KeysOnThePage { get; private set; }
        public long ClosedAt { get; private set; } = -1;
        public List<string> Events { get; } = new();
        public List<Point> Taps { get; } = new();
        public List<IReadOnlyList<Point>> Runs { get; } = new();
        public List<int> Gaps { get; } = new();
        public List<(int X, int Y0, int Y1)> Drags { get; } = new();
        public List<string> Alerts { get; } = new();
        private long _upAt;

        private static readonly Bbs757EfbItem Cancel = new(Bbs757EfbKind.Button, "Cancel", new Rectangle(336, 381, 177, 46), new Point(424, 404), true, "q:CANCEL");
        private static readonly Bbs757EfbItem Insert = new(Bbs757EfbKind.Button, "Insert", new Rectangle(523, 381, 177, 46), new Point(611, 404), true, "q:INSERT");

        public Bbs757EfbItem Field()
        {
            int y = (int)Math.Round(ContentY - Offset);
            var box = new Rectangle(289, y, 150, 34);
            return new Bbs757EfbItem(Bbs757EfbKind.Field, Label, box, new Point(box.Left + 75, y + 17), true, Name, Value: Text, Name: Name,
                TextState: ValueBox ? null : new Bbs757EfbTextState(Caret, MaxLength, Keypad), Unit: Unit);
        }

        public Point FieldHit => Field().Hit;

        public Bbs757EfbScreenModel Page()
        {
            Start();
            var page = PageFrom != null ? PageFrom(this)
                : !InQuestion ? PageOf("NAVIGATION", "PERF CALC", Field())
                : DialogUp ? PageOf("AIRCRAFT", "FUEL") with
                {
                    Question = new Bbs757EfbQuestion("Insert desired fuel level (0-2176 Gal)", new[] { Cancel, Insert }) { Rows = new[] { Field() } },
                }
                : PageOf("AIRCRAFT", "FUEL");
            return page with
            {
                Keyboard = KeysUnreadWhileDown && !Shown ? null : Kb,
                KeyboardState = new Bbs757EfbKeyboardState(Shown, Ready, KeypadUp, Target, Numeric, Shift, Caps, Shown ? KeypadAt : FuelKeypad),
                Scrollable = Scrollable,
                Viewport = Bbs757EfbScreen.ContentArea,
            };
        }

        /// <summary>A press on the box as the window shows it now.</summary>
        public Bbs757EfbPressed Pressed()
        {
            var page = Page();
            return new(InQuestion ? page.Question!.Rows[0] : page.Items.First(i => i.Name == Name), page);
        }

        /// <summary>Where the box is drawn now; null when the page shows it no more.</summary>
        private Rectangle? FieldBox() => PageFrom == null ? Field().Box : PageFrom(this).Items.FirstOrDefault(i => i.Name == Name)?.Box;

        public Bbs757EfbPressRunner Runner() => new(new Bbs757EfbPressIo
        {
            Read = _ =>
            {
                Events.Add("read");
                if (Shown && !Ready && Now - _upAt >= ReadyAfterMs) Ready = true;
                return Task.FromResult<Bbs757EfbScreenModel?>(Page());
            },
            Tap = (p, _) =>
            {
                Events.Add($"tap {p.X},{p.Y}");
                Taps.Add(p);
                Land(p);
                return Task.FromResult(true);
            },
            TapRun = (points, gap, _) =>
            {
                Events.Add($"run {points.Count}");
                if (!RunsGoOut) return Task.FromResult(false);
                Runs.Add(points.ToList());
                Gaps.Add(gap);
                foreach (var p in points)
                {
                    Land(p);
                    Now += gap;
                }
                if (Runs.Count == DownAfterRuns) Down();
                return Task.FromResult(true);
            },
            Drag = (x, y0, y1, _) =>
            {
                Events.Add("drag");
                Drags.Add((x, y0, y1));
                Offset = Math.Clamp(Offset + (y0 - y1), 0, 1000);
                return Task.FromResult(true);
            },
            ReadInstalledCycle = () => null,
            ReadVar = (_, _) => Task.FromResult<double?>(null),
            ReadRegistered = (_, _) => Task.FromResult<double?>(null),
            ReadSetting = (_, _) => Task.FromResult<Bbs757Value?>(null),
            Alert = text => { Alerts.Add(text); Events.Add("alert " + text); },
            Delay = (ms, ct) => { ct.ThrowIfCancellationRequested(); Now += ms; return Task.CompletedTask; },
            NowMs = () => Now,
            ArmServiceEcho = _ => { },
            SettleServiceEcho = (_, _) => { },
            ArmGroundPowerEcho = () => { },
            SettleGroundPowerEcho = _ => { },
            Hooked = (_, _) => { },
        });

        /// <summary>A tap landing on the tablet.</summary>
        private void Land(Point p)
        {
            Start();
            // Shown and settled, the keyboard (or the keypad) takes a press over it as a key, whatever the page has beneath.
            if (Shown && Ready && (KeypadUp ? KeypadAt : Kb.Box).Contains(p)) { KeyLanded(p); return; }
            if ((!InQuestion || DialogUp) && FieldBox() is { } box && box.Contains(p))
            {
                if (!OpensKeyboard) return;
                if (!Shown) { Shown = true; Ready = false; _upAt = Now; }
                Target = UpFor ?? Name;
                _keypadNow = null;
                Caret = Math.Min(TapCaret, Text.Length);
                return;
            }
            if (!Shown) { Events.Add("page tap"); return; }
            if (!Ready) { KeysOnThePage++; Down(); return; }   // before it settled: on the page beneath, and the keyboard slides away
            KeyLanded(p);
        }

        private void KeyLanded(Point p)
        {
            if (Target != Name)
            {
                // Up for another box: only its close is modeled; any other key goes into that box.
                if (KeypadUp ? p == Bbs757EfbTyping.KeypadOk(KeypadAt) : p == KeyAt("btn_keyboard_down")) Down();
                else KeysIntoAnother++;
                return;
            }
            if (Keypad) KeypadKey(p);
            else Key(p);
        }

        private void Key(Point p)
        {
            var key = Kb.Keys.FirstOrDefault(k => k.Hit == p);
            switch (key?.Name)
            {
                case null: Events.Add("off the keys"); break;
                case "btn_backspace": Backspace(); break;
                case "btn_delete": if (Caret < Text.Length) Text = Text.Remove(Caret, 1); break;
                case "btn_shift": Shift = !Shift; if (Shift) Numeric = false; break;
                case "btn_double_shift": Caps = !Caps; break;
                case "btn_numeric_switch": Numeric = !Numeric; if (Numeric) Shift = false; break;
                case "btn_space": Type(' '); break;
                case "btn_keyboard_down": Down(); break;
                default:
                    string layer = Numeric ? key.Third : Shift || Caps ? key.Upper : key.Lower;
                    if (layer.Length == 1) Type(layer[0]);
                    Shift = false;
                    break;
            }
        }

        private void KeypadKey(Point p)
        {
            string[] rows = { "789", "456", "123", "-0." };
            int column = (p.X - KeypadAt.X) / 120, row = (int)Math.Floor((p.Y - KeypadAt.Y - 50) / 75.0);
            if (row is >= 0 and < 4 && column is >= 0 and < 3) Type(rows[row][column]);
            else if (row == 4 && column == 0) Backspace();
            else if (row == 4 && column == 1) { Text = ""; Caret = 0; }
            else if (row == 4 && column == 2) { Saved |= OkSaves; Down(); }
            else Events.Add("off the keypad");
        }

        private void Backspace()
        {
            if (Caret > 0) { Text = Text.Remove(Caret - 1, 1); Caret--; }
            Shift = false;
        }

        private void Type(char c)
        {
            if (Takes(c) is not char taken || Text.Length >= MaxLength) return;
            Text = Text.Insert(Caret, taken.ToString());
            Caret = Text.Length;
        }

        private void Down()
        {
            Shown = Ready = Numeric = Shift = Caps = false;
            Target = "";
            ClosedAt = Now;
        }
    }

    [Fact]
    public async Task Typing_replaces_the_whole_old_text()
    {
        // KSEA typed over KSEA1, the field's tap putting the caret after "KS": two backspaces clear before it, three deletes after it, read
        // from the box on the read that shows the keyboard up (before the tap it read 0, which would leave "KS").
        var tablet = new TypingTablet { Text = "KSEA1", TapCaret = 2 };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KSEA", CancellationToken.None);

        Assert.Equal("KSEA", tablet.Text);
        var run = Assert.Single(tablet.Runs);
        Assert.Equal(new[] { KeyAt("btn_backspace"), KeyAt("btn_backspace"), KeyAt("btn_delete"), KeyAt("btn_delete"), KeyAt("btn_delete") }, run.Take(5));
        Assert.Equal(Bbs757EfbTyping.TapGapMs, Assert.Single(tablet.Gaps));
        Assert.Equal(new[] { tablet.FieldHit, KeyAt("btn_keyboard_down") }, tablet.Taps);   // the box, then the keyboard's down key
        Assert.False(tablet.Shown);
        Assert.Equal(new[] { "Airport: KSEA" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task No_keyboard_types_nothing(string culture)
    {
        // BlueBird took the box's tap elsewhere: the keyboard never reads up, and after KeyboardReadyMaxMs nothing more is tapped.
        var tablet = new TypingTablet { Text = "KSEA", OpensKeyboard = false };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None));

        Assert.Equal(new[] { tablet.FieldHit }, tablet.Taps);
        Assert.Empty(tablet.Runs);
        Assert.Equal("KSEA", tablet.Text);
        Assert.Equal(new[] { "Airport: the tablet's keyboard did not come up." }, tablet.Alerts);
        Assert.InRange(tablet.Now, Bbs757EfbPressRunner.KeyboardReadyMaxMs, Bbs757EfbPressRunner.KeyboardReadyMaxMs + 200);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_keyboard_up_for_another_box_is_not_ready_and_is_put_down(string culture)
    {
        // Shown and settled, but typing into another box: no key is typed, and once the wait is out the keyboard is put down with its down
        // key, so it never stays over the page.
        var tablet = new TypingTablet { Text = "KSEA", UpFor = "planning_perf_calc_runway_length_input" };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None));

        Assert.Empty(tablet.Runs);
        Assert.Equal(new[] { tablet.FieldHit, KeyAt("btn_keyboard_down") }, tablet.Taps);
        Assert.False(tablet.Shown);
        Assert.Equal("KSEA", tablet.Text);
        Assert.Equal(new[] { "Airport: the tablet's keyboard did not come up." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_keypad_up_for_another_box_is_put_down_with_its_OK()
    {
        // The keypad up for the fuel input dialog's box (the read says the keypad shows, where it is drawn): put down with its OK.
        var tablet = new TypingTablet { Text = "KSEA", UpFor = "fuel_input_textbox", UpForKeypad = true, KeypadAt = FuelKeypad };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None);

        Assert.Empty(tablet.Runs);
        Assert.Equal(new[] { tablet.FieldHit, new Point(895, 735) }, tablet.Taps);
        Assert.False(tablet.Shown);
        Assert.Equal(new[] { "Airport: the tablet's keyboard did not come up." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_keyboard_that_never_settles_is_not_tapped()
    {
        // Shown but still sliding when the wait is out: a tap on its down key's place would land on the page beneath (measured), so none.
        var tablet = new TypingTablet { Text = "KSEA", ReadyAfterMs = 10_000 };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None);

        Assert.Equal(new[] { tablet.FieldHit }, tablet.Taps);
        Assert.Equal(0, tablet.KeysOnThePage);
        Assert.Equal(new[] { "Airport: the tablet's keyboard did not come up." }, tablet.Alerts);
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1940)]
    [InlineData(4900)]
    public async Task A_key_is_never_tapped_before_the_keyboard_is_ready(int settleMs)
    {
        // The keyboard reads shown from the box's tap and settled 1.1 to more than 1.9 s later (measured); a key tapped before then lands on
        // the page beneath and the keyboard slides away.
        var tablet = new TypingTablet { Text = "KSEA", ReadyAfterMs = settleMs };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None);

        Assert.Equal(0, tablet.KeysOnThePage);
        Assert.Equal("KPDX", tablet.Text);
        Assert.Equal(new[] { "Airport: KPDX" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_missing_key_types_nothing_and_taps_nothing(string culture)
    {
        // Found on the first fresh read, before any tap (plan 22's Global Constraints): the field below the viewport is not even dragged to.
        var tablet = new TypingTablet { Text = "KSEA", ContentY = 1300, Scrollable = true };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "KSé", CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Runs);
        Assert.False(tablet.Shown);
        Assert.Equal("KSEA", tablet.Text);
        Assert.Equal(new[] { "Airport: the tablet's keyboard has no é." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_missing_key_the_first_read_cannot_tell_closes_the_keyboard()
    {
        // The safety net: the keyboard's keys not read before the tap, so the read that shows the keyboard up finds the missing character;
        // the keyboard is put down again with nothing typed. (A box whose own state was not read is never typed: Task 5.)
        var tablet = new TypingTablet { Text = "KSEA", KeysUnreadWhileDown = true };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KSé", CancellationToken.None);

        Assert.Empty(tablet.Runs);
        Assert.Equal("KSEA", tablet.Text);
        Assert.Equal(new[] { tablet.FieldHit, KeyAt("btn_keyboard_down") }, tablet.Taps);
        Assert.False(tablet.Shown);
        Assert.Equal(new[] { "Airport: the tablet's keyboard has no é." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task The_field_reading_the_text_is_said_once(string culture)
    {
        // The Airport box upper-cases what it takes: "kpdx" reads KPDX, the text as typed but for case.
        var tablet = new TypingTablet { Text = "KSEA", MaxLength = 4, Takes = AirportTakes };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "kpdx", CancellationToken.None));

        Assert.Equal("KPDX", tablet.Text);
        Assert.Equal(new[] { "Airport: KPDX" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task The_field_reading_other_text_says_what_it_reads(string culture)
    {
        // The Airport box drops digits: "KPD1" reads KPD.
        var tablet = new TypingTablet { Text = "KSEA", MaxLength = 4, Takes = AirportTakes };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "KPD1", CancellationToken.None));

        Assert.Equal("KPD", tablet.Text);
        Assert.Equal(new[] { "Airport reads KPD." }, tablet.Alerts);
    }

    [Fact]
    public async Task Typing_the_text_already_there_taps_nothing()
    {
        var tablet = new TypingTablet { Text = "KSEA" };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KSEA", CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Runs);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task The_text_already_there_in_another_case_is_typed_and_said_from_the_first_read()
    {
        // Not the same text (ordinal), so it is typed; the box upper-cases it back to what it held, and the first read that shows it reading
        // the text says so, without waiting out ChangeMaxMs for a change.
        var tablet = new TypingTablet { Text = "KSEA", MaxLength = 4, Takes = AirportTakes };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "ksea", CancellationToken.None);

        Assert.Single(tablet.Runs);
        Assert.Equal(new[] { "Airport: KSEA" }, tablet.Alerts);
        Assert.InRange(tablet.Now - tablet.ClosedAt, 0, 500);
    }

    [Fact]
    public async Task A_field_below_the_viewport_is_scrolled_to_first()
    {
        var tablet = new TypingTablet { Text = "KSEA", ContentY = 1300, Scrollable = true };
        var pressed = tablet.Pressed();
        Assert.Null(Bbs757EfbPress.TapPoint(pressed.Page, pressed.Item));   // drawn below the viewport's bottom (1048)

        await tablet.Runner().TypeAsync(pressed, "KPDX", CancellationToken.None);

        Assert.NotEmpty(tablet.Drags);
        Assert.All(tablet.Drags, d => Assert.True(d.Y0 > d.Y1));   // the content moves up
        Assert.Equal(tablet.FieldHit, tablet.Taps[0]);              // tapped where it is drawn now
        Assert.True(Bbs757EfbScreen.ContentArea.Contains(tablet.Taps[0]));
        Assert.True(tablet.Events.IndexOf("drag") < tablet.Events.FindIndex(e => e.StartsWith("tap", StringComparison.Ordinal)));
        Assert.Equal("KPDX", tablet.Text);
        Assert.Equal(new[] { "Airport: KPDX" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_keypad_box_is_typed_on_the_keypad_where_it_shows_and_closed_with_its_OK(string culture)
    {
        // The TOD pause box opens BlueBird's numeric keypad below it ([1037,266 360x430] measured); the keypad moves with its box, so the
        // read that shows it up says where (down, its rectangle reads where it was last): its DEL clears the text, then the digits, then its
        // OK commits the box.
        var tablet = new TypingTablet { Label = "TOD PAUSE", Name = "tod_pause_textbox", Text = "40", Keypad = true, MaxLength = 3, ReadyAfterMs = 100 };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "120", CancellationToken.None));

        Assert.Equal("120", tablet.Text);
        Assert.Equal(Bbs757EfbTyping.PlanKeypad(TodKeypad, "120", out _)!.Taps, Assert.Single(tablet.Runs));
        Assert.Equal(new[] { tablet.FieldHit, new Point(1337, 653) }, tablet.Taps);   // the box, then the keypad's OK (row 4, column 2)
        Assert.False(tablet.Shown);
        Assert.Equal(new[] { "TOD pause: 120" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_letter_for_a_keypad_box_taps_nothing(string culture)
    {
        // The box opens the keypad (its state on the first fresh read), which has no letter: nothing tapped at all, the box not even tapped.
        var tablet = new TypingTablet { Label = "TOD PAUSE", Name = "tod_pause_textbox", Text = "40", Keypad = true, ReadyAfterMs = 100 };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "1a", CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Runs);
        Assert.Equal("40", tablet.Text);
        Assert.Equal(new[] { "TOD pause: the tablet's keyboard has no a." }, tablet.Alerts);
    }

    [Fact]
    public async Task The_text_already_there_below_the_viewport_drags_nothing()
    {
        var tablet = new TypingTablet { Text = "KSEA", ContentY = 1300, Scrollable = true };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KSEA", CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task Emptying_a_field_says_it_is_empty(string culture)
    {
        var tablet = new TypingTablet { Text = "KSEA" };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "", CancellationToken.None));

        Assert.Equal("", tablet.Text);
        Assert.Equal(new[] { "Airport is empty." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_field_that_takes_none_of_the_text_says_it_is_empty()
    {
        // The Airport box drops digits: "123" leaves it empty, said so rather than "Airport reads ."
        var tablet = new TypingTablet { Text = "KSEA", MaxLength = 4, Takes = AirportTakes };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "123", CancellationToken.None);

        Assert.Equal("", tablet.Text);
        Assert.Equal(new[] { "Airport is empty." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_run_that_does_not_go_out_stops_the_typing_and_closes_the_keyboard(string culture)
    {
        var tablet = new TypingTablet { Text = "KSEA", RunsGoOut = false };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None));

        Assert.Single(tablet.Events, e => e.StartsWith("run", StringComparison.Ordinal));   // no second run, no read-back
        Assert.Equal(new[] { tablet.FieldHit, KeyAt("btn_keyboard_down") }, tablet.Taps);
        Assert.False(tablet.Shown);
        Assert.Equal(new[] { "Airport not typed." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_long_text_goes_in_runs_of_at_most_forty_taps()
    {
        string text = new string('a', 45);
        var tablet = new TypingTablet { Text = "x" };

        await tablet.Runner().TypeAsync(tablet.Pressed(), text, CancellationToken.None);

        Assert.Equal(2, tablet.Runs.Count);
        Assert.All(tablet.Runs, r => Assert.InRange(r.Count, 1, Bbs757EfbTyping.MaxTapsPerRun));
        Assert.Equal(text, tablet.Text);
        Assert.Equal(new[] { "Airport: " + text }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_dialogs_box_is_typed_under_its_question_and_its_keypad_closed_with_OK(string culture)
    {
        // The fuel input dialog's box, a question's row, with the keypad up for it: found again under the same question, typed on its keypad,
        // and the keypad's OK closes the keypad alone (INSERT stays the pilot's answer: measured). The box holds its own text, its unit
        // apart (Task 5): read back as typed, under its name as the window shows it, the unit after it.
        var tablet = new TypingTablet
        {
            Label = "FUEL", Name = "fuel_input_textbox", Text = "2176", Unit = "Gal", Keypad = true, MaxLength = 8, KeypadAt = FuelKeypad,
            InQuestion = true, ReadyAfterMs = 100,
        };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "2000", CancellationToken.None));

        Assert.Equal("2000", tablet.Text);
        Assert.Equal(new[] { tablet.FieldHit, new Point(895, 735) }, tablet.Taps);
        Assert.Equal(new[] { "Fuel (Gal): 2000" }, tablet.Alerts);
    }

    [Fact]
    public async Task A_dialogs_box_holding_the_text_already_taps_nothing()
    {
        // With the unit apart from its text, the box holding the text typed is found so on the first read: nothing tapped, nothing said.
        var tablet = new TypingTablet { Label = "FUEL", Name = "fuel_input_textbox", Text = "2176", Unit = "Gal", Keypad = true, InQuestion = true };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "2176", CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_field_with_no_text_state_is_never_typed(string culture)
    {
        // A value box (fuel_left_qty_box: no caret in any read) is no text box: its tap would open BlueBird's fuel input dialog, so nothing
        // is tapped or dragged, and "not typed" is said (Task 3's review).
        var tablet = new TypingTablet
        {
            Label = "QUANTITY", Name = "fuel_left_qty_box", Text = "1539", ValueBox = true, ContentY = 1300, Scrollable = true,
        };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), "1000", CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Runs);
        Assert.Equal(new[] { "Quantity not typed." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_keyboard_that_went_down_between_runs_types_no_more(string culture)
    {
        // A long text goes in runs; before each run after the first the tablet is read again, and a keyboard no longer up for the field (the
        // pilot's own mouse put it down) takes no second run: a key's place is the page's then. Nothing to close; "not typed" is said.
        var tablet = new TypingTablet { Text = "x", DownAfterRuns = 1 };

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(tablet.Pressed(), new string('a', 45), CancellationToken.None));

        Assert.Single(tablet.Runs);
        Assert.Equal(new[] { tablet.FieldHit }, tablet.Taps);   // no close tap
        Assert.Equal(0, tablet.KeysOnThePage);
        Assert.Equal(new[] { "Airport not typed." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_dialogs_box_whose_dialog_closed_is_no_longer_on_the_tablet()
    {
        var tablet = new TypingTablet { Label = "FUEL", Name = "fuel_input_textbox", Text = "2176", Keypad = true, KeypadAt = FuelKeypad, InQuestion = true };
        var pressed = tablet.Pressed();
        tablet.DialogUp = false;

        await tablet.Runner().TypeAsync(pressed, "2000", CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Fuel is no longer on the tablet." }, tablet.Alerts);
    }

    // ---- a press under BlueBird's keyboard (the final review's I2) ----------------------------------------------------------------

    [Fact]
    public void A_point_under_the_shown_keyboard_or_keypad_is_under_it()
    {
        // Up, BlueBird's keyboard covers kb_grid ([0,648 1397x400]) and takes a press there as a key; the numeric keypad covers its own
        // rectangle, where that read draws it, and the QWERTY keyboard's place is then the page's.
        var page = PageOf("NAVIGATION", "PERF CALC") with { Keyboard = Kb };
        Assert.Equal(new Rectangle(0, 648, 1397, 400), Kb.Box);
        var qwerty = new Bbs757EfbKeyboardState(true, true, false, "planning_perf_calc_airport_input", false, false, false, Rectangle.Empty);
        Assert.True(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = qwerty }, new Point(700, 800)));
        Assert.True(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = qwerty }, new Point(10, 648)));
        Assert.False(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = qwerty }, new Point(700, 647)));
        Assert.True(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = qwerty with { Ready = false } }, new Point(700, 800)));   // sliding

        // Down, unread, or its keys unread (no box to tell): under nothing.
        Assert.False(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = qwerty with { Shown = false, Ready = false, Target = "" } }, new Point(700, 800)));
        Assert.False(Bbs757EfbPress.UnderKeyboard(page, new Point(700, 800)));
        Assert.False(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = qwerty, Keyboard = null }, new Point(700, 800)));

        var keypad = qwerty with { Target = "fuel_input_textbox", Keypad = true, KeypadBox = FuelKeypad };
        Assert.True(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = keypad }, new Point(952, 404)));    // the fuel dialog's INSERT
        Assert.False(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = keypad }, new Point(1100, 800)));  // kb_grid's, not the keypad's
        Assert.False(Bbs757EfbPress.UnderKeyboard(page with { KeyboardState = keypad with { KeypadBox = Rectangle.Empty } }, new Point(952, 404)));
    }

    [Fact]
    public async Task A_field_under_the_keyboard_up_for_another_box_is_tapped_once_it_is_down()
    {
        // The keyboard is up for the runway length box (the pilot's mouse, or a typing that stopped) and the Airport box lies under it: its
        // tap would be a key typed into the runway length box. The keyboard is put down first with its down key (it keeps the text), the
        // box found again and tapped, and the typing goes on as ever.
        var tablet = new TypingTablet { Text = "KSEA", ContentY = 800, UpAtStartFor = "planning_perf_calc_runway_length_input" };
        var pressed = tablet.Pressed();
        Assert.True(Bbs757EfbPress.UnderKeyboard(pressed.Page, tablet.FieldHit));

        await tablet.Runner().TypeAsync(pressed, "KPDX", CancellationToken.None);

        Assert.Equal(new[] { KeyAt("btn_keyboard_down"), tablet.FieldHit, KeyAt("btn_keyboard_down") }, tablet.Taps);
        Assert.Equal(0, tablet.KeysIntoAnother);
        Assert.Equal(0, tablet.KeysOnThePage);
        Assert.Equal("KPDX", tablet.Text);
        Assert.Equal(new[] { "Airport: KPDX" }, tablet.Alerts);
    }

    [Fact]
    public async Task A_field_under_a_keypad_up_for_another_box_is_tapped_once_its_OK_closed_it()
    {
        var keypadAt = new Rectangle(250, 100, 360, 430);   // over the Airport box (289,156 150x34)
        var tablet = new TypingTablet { Text = "KSEA", KeypadAt = keypadAt, UpAtStartFor = "tod_pause_textbox", UpAtStartKeypad = true };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None);

        Assert.Equal(new[] { Bbs757EfbTyping.KeypadOk(keypadAt)!.Value, tablet.FieldHit, KeyAt("btn_keyboard_down") }, tablet.Taps);
        Assert.Equal(0, tablet.KeysIntoAnother);
        Assert.Equal("KPDX", tablet.Text);
        Assert.Equal(new[] { "Airport: KPDX" }, tablet.Alerts);
    }

    [Fact]
    public async Task A_field_clear_of_the_keyboard_up_for_another_box_closes_nothing()
    {
        // The Airport box (y 156) is not under kb_grid: its tap takes the keyboard over (BlueBird types into the box tapped), nothing is
        // closed first.
        var tablet = new TypingTablet { Text = "KSEA", UpAtStartFor = "planning_perf_calc_runway_length_input" };

        await tablet.Runner().TypeAsync(tablet.Pressed(), "KPDX", CancellationToken.None);

        Assert.Equal(new[] { tablet.FieldHit, KeyAt("btn_keyboard_down") }, tablet.Taps);
        Assert.Equal("KPDX", tablet.Text);
        Assert.Equal(new[] { "Airport: KPDX" }, tablet.Alerts);
    }

    /// <summary>The trees with the control named <paramref name="name"/> replaced by <paramref name="by"/>.</summary>
    private static IReadOnlyList<Bbs757EfbMemory.App> ReplacedNode(IReadOnlyList<Bbs757EfbMemory.App> apps, string name, Bbs757EfbMemory.Node by)
    {
        Bbs757EfbMemory.Node Walk(Bbs757EfbMemory.Node n) => n.Name == name ? by : n with { Children = n.Children.Select(Walk).ToList() };
        return apps.Select(a => a with { Controls = a.Controls.Select(Walk).ToList() }).ToList();
    }

    /// <summary>
    /// The AIRAC page (55, the scrub's made-up values: B757-24) with its SimBrief row as the tablet shows it. With no ID saved, its input
    /// cell holds the empty text box (<c>simbrief_id_textbox</c>, [763.5,95 170x44], the keypad's, at most 8: measured), named by the row's
    /// label. Once the keypad's OK saved it, the box is hidden and the cell shows <c>simbrief_id_saved_label</c> holding the ID typed
    /// (<paramref name="savedLine"/>), or nothing at all (false: no line takes the box's place).
    /// </summary>
    private static Bbs757EfbScreenModel SimBriefRow(TypingTablet t, bool savedLine = true)
    {
        var apps = Bbs757EfbTreeJson.Load("55-settings-airac-data");
        apps = !t.Saved
            ? ReplacedNode(apps, "simbrief_id_saved_label", new Bbs757EfbMemory.Node("textbox", "simbrief_id_textbox", t.Text, 763.5f, 95, 170, 44, 0,
                Array.Empty<Bbs757EfbMemory.Node>(), Caret: t.Caret, MaxLength: 8, Keypad: true))
            : savedLine ? Bbs757EfbTreeJson.WithText(apps, "simbrief_id_saved_label", t.Text)
            : ReplacedNode(apps, "simbrief_id_saved_label", new Bbs757EfbMemory.Node("other", "simbrief_id_saved_label", "", 763.5f, 104, 170, 26, 0,
                Array.Empty<Bbs757EfbMemory.Node>()));
        return Bbs757EfbModelReader.Read(apps)!;
    }

    private static TypingTablet SimBriefTablet(bool savedLine = true) => new()
    {
        Name = "simbrief_id_textbox", Text = "", Keypad = true, MaxLength = 8, ReadyAfterMs = 100, OkSaves = true,
        PageFrom = t => SimBriefRow(t, savedLine),
    };

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_field_its_typing_hid_is_read_back_from_the_line_its_row_shows(string culture)
    {
        // The final review's I3: the empty SimBrief row saves on the keypad's OK (as TOD's OK commits), which hides its box, and the row then
        // shows the saved ID's line. Found again by the row's label the box was named by (B757-22), the line is the read-back, said as soon
        // as it shows, where "is no longer on the tablet" after a typing that worked sounded like a failure. The ID is the scrub's 123456.
        var tablet = SimBriefTablet();
        var pressed = tablet.Pressed();
        Assert.Equal("SIMBRIEF ID number", Bbs757EfbPress.SpokenName(pressed.Page, pressed.Item));

        await TestCulture.Run(culture, () => tablet.Runner().TypeAsync(pressed, "123456", CancellationToken.None));

        Assert.True(tablet.Saved);
        Assert.DoesNotContain(tablet.Page().Items, i => i.Name == "simbrief_id_textbox");
        Assert.Contains(tablet.Page().Items, i => i.Kind == Bbs757EfbKind.Row && i.Text == pressed.Item.Text && i.Value == "123456");
        Assert.Equal(new[] { pressed.Item.Hit, Bbs757EfbTyping.KeypadOk(TodKeypad)!.Value }, tablet.Taps);   // the box, then the keypad's OK
        Assert.Equal(new[] { "SIMBRIEF ID number: 123456" }, tablet.Alerts);
        Assert.InRange(tablet.Now - tablet.ClosedAt, 0, 500);   // at its first read, not after ChangeMaxMs
    }

    [Fact]
    public async Task A_field_gone_after_its_typing_with_no_line_in_its_place_is_no_longer_on_the_tablet()
    {
        // With no line of its row in the box's place, the read-back waits it out (ChangeMaxMs) and says the field is gone, as before.
        var tablet = SimBriefTablet(savedLine: false);

        await tablet.Runner().TypeAsync(tablet.Pressed(), "123456", CancellationToken.None);

        Assert.True(tablet.Saved);
        Assert.Equal(new[] { "SIMBRIEF ID number is no longer on the tablet." }, tablet.Alerts);
        Assert.True(tablet.Now - tablet.ClosedAt >= Bbs757EfbPressRunner.ChangeMaxMs);
    }

    [Fact]
    public async Task A_click_on_a_field_taps_nothing()
    {
        // A field is typed into (TypeAsync), never clicked: a tap would bring the keyboard up with nothing to type.
        var tablet = new TypingTablet { Text = "KSEA" };

        await tablet.Runner().ClickAsync(tablet.Pressed(), CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_typing_texts_are_the_same_in_any_Windows_culture(string culture)
    {
        // Plain words joined: the value is the tablet's own text, said as it reads ("29.92" stays 29.92 under de-DE and sv-SE).
        var texts = TestCulture.Run(culture, () => new[]
        {
            Bbs757EfbPress.NoKeyboardText("Airport"),
            Bbs757EfbPress.NoKeyText("Airport", 'é'),
            Bbs757EfbPress.TypedText("Airport", "KPDX"),
            Bbs757EfbPress.TypedText("Pressure (inHg)", "29.92"),
            Bbs757EfbPress.TypedOtherText("Airport", "KPD"),
            Bbs757EfbPress.NotTypedText("Airport"),
            Bbs757EfbPress.EmptyText("Airport"),
        });

        Assert.Equal(new[]
        {
            "Airport: the tablet's keyboard did not come up.",
            "Airport: the tablet's keyboard has no é.",
            "Airport: KPDX",
            "Pressure (inHg): 29.92",
            "Airport reads KPD.",
            "Airport not typed.",
            "Airport is empty.",
        }, texts);
    }

    public static TheoryData<string, char, string> NamedCharacters
    {
        get
        {
            // The final review's M3: the alert gave the raw character, so a space read "has no  ." and, at NVDA's default punctuation
            // level, a comma or an underscore was not spoken at all. A space and every symbol NVDA does not speak at that level are named;
            // letters, digits and the symbols it does speak (% & * + . / = @) stay as they are.
            var data = new TheoryData<string, char, string>();
            foreach (var culture in new[] { "en-US", "de-DE", "sv-SE" })
            {
                data.Add(culture, ' ', "space");
                data.Add(culture, ',', "comma");                    // the keypad's: a decimal comma typed into the TOD distance
                data.Add(culture, '_', "underscore");
                data.Add(culture, '\t', "tab");
                data.Add(culture, '\u00A0', "no-break space");
                data.Add(culture, '<', "less-than sign");
                data.Add(culture, '~', "tilde");
                data.Add(culture, '\'', "apostrophe");
                data.Add(culture, 'é', "é");
                data.Add(culture, 'a', "a");
                data.Add(culture, '/', "/");
                data.Add(culture, '@', "@");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NamedCharacters))]
    public void A_character_no_key_carries_is_named_where_the_screen_reader_would_say_nothing(string culture, char character, string said)
    {
        Assert.Equal("Airport: the tablet's keyboard has no " + said + ".",
            TestCulture.Run(culture, () => Bbs757EfbPress.NoKeyText("Airport", character)));
    }

    [Fact]
    public void Every_ascii_symbol_is_named_or_one_the_screen_reader_says_at_its_default_level()
    {
        // NVDA's default punctuation level ("some") speaks % & * + . / = @ (its symbol dictionary); every other ASCII symbol is named in
        // words, each with its own name.
        var kept = "%&*+./=@";
        var names = new Dictionary<string, char>();
        for (char c = '!'; c <= '~'; c++)
        {
            if (char.IsLetterOrDigit(c)) continue;
            string text = Bbs757EfbPress.NoKeyText("Airport", c);
            string name = text["Airport: the tablet's keyboard has no ".Length..^1];
            if (kept.Contains(c)) { Assert.Equal(c.ToString(), name); continue; }
            Assert.True(name.Length > 1 && name.All(ch => char.IsLetter(ch) || ch is ' ' or '-'), $"'{c}' is said as '{name}'");
            Assert.True(names.TryAdd(name, c), $"'{c}' and '{names.GetValueOrDefault(name)}' are both said as '{name}'");
        }
    }
}
