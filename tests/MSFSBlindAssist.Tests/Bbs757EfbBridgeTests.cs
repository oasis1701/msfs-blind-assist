using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 tablet window presses what the pilot presses, reading the tablet from the module's memory (the owner's ruling of
/// 2026-10-29: the window refuses nothing; only the press runner's precision checks may stop a tap). The press runner
/// (<see cref="Bbs757EfbPressRunner"/>) runs over a fake tablet: a page, BlueBird's settings file and variables, and a clock its
/// waits move; a tap and a scripted page change. Everything spoken is checked under en-US, de-DE and sv-SE (CI runs under en-US).
/// Pure element-building is in <see cref="Bbs757EfbElementsTests"/>.
/// </summary>
public class Bbs757EfbBridgeTests
{
    private static readonly IReadOnlyList<Bbs757EfbExtraElement> NoExtras = Array.Empty<Bbs757EfbExtraElement>();

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private const string Ellipsis = "…";
    private const string Egpws = "options.egpws_callouts.";

    private static Point Centre(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    private static Bbs757EfbItem Tile(string text, string key, Rectangle box, Bbs757EfbTint tint = Bbs757EfbTint.None) =>
        new(Bbs757EfbKind.Button, text, box, Centre(box), true, key, tint);

    private static Bbs757EfbItem Switch(string text, string key, bool? on, string value = "", int y = 333) =>
        new(Bbs757EfbKind.Toggle, text, new Rectangle(273, y, 237, 33), new Point(1170, y + 16), true, key, On: on, Value: value);

    private static Bbs757EfbScreenModel PageOf(string app, string section, params Bbs757EfbItem[] items) =>
        new(app, section, Array.Empty<Bbs757EfbItem>(), items);

    private static Bbs757EfbScreenModel Ground(params Bbs757EfbItem[] items) => PageOf("AIRCRAFT", "GROUND SERVICES", items);

    private static Bbs757EfbItem ReqFuelTruck() => Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1132, 577, 196, 42));
    private static Bbs757EfbItem ReqCatering() => Tile("Req catering", "btn:|REQ CATERING@45,36", new Rectangle(912, 723, 196, 42));
    private static Bbs757EfbItem ReqTug(int top = 577) => Tile("Req PB tug", "btn:|REQ PB TUG@61," + (top + 21) / 20, new Rectangle(1132, top, 196, 42));

    /// <summary>A ground-service tile (keyed so <see cref="Bbs757EfbScreen.KeyWords"/> gives the service's word).</summary>
    private static Bbs757EfbItem FuelTruck(string state) =>
        Tile("Fuel truck " + state, "btn:|FUEL TRUCK@61,29", new Rectangle(1132, 577, 196, 42),
            state == "on" ? Bbs757EfbTint.Green : Bbs757EfbTint.None);
    private static Bbs757EfbItem Gpu(string state) => Tile("GPU " + state, "btn:|GPU@16,16", new Rectangle(300, 300, 196, 42));

    private static string Tap(Bbs757EfbItem item) => $"tap {item.Hit.X},{item.Hit.Y}";

    private static Bbs757EfbItem Countdown(string text) =>
        new(Bbs757EfbKind.Text, text, new Rectangle(1029, 0, 106, 28), new Point(1082, 14), false, Bbs757EfbTopBar.DepartureKey);

    [Fact]
    public void An_alert_is_the_first_element_and_assertive()
    {
        var b = Bbs757EfbElements.Build(null, "The tablet could not be read.", NoExtras);
        Assert.Equal("BlueBird tablet", b.Data["page"]);
        Assert.Equal("alert", b.Data["items.0.kind"]);
        Assert.Equal("assertive", b.Data["items.0.live"]);
        Assert.Equal("alert", b.Data["items.0.key"]);
    }

    /// <summary>
    /// A tablet behind the press runner: the page it shows now (a memory read returns it), BlueBird's settings file, variables and
    /// installed nav data cycle, a clock the runner's waits move, and what a tap does. What the runner did is kept in order (reads,
    /// cycle reads, taps, alerts, echoes, hooks); <see cref="Flow"/> is that order without the reads, which a press makes many of.
    /// </summary>
    private sealed class FakeTablet
    {
        public FakeTablet(Bbs757EfbScreenModel page) => Page = page;

        public Bbs757EfbScreenModel Page { get; private set; }
        public long Now { get; private set; }
        public bool Reachable { get; set; } = true;
        public int? Cycle { get; set; }
        public Action<Point>? OnTap { get; set; }
        public Action<int, int, int>? OnDrag { get; set; }
        /// <summary>Runs at each memory read, before the page is returned: the tablet changing between two reads with no tap.</summary>
        public Action? OnRead { get; set; }
        public bool DragReachable { get; set; } = true;
        public List<(int X, int Y0, int Y1)> Drags { get; } = new();
        public Dictionary<string, Bbs757Value> File { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> Vars { get; } = new(StringComparer.Ordinal);
        public List<string> Events { get; } = new();
        public List<Point> Taps { get; } = new();
        public List<string> Alerts { get; } = new();
        public List<string> SettingsRead { get; } = new();

        /// <summary>The tablet shows another page.</summary>
        public void Show(Bbs757EfbScreenModel page) => Page = page;

        /// <summary>A press on <paramref name="item"/> of the page shown now.</summary>
        public Bbs757EfbPressed Press(Bbs757EfbItem item) => new(item, Page);

        /// <summary>The order the runner acted in, without the reads.</summary>
        public List<string> Flow => Events.Where(e => e != "read").ToList();

        private static string Said(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "unread";

        public Bbs757EfbPressRunner Runner() => new(new Bbs757EfbPressIo
        {
            ReadInstalledCycle = () => { Events.Add("cycle"); return Cycle; },
            Read = _ => { Events.Add("read"); OnRead?.Invoke(); return Task.FromResult<Bbs757EfbScreenModel?>(Page); },
            Tap = (p, _) =>
            {
                Events.Add($"tap {p.X},{p.Y}");
                if (!Reachable) return Task.FromResult(false);
                Taps.Add(p);
                OnTap?.Invoke(p);
                return Task.FromResult(true);
            },
            TapRun = (points, _, _) =>
            {
                Events.Add($"run {points.Count}");
                return Task.FromResult(Reachable);
            },
            Drag = (x, y0, y1, _) =>
            {
                Events.Add($"drag {x},{y0},{y1}");
                if (!DragReachable) return Task.FromResult(false);
                Drags.Add((x, y0, y1));
                OnDrag?.Invoke(x, y0, y1);
                return Task.FromResult(true);
            },
            ReadVar = (name, _) => Task.FromResult<double?>(Vars.TryGetValue(name, out var v) ? v : null),
            ReadRegistered = (key, _) => Task.FromResult<double?>(Vars.TryGetValue(key, out var v) ? v : null),
            ReadSetting = (s, _) => { SettingsRead.Add(s.Path); return Task.FromResult<Bbs757Value?>(File.TryGetValue(s.Path, out var v) ? v : null); },
            Alert = text => { Alerts.Add(text); Events.Add("alert " + text); },
            Delay = (ms, ct) => { ct.ThrowIfCancellationRequested(); Now += ms; return Task.CompletedTask; },
            NowMs = () => Now,
            ArmServiceEcho = s => Events.Add("arm " + s.Key),
            SettleServiceEcho = (s, v) => Events.Add("settle " + s.Key + " " + Said(v)),
            ArmGroundPowerEcho = () => Events.Add("arm GPU"),
            SettleGroundPowerEcho = v => Events.Add("settle GPU " + Said(v)),
            Hooked = (hook, cycle) => Events.Add("hook " + hook + (cycle is int c ? " " + c.ToString(CultureInfo.InvariantCulture) : "")),
        });
    }

    // ---- where a press taps -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_tab_from_the_real_maintenance_page_is_relocated_and_tapped()
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var hydraulic = Assert.Single(page.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Name == "maintenance_hydraulic_tab_button");
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(hydraulic), CancellationToken.None);

        Assert.Equal(new[] { hydraulic.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_press_reads_relocates_and_taps_the_tile_in_place()
    {
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        tablet.OnTap = _ => tablet.Show(Ground(Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@56,28",
            new Rectangle(1131, 576, 197, 43)), ReqCatering()));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { tile.Hit }, tablet.Taps);           // tapped where the fresh read placed it
        Assert.Equal(new[] { "Requesting" + Ellipsis }, tablet.Alerts);   // its new words, once
    }

    [Fact]
    public async Task A_press_on_a_moved_control_taps_the_new_point()
    {
        // A heading appeared above the tiles, so the tile is 25 px lower under another key; the fresh read finds it there.
        var tile = ReqTug();
        var tablet = new FakeTablet(Ground(tile));
        var press = tablet.Press(tile);
        var moved = ReqTug(top: 602);
        tablet.Show(Ground(new Bbs757EfbItem(Bbs757EfbKind.Heading, "Services", new Rectangle(273, 540, 120, 30), new Point(333, 555), false, "h:SERVICES"), moved));
        tablet.OnTap = _ => tablet.Show(Ground(Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@61,31", moved.Box)));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.NotEqual(tile.Key, moved.Key);
        Assert.Equal(new[] { moved.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Requesting" + Ellipsis }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_press_on_a_vanished_control_taps_nothing_and_says_so(string culture)
    {
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        tablet.Show(Ground(ReqCatering()) with { Notices = new[] { new Bbs757EfbNotice(1, "GPU connected.", "14:17Z") } });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Req fuel truck is no longer on the tablet." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_that_turned_into_text_is_gone(string culture)
    {
        // START BOARDING became "Boarding… 10 min" after the page was read: text is no control.
        var start = new Bbs757EfbItem(Bbs757EfbKind.Button, "Start boarding", new Rectangle(1142, 3, 180, 28), new Point(1231, 17), true,
            Bbs757EfbTopBar.BoardingKey, Bbs757EfbTint.Green);
        var tablet = new FakeTablet(Ground(ReqFuelTruck()) with { TopBar = new[] { start } });
        var press = tablet.Press(start);
        tablet.Show(Ground(ReqFuelTruck()) with
        {
            TopBar = new[] { new Bbs757EfbItem(Bbs757EfbKind.Text, "Boarding" + Ellipsis + " 10 min", start.Box, new Point(1232, 17), false, Bbs757EfbTopBar.BoardingKey) },
        });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.DoesNotContain(tablet.Flow, e => e.StartsWith("hook", StringComparison.Ordinal));
        Assert.Equal(new[] { "Start boarding is no longer on the tablet." }, tablet.Alerts);
    }

    // ---- questions --------------------------------------------------------------------------------------------------

    private const string DefuelQuestion = "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?";
    private const string UnitsQuestion = "Mismatch between flight plan and aircraft weight units! Do you want to switch the aircraft unit system?";

    private static Bbs757EfbScreenModel FuelAsked(string question, Bbs757EfbItem answer) =>
        PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45)))
            with { Question = new Bbs757EfbQuestion(question, new[] { answer }) };

    private static readonly Bbs757EfbItem YesAnswer = Tile("Yes", "q:YES", new Rectangle(336, 591, 177, 57), Bbs757EfbTint.Green);

    [Fact]
    public async Task A_control_a_new_question_covers_is_not_tapped()
    {
        var import = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "FUEL", import));
        var press = tablet.Press(import);
        tablet.Show(PageOf("AIRCRAFT", "FUEL", import) with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { YesAnswer }) });

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_question_over_the_page_holds_back_a_page_press()
    {
        // The Fuel page read with no dialog (11b), then the fuel input dialog up over it (11c, captured live 2026-11-07): a press of the
        // page's IMPORT FROM SIMBRIEF made on the first read taps nothing and says nothing (the tablet takes no tap under its dialog, and
        // the read that found the dialog said it). The dialog's own answer is tapped where BlueBird draws it.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import"))!;
        var import = Assert.Single(page.Items, i => i.Kind == Bbs757EfbKind.Button && i.Name == "fuel_up_simbrief");
        var dialog = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog"))!;
        var tablet = new FakeTablet(page);
        var press = tablet.Press(import);
        tablet.Show(dialog);

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);

        // The window offers no page control under it: only the dialog's two answers press.
        var built = Bbs757EfbElements.Build(dialog, null, NoExtras);
        Assert.Equal(new[] { "q:CANCEL", "q:INSERT" }, built.Actions.OrderBy(a => a.Key).Select(a => a.Value.Item!.Key));

        var cancel = dialog.Question!.Buttons[0];
        tablet.OnTap = _ => tablet.Show(page);
        await tablet.Runner().ClickAsync(tablet.Press(cancel), CancellationToken.None);
        Assert.Equal(new[] { new Point(424, 404) }, tablet.Taps);
    }

    [Fact]
    public async Task A_value_box_press_taps_it_and_its_dialog_comes_up()
    {
        // The Fuel page (11b): its left quantity box, BlueBird's button, pressed in the window is tapped at its tap point, as a sighted pilot
        // taps it, and the fuel input dialog comes up over the page (11c, captured with it up). The press says nothing: the read that finds
        // the question says it (Bbs757EfbReadNews), and the dialog's box and answers are then the window's.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import"))!;
        var box = Assert.Single(page.Items, i => i.Key == "fuel_left_qty_box");
        Assert.True(box.ValueBox);
        var tablet = new FakeTablet(page);
        tablet.OnTap = _ => tablet.Show(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog"))!);

        await tablet.Runner().ClickAsync(tablet.Press(box), CancellationToken.None);

        Assert.Equal(new[] { box.Hit }, tablet.Taps);
        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Alerts);
        Assert.NotNull(tablet.Page.Question);
    }

    [Fact]
    public async Task A_value_box_press_that_changes_nothing_says_so()
    {
        // A tap BlueBird answered with no dialog (the page as it was): said once, as any button's press that changed nothing.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import"))!;
        var box = Assert.Single(page.Items, i => i.Key == "fuel_left_qty_box");
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(box), CancellationToken.None);

        Assert.Equal(new[] { box.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Quantity: no change on the tablet." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_yes_pressed_on_one_question_is_never_tapped_on_another()
    {
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, YesAnswer));
        var press = tablet.Press(YesAnswer);
        tablet.Show(FuelAsked(UnitsQuestion, YesAnswer));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_yes_whose_question_was_answered_meanwhile_is_gone(string culture)
    {
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, YesAnswer));
        var press = tablet.Press(YesAnswer);
        tablet.Show(PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45))));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Yes is no longer on the tablet." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_yes_is_tapped_while_its_question_reads_the_same()
    {
        var tablet = new FakeTablet(FuelAsked(DefuelQuestion, YesAnswer));
        var press = tablet.Press(YesAnswer);
        tablet.OnTap = _ => tablet.Show(PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45))));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { YesAnswer.Hit }, tablet.Taps);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tap_that_cannot_run_says_so_and_starts_no_hook(string culture)
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch)) { Reachable = false };

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(tablet.Press(fetch), CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "The tablet cannot be reached, so nothing was tapped." }, tablet.Alerts);
        Assert.DoesNotContain(tablet.Flow, e => e.StartsWith("hook", StringComparison.Ordinal));
    }

    // ---- outcomes ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tile_that_changed_nothing_says_so(string culture)
    {
        var tile = ReqTug();
        var tablet = new FakeTablet(Ground(tile));
        var press = tablet.Press(tile);
        // The tap changes nothing on the page.

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { tile.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Req PB tug: no change on the tablet." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_relabeled_under_a_new_key_is_said_once_in_its_new_words(string culture)
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch));
        var press = tablet.Press(fetch);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "ACARS DASHBOARD", Tile("Refresh SIMBRIEF ofp", "btn:|REFRESH SIMBRIEF OFP@42,40", fetch.Box)));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { "Refresh SIMBRIEF ofp" }, tablet.Alerts);
    }

    [Fact]
    public async Task A_press_whose_control_kept_its_words_but_changed_the_page_says_nothing()
    {
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(Ground(tile, ReqCatering()));
        var press = tablet.Press(tile);
        // The tile kept its words, but another tile's did change: the press's own control says nothing.
        tablet.OnTap = _ => tablet.Show(Ground(tile, Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@45,36", new Rectangle(912, 723, 196, 42))));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { tile.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_press_that_brought_up_a_question_says_nothing_over_it()
    {
        var import = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "FUEL", import));
        var press = tablet.Press(import);
        tablet.OnTap = _ => tablet.Show(FuelAsked(DefuelQuestion, YesAnswer));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { import.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);   // the read that found the question says it, not the press
    }

    // ---- navigation -------------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Menu(string word) =>
        new(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(word), new Rectangle(0, 142, 250, 60), new Point(125, 172), true, "menu:" + word);

    private static Bbs757EfbItem HomeApp(string app) =>
        new(Bbs757EfbKind.Menu, Bbs757EfbVocabulary.Pretty(app), new Rectangle(273, 57, 110, 110), new Point(328, 112), true, "app:" + app);

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_menu_press_whose_page_never_comes_says_so(string culture)
    {
        var fuel = Menu("FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(fuel);
        // The tap changes nothing: the page never becomes "Aircraft, Fuel".

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { fuel.Hit }, tablet.Taps);
        Assert.Equal(new[] { "The tablet did not show Fuel. Try again, or press F5 to read the tablet again." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_menu_press_whose_section_is_not_read_says_nothing(string culture)
    {
        var fuel = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(130, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, new[] { ReqFuelTruck() }));
        var press = tablet.Press(fuel);
        tablet.OnTap = _ => tablet.Show(new Bbs757EfbScreenModel("AIRCRAFT", "", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
        Assert.True(tablet.Now >= Bbs757EfbPressRunner.NavigateMaxMs, "read again for four seconds");
    }

    [Fact]
    public async Task A_menu_press_whose_page_reads_unknown_says_nothing()
    {
        var fuel = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(130, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, new[] { ReqFuelTruck() }));
        var press = tablet.Press(fuel);
        tablet.OnTap = _ => tablet.Show(new Bbs757EfbScreenModel("", "", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_home_press_that_lands_in_another_app_says_so()
    {
        var home = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", new Rectangle(5, 5, 40, 30), new Point(25, 20), true, "btn:HOME");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("NAVIGATION", "", new[] { home }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(home);

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "The tablet did not show Home. Try again, or press F5 to read the tablet again." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_menu_press_whose_page_comes_late_says_nothing()
    {
        var fuel = Menu("FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(fuel);
        int taps = 0;
        tablet.OnTap = _ => { };
        // The page follows two reads after the tap.
        var runner = tablet.Runner();
        tablet.OnTap = _ => taps++;
        // Script the page to change to Fuel after the tap.
        tablet.OnTap = _ => tablet.Show(new Bbs757EfbScreenModel("AIRCRAFT", "FUEL", new[] { fuel with { Selected = true } }, Array.Empty<Bbs757EfbItem>()));

        await runner.ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { fuel.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_side_menu_entry_pressed_again_on_its_own_page_taps_nothing_and_says_nothing()
    {
        var fuel = Menu("FUEL");
        // The tablet already shows Aircraft, Fuel: the first press took it there, this one is done.
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "FUEL", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));

        await tablet.Runner().ClickAsync(tablet.Press(fuel), CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_home_screen_app_pressed_again_once_it_opened_taps_nothing_and_says_nothing()
    {
        var aircraft = HomeApp("AIRCRAFT");
        var tablet = new FakeTablet(Ground(ReqFuelTruck()));   // already in the AIRCRAFT app

        await tablet.Runner().ClickAsync(tablet.Press(aircraft), CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_side_menu_entry_on_a_page_that_went_elsewhere_is_gone(string culture)
    {
        var fuel = Menu("FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { fuel }, Array.Empty<Bbs757EfbItem>()));
        var press = tablet.Press(fuel);
        // The tablet went to another app entirely: the entry is not there.
        tablet.Show(PageOf("SETTINGS", "GENERAL", Switch("Lock keyboard to EFB", "tgl:LOCK KEYBOARD TO EFB", on: true)));

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Fuel is no longer on the tablet." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_tab_press_is_judged_as_a_button_not_as_a_page()
    {
        // A tab (Kind.Tab, keyed tab:) changes the list under it, not the page name: no navigation, and the reader only flips Selected.
        var tab = new Bbs757EfbItem(Bbs757EfbKind.Tab, "Metar", new Rectangle(300, 100, 120, 40), new Point(360, 120), true, "tab:METAR");
        var tablet = new FakeTablet(PageOf("NAVIGATION", "WEATHER & NOTAM", tab));
        var press = tablet.Press(tab);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "WEATHER & NOTAM", tab with { Selected = true }));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { tab.Hit }, tablet.Taps);
        // No "did not show" navigation message: a tab is not a navigation. It says it is the current one.
        Assert.Equal(new[] { "Metar (current)" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_tab_of_the_real_maintenance_page_says_it_is_current_once(string culture)
    {
        // The final review's I2: HYDRAULIC pressed on Landing gear, the tablet then showing the Hydraulic tab (15b). The reader keeps
        // the tab's words and flips Selected; the window patches its label silently, so the press says it.
        var gear = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var hydraulic = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15b-aircraft-maintenance-hydraulic"))!;
        var tab = Assert.Single(gear.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Name == "maintenance_hydraulic_tab_button");
        Assert.False(tab.Selected);
        var tablet = new FakeTablet(gear);
        tablet.OnTap = _ => tablet.Show(hydraulic);

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(tablet.Press(tab), CancellationToken.None));

        Assert.Equal(new[] { tab.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Hydraulic (current)" }, tablet.Alerts);
    }

    [Fact]
    public async Task The_current_tab_pressed_again_changes_nothing_and_says_so()
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("15-aircraft-maintenance"))!;
        var tab = Assert.Single(page.Items, i => i.Kind == Bbs757EfbKind.Tab && i.Selected);
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(tab), CancellationToken.None);

        Assert.Equal(new[] { "Landing gear: no change on the tablet." }, tablet.Alerts);
    }

    // ---- M2: what a press says names the control as the window does ---------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_labelled_door_says_its_label_with_its_new_words(string culture)
    {
        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("14-aircraft-cabin-doors"))!;
        var door = Assert.Single(closed.Items, i => i.Name == "btn_l1_door");
        var tablet = new FakeTablet(closed);
        tablet.OnTap = _ => tablet.Show(closed with { Items = closed.Items.Select(i => i.Name == "btn_l1_door" ? i with { Text = "Open" } : i).ToList() });

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(tablet.Press(door), CancellationToken.None));

        Assert.Equal(new[] { "Left FWD: Open" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_settings_row_the_tablet_did_not_follow_is_named_in_sentence_case(string culture)
    {
        // The reader keeps a settings row's words as BlueBird writes them (SYNC BAROMETERS); the window and the press both say
        // "Sync barometers".
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        var sync = Assert.Single(page.Items, i => i.Text == "SYNC BAROMETERS");
        var tablet = new FakeTablet(page);
        tablet.File["baro_sync"] = Bbs757Value.Of(true);

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(tablet.Press(sync), wantOn: false, CancellationToken.None));

        Assert.Equal(new[] { "Sync barometers did not change." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_real_row_that_never_comes_into_view_is_named_in_sentence_case(string culture)
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        var tablet = new FakeTablet(page);   // the drag moves nothing

        await TestCulture.Run(culture, () =>
            tablet.Runner().SwitchAsync(tablet.Press(page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle)), wantOn: true, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Dirty panel states could not be brought into view, not pressed." }, tablet.Alerts);
    }

    // ---- echoes and hooks ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_fuel_truck_tile_arms_and_settles_its_echo()
    {
        var off = FuelTruck("off");
        var tablet = new FakeTablet(Ground(off));
        tablet.Vars["BBS757_vis_fueltruck"] = 0;
        var press = tablet.Press(off);
        tablet.OnTap = _ =>
        {
            tablet.Vars["BBS757_vis_fueltruck"] = 1;
            tablet.Show(Ground(FuelTruck("on")));
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "arm FUEL TRUCK", Tap(off), "alert Fuel truck on", "settle FUEL TRUCK 1" }, tablet.Flow);
    }

    [Fact]
    public async Task The_GPU_tile_arms_and_settles_the_ground_power_echo()
    {
        var gpu = Gpu("removed");
        var tablet = new FakeTablet(Ground(gpu));
        tablet.Vars[Bbs757SharedKeys.GpuConnected] = 0;
        var press = tablet.Press(gpu);
        tablet.OnTap = _ =>
        {
            tablet.Vars[Bbs757SharedKeys.GpuConnected] = 1;
            tablet.Show(Ground(Gpu("connected")));
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { "arm GPU", Tap(gpu), "alert GPU connected", "settle GPU 1" }, tablet.Flow);
    }

    [Fact]
    public async Task An_echo_is_settled_with_nothing_read_when_the_tap_could_not_run()
    {
        var off = FuelTruck("off");
        var tablet = new FakeTablet(Ground(off)) { Reachable = false };
        tablet.Vars["BBS757_vis_fueltruck"] = 0;

        await tablet.Runner().ClickAsync(tablet.Press(off), CancellationToken.None);

        Assert.Equal(new[] { "arm FUEL TRUCK", Tap(off), "alert The tablet cannot be reached, so nothing was tapped.", "settle FUEL TRUCK unread" }, tablet.Flow);
    }

    [Fact]
    public async Task A_fetch_press_calls_the_hook_after_the_tap()
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch));
        var press = tablet.Press(fetch);
        tablet.OnTap = _ => tablet.Show(PageOf("NAVIGATION", "ACARS DASHBOARD", Tile("Refresh SIMBRIEF ofp", "btn:|REFRESH SIMBRIEF OFP@42,40", fetch.Box)));

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { Tap(fetch), "hook Fetch", "alert Refresh SIMBRIEF ofp" }, tablet.Flow);
    }

    [Fact]
    public async Task The_nav_data_buttons_hook_the_nav_data_watch()
    {
        foreach (var (words, hook) in new[] { ("Update", Bbs757EfbHook.NavDataUpdate), ("Remove", Bbs757EfbHook.NavDataRemove) })
        {
            var button = Tile(words, "btn:|" + words.ToUpperInvariant() + "@64,16", new Rectangle(1221, 307, 140, 40));
            var tablet = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", button)) { Cycle = 2609 };
            tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRAC DATA", button with { Text = "Updating" + Ellipsis }));

            await tablet.Runner().ClickAsync(tablet.Press(button), CancellationToken.None);

            Assert.Equal(new[] { "cycle", Tap(button), "hook " + hook + " 2609", "alert Updating" + Ellipsis }, tablet.Flow);
        }
    }

    [Fact]
    public async Task A_removal_hands_the_watch_the_cycle_installed_before_the_tap()
    {
        var remove = Tile("Remove", "btn:|REMOVE@64,16", new Rectangle(1221, 307, 140, 40));
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", remove)) { Cycle = 2610 };
        tablet.OnTap = _ =>
        {
            tablet.Cycle = null;
            tablet.Show(PageOf("SETTINGS", "AIRAC DATA", remove with { Text = "Update" }));
        };

        await tablet.Runner().ClickAsync(tablet.Press(remove), CancellationToken.None);

        Assert.Equal(new[] { "cycle", Tap(remove), "hook NavDataRemove 2610", "alert Update" }, tablet.Flow);
    }

    [Fact]
    public async Task No_cycle_is_read_for_a_press_that_is_no_nav_data_press_or_whose_tap_cannot_run()
    {
        var fetch = Tile("Fetch SIMBRIEF ofp", "btn:|FETCH SIMBRIEF OFP@42,40", new Rectangle(700, 780, 280, 45));
        var acars = new FakeTablet(PageOf("NAVIGATION", "ACARS DASHBOARD", fetch)) { Cycle = 2609 };
        await acars.Runner().ClickAsync(acars.Press(fetch), CancellationToken.None);
        Assert.DoesNotContain("cycle", acars.Events);

        var update = Tile("Update", "btn:|UPDATE@64,16", new Rectangle(1221, 307, 140, 40));
        var airac = new FakeTablet(PageOf("SETTINGS", "AIRAC DATA", update)) { Cycle = 2609, Reachable = false };
        await airac.Runner().ClickAsync(airac.Press(update), CancellationToken.None);
        Assert.DoesNotContain(airac.Events, e => e.StartsWith("hook", StringComparison.Ordinal));
    }

    // ---- switches: the wish, and BlueBird's file -----------------------------------------------------------------------

    [Fact]
    public async Task A_switch_already_as_wished_is_not_tapped_and_nothing_is_said()
    {
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));
        var press = tablet.Press(sync);
        tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_settings_switch_not_followed_by_the_file_says_did_not_change(string culture)
    {
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));
        tablet.File["baro_sync"] = Bbs757Value.Of(false);
        var press = tablet.Press(sync);
        tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "Sync barometers did not change." }, tablet.Alerts);
        Assert.All(tablet.SettingsRead, path => Assert.Equal("baro_sync", path));
        Assert.True(tablet.Now >= Bbs757EfbPressRunner.FileMaxMs, "the file is read for two seconds");
    }

    [Fact]
    public async Task A_settings_switch_the_file_follows_says_nothing()
    {
        var sync = Switch("Sync barometers", "tgl:SYNC BAROMETERS", on: false);
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", sync));
        tablet.File["baro_sync"] = Bbs757Value.Of(false);
        var press = tablet.Press(sync);
        tablet.OnTap = _ =>
        {
            tablet.File["baro_sync"] = Bbs757Value.Of(true);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", sync with { On = true }));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_press_that_switches_a_callout_off_names_it(string culture)
    {
        var height = Switch("2500", "tgl:2500", on: true, y: 400);
        var radio = Switch("Radio altimeter (2500)", "tgl:RADIO ALTIMETER (2500)", on: false, y: 450);
        var tablet = new FakeTablet(PageOf("SETTINGS", "CALLOUTS", height, radio));
        tablet.File[Egpws + "2500"] = Bbs757Value.Of(true);
        tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(false);
        var press = tablet.Press(radio);
        tablet.OnTap = _ =>
        {
            tablet.File[Egpws + "2500"] = Bbs757Value.Of(false);
            tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(true);
            tablet.Show(PageOf("SETTINGS", "CALLOUTS", height with { On = false }, radio with { On = true }));
        };

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "2500 turned off" }, tablet.Alerts);
    }

    [Fact]
    public async Task A_callout_that_was_already_off_is_not_named()
    {
        var height = Switch("2500", "tgl:2500", on: false, y: 400);
        var radio = Switch("Radio altimeter (2500)", "tgl:RADIO ALTIMETER (2500)", on: false, y: 450);
        var tablet = new FakeTablet(PageOf("SETTINGS", "CALLOUTS", height, radio));
        tablet.File[Egpws + "2500"] = Bbs757Value.Of(false);
        tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(false);
        var press = tablet.Press(radio);
        tablet.OnTap = _ =>
        {
            tablet.File[Egpws + "radio_altimeter"] = Bbs757Value.Of(true);
            tablet.Show(PageOf("SETTINGS", "CALLOUTS", height, radio with { On = true }));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.Alerts);
    }

    public static TheoryData<string, long, bool> CulturesByCpdlcFile => new()
    {
        { "en-US", 1L, false }, { "en-US", 0L, true },
        { "de-DE", 1L, false }, { "de-DE", 0L, true },
        { "sv-SE", 1L, false }, { "sv-SE", 0L, true },
    };

    [Theory]
    [MemberData(nameof(CulturesByCpdlcFile))]
    public async Task A_two_choice_setting_is_judged_by_the_choice_that_was_not_set(string culture, long stored, bool notChanged)
    {
        // A choice list in the window (its word was read): judged by the file, and only a failure speaks (item 18).
        var cpdlc = Switch("CPDLC network", "tgl:CPDLC NETWORK", on: null, value: "Hoppie");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", cpdlc));
        tablet.File["cpdlc_network"] = Bbs757Value.Of(0L);
        var press = tablet.Press(cpdlc);
        tablet.OnTap = _ =>
        {
            tablet.File["cpdlc_network"] = Bbs757Value.Of(stored);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", cpdlc with { Value = "SayIntentions" }));
        };

        await TestCulture.Run(culture, () => tablet.Runner().ChoiceSwitchAsync(press, wanted: 1, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(notChanged ? new[] { "CPDLC network did not change." } : Array.Empty<string>(), tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_two_choice_setting_the_window_shows_as_a_check_box_still_says_its_new_word(string culture)
    {
        // Its word was not read, so the window shows a check box, of which the screen reader says only checked or not: the press says
        // the new word, from the catalog, as before item 18.
        var cpdlc = Switch("CPDLC network", "tgl:CPDLC NETWORK", on: false, value: "");
        var page = PageOf("SETTINGS", "AIRCRAFT", cpdlc);
        Assert.False(Bbs757EfbElements.ShowsAsChoiceList(cpdlc, page.PageName));
        var tablet = new FakeTablet(page);
        tablet.File["cpdlc_network"] = Bbs757Value.Of(0L);
        tablet.OnTap = _ => tablet.File["cpdlc_network"] = Bbs757Value.Of(1L);

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(tablet.Press(cpdlc), wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "CPDLC network: SayIntentions" }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_two_choice_row_that_is_no_setting_says_the_word_the_page_shows_now(string culture)
    {
        // A label misread so it matches no setting: judged by the page, and the new word it shows is said.
        var row = Switch("CPDLC netwrk", "tgl:CPDLC NETWRK", on: false, value: "Hoppie");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", row));
        var press = tablet.Press(row);
        tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", row with { On = true, Value = "SayIntentions" }));

        await TestCulture.Run(culture, () => tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "CPDLC netwrk: SayIntentions" }, tablet.Alerts);
    }

    // ---- sliders -----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_the_tablet_does_not_follow_could_not_be_set(string culture)
    {
        var irs = new Bbs757EfbItem(Bbs757EfbKind.Slider, "IRS alignment time", new Rectangle(273, 513, 255, 33), new Point(1190, 529), true,
            "sld:IRS ALIGNMENT TIME", Value: "Full (7 min+)", Slider: new Bbs757EfbSlider(895, 1190, 1190));
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", irs));
        tablet.File["irs_align_time"] = Bbs757Value.Of(9L);
        var press = tablet.Press(irs);

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(press, "2 min", CancellationToken.None));

        Assert.Equal(Bbs757EfbPress.MaxSliderTaps, tablet.Taps.Count);
        Assert.Equal(new[] { "IRS alignment time could not be set to 2 min." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_question_raised_by_a_choice_step_stops_the_steps()
    {
        // B757-18: a question that came up over the page since the read takes no tap on the page under it. A choice slider's first tap
        // brings up a dialog (BlueBird routes every tap to it while it is up): no next step lands in it, and nothing more is said (the
        // read that found the question said it).
        var irs = new Bbs757EfbItem(Bbs757EfbKind.Slider, "IRS alignment time", new Rectangle(273, 513, 255, 33), new Point(1190, 529), true,
            "sld:IRS ALIGNMENT TIME", Value: "Full (7 min+)", Slider: new Bbs757EfbSlider(895, 1190, 1190));
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", irs));
        tablet.File["irs_align_time"] = Bbs757Value.Of(9L);
        var press = tablet.Press(irs);
        tablet.OnTap = _ => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", irs) with
        {
            Question = new Bbs757EfbQuestion("Something went wrong.", new[] { Tile("OK", "q:OK", new Rectangle(608, 589, 180, 60)) }),
        });

        await tablet.Runner().SliderAsync(press, "2 min", CancellationToken.None);

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_number_slider_is_tapped_at_its_place_and_judged_by_the_knob(string culture)
    {
        var ceiling = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Ceiling", new Rectangle(300, 300, 100, 30), new Point(500, 315), true,
            "sld:CEILING", Slider: new Bbs757EfbSlider(500, 900, 500));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        var press = tablet.Press(ceiling);
        tablet.OnTap = p => tablet.Show(PageOf("AIRCRAFT", "CABIN & DOORS",
            ceiling with { Slider = new Bbs757EfbSlider(500, 900, p.X), Value = ((p.X - 500) / 4).ToString(CultureInfo.InvariantCulture) + "%" }));

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(press, "30", CancellationToken.None));
        Assert.Equal(new[] { new Point(620, 315) }, tablet.Taps);
        Assert.Empty(tablet.Alerts);

        var stuck = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        await TestCulture.Run(culture, () => stuck.Runner().SliderAsync(stuck.Press(ceiling), "70", CancellationToken.None));
        Assert.Equal(new[] { "Ceiling could not be set to 70 percent." }, stuck.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_choice_slider_whose_choice_was_not_read_is_set_as_a_percent(string culture)
    {
        var irs = new Bbs757EfbItem(Bbs757EfbKind.Slider, "IRS alignment time", new Rectangle(273, 513, 255, 33), new Point(1000, 529), true,
            "sld:IRS ALIGNMENT TIME", Value: "", Slider: new Bbs757EfbSlider(895, 1190, 1000));
        var page = PageOf("SETTINGS", "AIRCRAFT", irs);
        Assert.NotNull(Bbs757EfbElements.OptionsFor(irs, page.PageName));
        var tablet = new FakeTablet(page);
        var press = tablet.Press(irs);
        tablet.OnTap = p => tablet.Show(PageOf("SETTINGS", "AIRCRAFT", irs with { Slider = new Bbs757EfbSlider(895, 1190, p.X), Value = "50" }));

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(press, "50", CancellationToken.None));
        Assert.Equal(new[] { new Point(1043, 529) }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_value_that_is_neither_a_choice_nor_a_number_is_said_not_set(string culture)
    {
        var ceiling = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Ceiling", new Rectangle(300, 300, 100, 30), new Point(500, 315), true,
            "sld:CEILING", Slider: new Bbs757EfbSlider(500, 900, 500));
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(tablet.Press(ceiling), "bright", CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Ceiling could not be set to bright." }, tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_percent_is_judged_by_its_shown_value(string culture)
    {
        // The knob moves, but the value shown is what judges: 30 asked, 90 shown is not set; a value that is no percent but changed is.
        var ceiling = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Ceiling", new Rectangle(300, 300, 100, 30), new Point(500, 315), true,
            "sld:CEILING", Value: "10%", Slider: new Bbs757EfbSlider(500, 900, 500));
        var wrong = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling));
        wrong.OnTap = p => wrong.Show(PageOf("AIRCRAFT", "CABIN & DOORS", ceiling with { Value = "90%", Slider = new Bbs757EfbSlider(500, 900, p.X) }));
        await TestCulture.Run(culture, () => wrong.Runner().SliderAsync(wrong.Press(ceiling), "30", CancellationToken.None));
        Assert.Equal(new[] { "Ceiling could not be set to 30 percent." }, wrong.Alerts);

        var feet = ceiling with { Value = "100 ft" };
        var changed = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", feet));
        changed.OnTap = _ => changed.Show(PageOf("AIRCRAFT", "CABIN & DOORS", feet with { Value = "300 ft" }));
        await TestCulture.Run(culture, () => changed.Runner().SliderAsync(changed.Press(feet), "30", CancellationToken.None));
        Assert.Empty(changed.Alerts);

        var same = new FakeTablet(PageOf("AIRCRAFT", "CABIN & DOORS", feet));
        await TestCulture.Run(culture, () => same.Runner().SliderAsync(same.Press(feet), "30", CancellationToken.None));
        Assert.Equal(new[] { "Ceiling could not be set to 30 percent." }, same.Alerts);
    }

    // ---- I3, I4: sliders whose value shows elsewhere, or nowhere, and vertical ones -----------------------------------------

    /// <summary>
    /// Weight &amp; balance from its committed tree (captured live with its knobs, 2026-11-07: forward cargo at 59.8 %, the passengers'
    /// knobs at their ends), or, with <paramref name="knobs"/> false, as a layout without the slider knob reads it, which pins the
    /// fallback on the real page: a knob placed from the count a title shows, a slider judged by its count or by the page. The business
    /// title and the forward cargo's weight box show what is given, and the forward cargo's knob sits at <paramref name="cargoKnob"/>
    /// when given (0 to 1).
    /// </summary>
    private static Bbs757EfbScreenModel WeightBalance(bool knobs, string? businessTitle = null, string? forwardCargo = null, float? cargoKnob = null)
    {
        var apps = Bbs757EfbTreeJson.Load("12-aircraft-weight-balance");
        if (!knobs) apps = Bbs757EfbTreeJson.WithoutKnobs(apps);
        if (cargoKnob is float place) apps = Bbs757EfbTreeJson.WithKnob(apps, "wb_baggage_fwd_slider", place, 12);
        if (businessTitle != null) apps = Bbs757EfbTreeJson.WithText(apps, "wb_pax_business_title", businessTitle);
        if (forwardCargo != null) apps = Bbs757EfbTreeJson.WithText(apps, "wb_baggage_fwd_value", forwardCargo);
        return Bbs757EfbModelReader.Read(apps)!;
    }

    [Fact]
    public void A_passenger_slider_is_named_by_its_title_and_placed_by_its_count()
    {
        // The real page: named by its title, its count the value, and BlueBird's knob at the end, where 16 of 16 put it.
        var page = WeightBalance(knobs: true);
        var pax = Assert.Single(page.Items, i => i.Name == "wb_pax_business_slider");
        Assert.Equal("BUSINESS PAX", pax.Text);
        Assert.Equal("16/16", pax.Value);
        Assert.True(pax.Slider!.KnobRead);
        Assert.Equal(pax.Slider.TrackRight, pax.Slider.KnobX);
        Assert.DoesNotContain(page.Items, i => i.Kind == Bbs757EfbKind.Heading && i.Text.StartsWith("BUSINESS PAX", StringComparison.Ordinal));

        // With no knob read, the title's count places the knob: 16 of 16 at the end, 8 of 16 halfway.
        var bare = Assert.Single(WeightBalance(knobs: false).Items, i => i.Name == "wb_pax_business_slider");
        Assert.False(bare.Slider!.KnobRead);
        Assert.Equal(bare.Slider.TrackRight, bare.Slider.KnobX);
        Assert.Equal(bare.Slider.At(50),
            WeightBalance(knobs: false, "BUSINESS PAX - 8/16").Items.Single(i => i.Name == "wb_pax_business_slider").Slider!.KnobX);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_passenger_slider_is_judged_by_the_count_its_title_shows_after_the_tap(string culture)
    {
        // The fallback with no knob read (so the title's count edited here is the slider's whole state): the count judges.
        var page = WeightBalance(knobs: false);
        var pax = Assert.Single(page.Items, i => i.Name == "wb_pax_business_slider");
        var set = new FakeTablet(page);
        set.OnTap = _ => set.Show(WeightBalance(knobs: false, "BUSINESS PAX - 8/16"));

        await TestCulture.Run(culture, () => set.Runner().SliderAsync(set.Press(pax), "50", CancellationToken.None));

        Assert.Equal(new[] { new Point(pax.Slider!.At(50), pax.Hit.Y) }, set.Taps);
        Assert.Empty(set.Alerts);

        var wrong = new FakeTablet(page);
        wrong.OnTap = _ => wrong.Show(WeightBalance(knobs: false, "BUSINESS PAX - 15/16"));
        await TestCulture.Run(culture, () => wrong.Runner().SliderAsync(wrong.Press(pax), "50", CancellationToken.None));
        Assert.Equal(new[] { "Business pax could not be set to 50 percent." }, wrong.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_whose_value_shows_nowhere_is_judged_by_its_page(string culture)
    {
        // The fallback with no knob read: the cargo slider shows no value of its own, and its weight box beside it is what the tap
        // changes.
        var page = WeightBalance(knobs: false);
        var cargo = Assert.Single(page.Items, i => i.Name == "wb_baggage_fwd_slider");
        Assert.Equal("", cargo.Value);
        var followed = new FakeTablet(page);
        followed.OnTap = _ => followed.Show(WeightBalance(knobs: false, forwardCargo: "3080"));

        await TestCulture.Run(culture, () => followed.Runner().SliderAsync(followed.Press(cargo), "50", CancellationToken.None));

        Assert.Single(followed.Taps);
        Assert.Empty(followed.Alerts);

        var stuck = new FakeTablet(page);
        await TestCulture.Run(culture, () => stuck.Runner().SliderAsync(stuck.Press(cargo), "50", CancellationToken.None));
        Assert.Equal(new[] { "Forward cargo could not be set to 50 percent." }, stuck.Alerts);
    }

    [Theory]
    [InlineData("15b-aircraft-maintenance-hydraulic", "maintenance_hyd_res_left_slider", true)]
    [InlineData("30-systems", "elec_zoom_slider", true)]
    [InlineData("15b-aircraft-maintenance-hydraulic", "maintenance_hyd_res_left_slider", false)]
    [InlineData("30-systems", "elec_zoom_slider", false)]
    public async Task A_vertical_slider_is_set_along_its_height(string fixture, string name, bool knobs)
    {
        // A slider taller than wide runs bottom (0, measured 2026-11-07) to top, and is tapped at its x middle. With BlueBird's knob read
        // (both 12 px tall, the committed pages) along its knob center's range, the box less half the knob at each end; with none (the
        // fallback) along its box.
        var apps = Bbs757EfbTreeJson.Load(fixture);
        var page = Bbs757EfbModelReader.Read(knobs ? apps : Bbs757EfbTreeJson.WithoutKnobs(apps))!;
        var slider = Assert.Single(page.Items, i => i.Name == name);
        Assert.True(slider.Slider!.Vertical);
        Assert.Equal(knobs, slider.Slider.KnobRead);
        var taps = new List<Point>();
        foreach (var percent in new[] { "10", "90" })
        {
            var tablet = new FakeTablet(page);
            await tablet.Runner().SliderAsync(tablet.Press(slider), percent, CancellationToken.None);
            taps.Add(Assert.Single(tablet.Taps));
        }

        Assert.All(taps, t => Assert.Equal(slider.Box.Left + slider.Box.Width / 2, t.X));
        Assert.All(taps, t => Assert.InRange(t.Y, slider.Box.Top, slider.Box.Bottom - 1));
        Assert.True(taps[0].Y > taps[1].Y, $"10 percent at {taps[0]} should be below 90 percent at {taps[1]}");
        double half = knobs ? 6 : 0, run = slider.Box.Height - 2 * half;
        Assert.InRange(taps[0].Y, slider.Box.Bottom - half - run / 10 - 2, slider.Box.Bottom - half - run / 10 + 2);
        Assert.InRange(taps[1].Y, slider.Box.Top + half + run / 10 - 2, slider.Box.Top + half + run / 10 + 2);
    }

    // ---- sliders whose knob BlueBird's memory gives (plan 20, Task 13) --------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_slider_with_bluebirds_knob_is_judged_by_where_its_knob_went(string culture)
    {
        // The real page: forward cargo's knob at 59.8 % (6,160 lbs). The knob-center track of the 188 px slider at 1162.5 with its 12 px
        // knob runs 1168 to 1344, so 20 is tapped at 1203; the knob then sits there, and the weight box reads about 20 % of the hold.
        var page = WeightBalance(knobs: true);
        var cargo = Assert.Single(page.Items, i => i.Name == "wb_baggage_fwd_slider");
        Assert.True(cargo.Slider!.KnobRead);
        Assert.Equal(60, Math.Round(cargo.Slider.PercentOf(cargo.Slider.KnobX)));
        var set = new FakeTablet(page);
        set.OnTap = p => set.Show(WeightBalance(knobs: true, forwardCargo: "2060", cargoKnob: (p.X - 1168) / 176f));

        await TestCulture.Run(culture, () => set.Runner().SliderAsync(set.Press(cargo), "20", CancellationToken.None));

        Assert.Equal(new[] { new Point(1203, cargo.Hit.Y) }, set.Taps);
        Assert.Empty(set.Alerts);

        // Deliberately not a state the tablet makes: the weight box changed but the knob stayed at 59.8 %. The knob judges before the
        // page, so it was not set.
        var stuck = new FakeTablet(page);
        stuck.OnTap = _ => stuck.Show(WeightBalance(knobs: true, forwardCargo: "2060"));
        await TestCulture.Run(culture, () => stuck.Runner().SliderAsync(stuck.Press(cargo), "20", CancellationToken.None));
        Assert.Single(stuck.Taps);
        Assert.Equal(new[] { "Forward cargo could not be set to 20 percent." }, stuck.Alerts);
    }

    [Fact]
    public async Task An_upright_slider_with_bluebirds_knob_is_tapped_along_its_knobs_center()
    {
        // Measured 2026-11-07 on Systems' zoom: a tap at y 260 put the knob's center at 259.5, 25.7 % from the bottom. Its knob-center
        // track runs from 296 (0) up to 154 (100), so 25 is tapped at y 260, at the track's x middle, inside the box. The committed page
        // has the zoom at 10.4 %.
        var apps = Bbs757EfbTreeJson.Load("30-systems");
        var page = Bbs757EfbModelReader.Read(apps)!;
        var zoom = Assert.Single(page.Items, i => i.Name == "elec_zoom_slider");
        var tablet = new FakeTablet(page);
        tablet.OnTap = _ => tablet.Show(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithKnob(apps, "elec_zoom_slider", 0.257f, 12))!);

        await tablet.Runner().SliderAsync(tablet.Press(zoom), "25", CancellationToken.None);

        var tap = Assert.Single(tablet.Taps);
        Assert.Equal(new Point(37, 260), tap);
        Assert.InRange(tap.Y, zoom.Box.Top, zoom.Box.Bottom - 1);
        Assert.Empty(tablet.Alerts);
        // The review's I2: the knob moving alone is a change of the page, seen by the first read after the tap (the press's own read
        // and that one), never after the whole ChangeMaxMs.
        Assert.Equal(2, tablet.Events.Count(e => e == "read"));
        Assert.True(tablet.Now < Bbs757EfbPressRunner.ChangeMaxMs, $"waited {tablet.Now} ms for a change");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_sliders_knob_judges_it_before_the_percent_it_shows(string culture)
    {
        // The review's M1: the real page's left tank shows "34%" with its knob at 34.5 %. After the tap the label reads "50%", but the knob
        // stayed (deliberately not a state the tablet makes): the knob judges first, so it was not set.
        IReadOnlyList<Bbs757EfbMemory.App> Fuel(string shown) =>
            Bbs757EfbTreeJson.WithText(Bbs757EfbTreeJson.Load("11b-aircraft-fuel-simbrief-import"), "fuel_left_tank_label", shown);
        var page = Bbs757EfbModelReader.Read(Fuel("34%"))!;
        var left = Assert.Single(page.Items, i => i.Name == "fuel_slider_left");
        Assert.Equal("34%", left.Value);
        Assert.True(left.Slider!.KnobRead);
        var tablet = new FakeTablet(page);
        tablet.OnTap = _ => tablet.Show(Bbs757EfbModelReader.Read(Fuel("50%"))!);

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(tablet.Press(left), "50", CancellationToken.None));

        Assert.Single(tablet.Taps);
        Assert.Equal(new[] { "Left could not be set to 50 percent." }, tablet.Alerts);
    }

    /// <summary>Systems from its committed tree with Electrical's zoom slider's knob read at <paramref name="place"/> (the committed
    /// page has it at 0.104).</summary>
    private static Bbs757EfbScreenModel SystemsZoom(float place) =>
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithKnob(Bbs757EfbTreeJson.Load("30-systems"), "elec_zoom_slider", place, 12))!;

    [Fact]
    public async Task A_button_that_moves_only_a_knob_changed_the_tablet()
    {
        // The review's I2: "Elec zoom in" moves the zoom's knob up (measured) and nothing else on the page; that is a change, never
        // "Elec zoom in: no change on the tablet."
        var page = SystemsZoom(0.104f);
        var zoomIn = page.Items.First(i => i.Clickable && i.Name.Contains("elec_zoom_in", StringComparison.Ordinal));
        var tablet = new FakeTablet(page);
        tablet.OnTap = _ => tablet.Show(SystemsZoom(0.2f));

        await tablet.Runner().ClickAsync(tablet.Press(zoomIn), CancellationToken.None);

        Assert.Single(tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public void A_read_in_which_only_a_knob_moved_is_pushed()
    {
        // The review's I2: the window's slider value comes from the knob, so a read in which BlueBird moved only the knob is pushed.
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(SystemsZoom(0.104f));
        int pushes = h.Pushes.Count;
        string before = h.Pushes[^1][$"items.{IndexOf(h.Pushes[^1], "Elec zoom")}.value"];

        h.Bridge.TakeRead(SystemsZoom(0.5f));

        Assert.Equal(pushes + 1, h.Pushes.Count);
        // 10.4 % on the 142 px knob-center track is pixel 15 of it: 11 in whole percent.
        Assert.Equal(("11", "50"), (before, h.Pushes[^1][$"items.{IndexOf(h.Pushes[^1], "Elec zoom")}.value"]));
    }

    // ---- two-choice switches --------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Nosewheel(string value) =>
        new(Bbs757EfbKind.Toggle, "Nosewheel steering", new Rectangle(273, 333, 237, 33), new Point(1170, 349), true, "tgl:NOSEWHEEL STEERING", Value: value);

    private static async Task<List<string>> SetNosewheel(string shown, string wanted)
    {
        var page = PageOf("SETTINGS", "AIRCRAFT", Nosewheel(shown));
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(page, null) };
        h.Bridge.TakeRead(page);
        var push = h.Pushes[^1];
        int row = IndexOf(push, "Nosewheel steering");
        Assert.Equal("select", push[$"items.{row}.type"]);
        await h.Bridge.HandleAsync("set_element_value", new Dictionary<string, string> { ["index"] = push[$"items.{row}.aidx"], ["value"] = wanted }, CancellationToken.None);
        return Texts(h.Pushes[^1]);
    }

    [Fact]
    public async Task A_two_choice_switch_set_to_its_other_word_taps_once()
    {
        // The harness has no tablet to tap, so the tap ends "cannot be reached": that alert is the proof a tap was tried.
        var texts = await SetNosewheel("Realistic", "Rudder");
        Assert.Contains(Bbs757EfbPress.UnreachableText, texts);
    }

    [Fact]
    public async Task A_two_choice_switch_set_to_its_shown_word_taps_nothing()
    {
        var texts = await SetNosewheel("Realistic", "Realistic");
        Assert.DoesNotContain(Bbs757EfbPress.UnreachableText, texts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_two_choice_choice_list_change_says_nothing_and_a_failed_one_says_so(string culture)
    {
        // Item 18 (the owner's ruling, 2026-11-07): the screen reader said "Rudder" as the pilot chose it in the choice list, so a
        // change that landed says nothing; one that did not says "did not change", in sentence case.
        var row = Nosewheel("Realistic");
        var tablet = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", row));
        tablet.File["tiller_realistic"] = Bbs757Value.Of(true);
        tablet.OnTap = _ =>
        {
            tablet.File["tiller_realistic"] = Bbs757Value.Of(false);
            tablet.Show(PageOf("SETTINGS", "AIRCRAFT", row with { Value = "Rudder" }));
        };

        await TestCulture.Run(culture, () => tablet.Runner().ChoiceSwitchAsync(tablet.Press(row), wanted: 1, CancellationToken.None));

        Assert.Equal(new[] { row.Hit }, tablet.Taps);
        Assert.Empty(tablet.Alerts);

        var stuck = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", row with { Text = "NOSEWHEEL STEERING" }));
        stuck.File["tiller_realistic"] = Bbs757Value.Of(true);
        await TestCulture.Run(culture, () => stuck.Runner().ChoiceSwitchAsync(stuck.Press(stuck.Page.Items[0]), wanted: 1, CancellationToken.None));
        Assert.Single(stuck.Taps);
        Assert.Equal(new[] { "Nosewheel steering did not change." }, stuck.Alerts);
    }

    [Fact]
    public async Task A_two_choice_switch_whose_fresh_read_shows_the_wish_is_not_tapped()
    {
        // The final review's I5: pressed on a push showing Realistic, the tablet shows Rudder by the press's own read. Rudder asked
        // for: nothing tapped (a tap would switch it away from the wish). Realistic asked for: one tap.
        var pushed = Nosewheel("Realistic");
        var already = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", pushed));
        var press = already.Press(pushed);
        already.Show(PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Rudder")));

        await already.Runner().ChoiceSwitchAsync(press, wanted: 1, CancellationToken.None);

        Assert.Empty(already.Taps);
        Assert.Empty(already.Alerts);

        var back = new FakeTablet(PageOf("SETTINGS", "AIRCRAFT", pushed));
        var again = back.Press(pushed);
        back.Show(PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Rudder")));
        await back.Runner().ChoiceSwitchAsync(again, wanted: 0, CancellationToken.None);
        Assert.Single(back.Taps);
    }

    [Fact]
    public async Task A_two_choice_switch_set_from_an_older_push_to_the_word_the_tablet_shows_now_taps_nothing()
    {
        // Through the bridge: the push showed Realistic, the tablet's read now shows Rudder, the pilot picks Rudder.
        var pushed = PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Realistic"));
        var now = PageOf("SETTINGS", "AIRCRAFT", Nosewheel("Rudder"));
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(now, null) };
        h.Bridge.TakeRead(pushed);
        var push = h.Pushes[^1];
        int row = IndexOf(push, "Nosewheel steering");

        await h.Bridge.HandleAsync("set_element_value", new Dictionary<string, string> { ["index"] = push[$"items.{row}.aidx"], ["value"] = "Rudder" },
            CancellationToken.None);

        Assert.DoesNotContain(Bbs757EfbPress.UnreachableText, Texts(h.Pushes[^1]));   // no tap was tried
    }

    // ---- drop-downs and text boxes (plan 21) ----------------------------------------------------------------------------

    [Fact]
    public async Task A_set_on_a_drop_down_is_chosen_on_the_tablet_and_a_text_box_is_no_control()
    {
        // The window shows a drop-down as a choice list, and a choice in it is made on the tablet (plan 21, Task 7): the harness has no
        // tablet to tap, so the face's tap ends "cannot be reached", the proof a tap was tried. That push shows the tablet's entry again
        // (Task 5's review, M3): the window's list returns to RW07L, not the RW26 the pilot picked. A text box whose caret was not read (the
        // capture predates plan 22) is a line: nothing to set.
        var page = PerfCalc();
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(page, null) };
        h.Bridge.TakeRead(page);
        var push = h.Pushes[^1];
        int pushes = h.Pushes.Count;
        int airport = IndexOf(push, "Airport: KSEA");
        Assert.Equal(("", "false"), (push[$"items.{airport}.type"], push[$"items.{airport}.clickable"]));

        await h.Bridge.HandleAsync("set_element_value",
            new Dictionary<string, string> { ["index"] = push[$"items.{IndexOf(push, "Runway")}.aidx"], ["value"] = "RW26" }, CancellationToken.None);

        Assert.True(h.Pushes.Count > pushes);
        var after = h.Pushes[^1];
        Assert.Equal(("alert", Bbs757EfbPress.UnreachableText), (after["items.0.kind"], after["items.0.text"]));
        Assert.Equal(("select", "RW07L"), (after[$"items.{IndexOf(after, "Runway")}.type"], after[$"items.{IndexOf(after, "Runway")}.value"]));
    }

    // ---- edit boxes (plan 22, Task 5) ------------------------------------------------------------------------------------

    /// <summary>The window's set on the element named <paramref name="name"/> of the last push, as the shared window sends it on Enter,
    /// taken through the command queue (B757-28's settle passes the one set through) and handled; the alert it left, or null.</summary>
    private static async Task<string?> SetOnEnterAsync(BridgeHarness h, string name, string text)
    {
        var push = h.Pushes[^1];
        h.Bridge.EnqueueCommand("set_element_value", new Dictionary<string, string>
        {
            ["index"] = push[$"items.{IndexOf(push, name)}.aidx"], ["value"] = text, ["controlType"] = "text",
        });
        Assert.True(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));
        Assert.False(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));   // the one set, run once
        var last = h.Pushes[^1];
        return last.TryGetValue("items.0.kind", out var kind) && kind == "alert" ? last["items.0.text"] : null;
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_set_on_an_edit_box_types_it(string culture)
    {
        // PERF calc's Airport box with its caret read: an edit box whose set (the pilot's Enter) is typed on the tablet (TypeAsync). The
        // harness has no tablet to tap: a character on no key is said before any tap, the proof the text reached the typing; a text it can
        // type taps the field first, which ends "cannot be reached"; the text the box holds already taps nothing and says nothing.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithTextState(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"),
            "planning_perf_calc_airport_input", 4, 4, false))!;
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(page, null) };
        h.Bridge.TakeRead(page);
        int airport = IndexOf(h.Pushes[^1], "Airport");
        Assert.Equal(("text", "KSEA", "enter"),
            (h.Pushes[^1][$"items.{airport}.type"], h.Pushes[^1][$"items.{airport}.value"], h.Pushes[^1][$"items.{airport}.commit"]));

        await TestCulture.Run(culture, async () =>
        {
            Assert.Equal("Airport: the tablet's keyboard has no é.", await SetOnEnterAsync(h, "Airport", "KSé"));
            Assert.Equal(Bbs757EfbPress.UnreachableText, await SetOnEnterAsync(h, "Airport", "KPDX"));
            Assert.Null(await SetOnEnterAsync(h, "Airport", "KSEA"));
        });
    }

    [Fact]
    public async Task A_set_on_a_dialogs_edit_box_types_it_under_its_question()
    {
        // The fuel input dialog's box (11c, its caret read): the edit box named with its unit, its set typed on the keypad. A letter is on
        // no key of the keypad, said before any tap under the box's name as the window shows it; digits tap the box (no tablet here).
        var dialog = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithTextState(Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog"),
            "fuel_input_textbox", 3, 8, true))!;
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(dialog, null) };
        h.Bridge.TakeRead(dialog);

        Assert.Equal("Fuel (Gal): the tablet's keyboard has no a.", await SetOnEnterAsync(h, "Fuel (Gal)", "20a0"));
        Assert.Equal(Bbs757EfbPress.UnreachableText, await SetOnEnterAsync(h, "Fuel (Gal)", "2000"));
    }

    // ---- choosing from a drop-down (plan 21, Task 7) ---------------------------------------------------------------------

    private const string RunwayDropDown = "planning_perf_calc_runway_dropdown";
    private static readonly string[] Runways = { "RW07L", "RW07R", "RW08", "RW25L", "RW25R", "RW26" };

    /// <summary>Where RW26's row is drawn in the runway list captured open (25c): [519,379 150x38], its middle.</summary>
    private static readonly Point Rw26Row = new(594, 398);

    /// <summary>PERF calc as captured with the runway list closed (25b, RW07L) or open (25c); closed, showing <paramref name="face"/>
    /// when one is given (the tablet after a choice).</summary>
    private static Bbs757EfbScreenModel PerfCalc(bool open = false, string? face = null)
    {
        var apps = Bbs757EfbTreeJson.Load(open ? "25c-navigation-perf-calc-runway-open" : "25b-navigation-perf-calc-calculated");
        if (face != null) apps = Bbs757EfbTreeJson.AsDropDown(apps, RunwayDropDown, face, Runways);
        return Bbs757EfbModelReader.Read(apps)!;
    }

    private static Bbs757EfbItem Named(Bbs757EfbScreenModel m, string name) => m.Items.Single(i => i.Name == name);

    [Fact]
    public async Task A_drop_down_opens_and_its_entry_is_tapped()
    {
        // The captures of 2026-11-07: the runway drop-down closed (25b), its face tapped, the list open (25c, six rows 38 px apart from
        // 189), RW26's row tapped where the open list draws it, then the face shows RW26.
        var closed = PerfCalc();
        var runway = Named(closed, RunwayDropDown);
        var tablet = new FakeTablet(closed);
        tablet.OnTap = _ => tablet.Show(tablet.Taps.Count == 1 ? PerfCalc(open: true) : PerfCalc(face: "RW26"));

        bool took = await tablet.Runner().ChooseAsync(tablet.Press(runway), "RW26", CancellationToken.None);

        Assert.True(took);
        Assert.Equal(new[] { runway.Hit, Rw26Row }, tablet.Taps);
        Assert.Equal(new Point(594, 173), runway.Hit);   // the face, 519,156 150x34
        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task An_open_drop_down_is_not_opened_again()
    {
        // The pilot's own mouse left the list open after the window's push: the fresh read shows it open, so the entry's row is tapped
        // with no tap on the face, which would close it.
        var pushed = PerfCalc();
        var tablet = new FakeTablet(pushed);
        var press = tablet.Press(Named(pushed, RunwayDropDown));
        tablet.Show(PerfCalc(open: true));
        tablet.OnTap = _ => tablet.Show(PerfCalc(face: "RW26"));

        Assert.True(await tablet.Runner().ChooseAsync(press, "RW26", CancellationToken.None));

        Assert.Equal(new[] { Rw26Row }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    private const string SurfaceDropDown = "planning_perf_calc_surface_dropdown";
    private static readonly string[] Surfaces = { "Dry", "Frost", "Ice", "Slush", "Snow", "Wet" };

    /// <summary>PERF calc with the Surface Condition list open as measured live (2026-11-07, the coordinator's "Task 7 live"): its list
    /// box [977.9,357.2 150x180] showing four and a half of its six rows, [977.9, 357.2 + 38 i, 130x38]; Snow's drawn 509.2-547.2, of
    /// which 28 px show (to the box's bottom, 537.2), and Wet's 547.2-585.2, none.</summary>
    private static Bbs757EfbScreenModel SurfaceOpen() => Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithOpenList(
        Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"), SurfaceDropDown, 357.2f, 180, rowWidth: 130))!;

    private static Bbs757EfbScreenModel SurfaceShowing(string face) =>
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.AsDropDown(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"), SurfaceDropDown, face, Surfaces))!;

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_drop_down_entry_out_of_view_is_not_chosen_and_a_strip_of_it_is_tapped(string culture)
    {
        // Measured live on the Surface list (2026-11-07): an open list scrolls by no input (an eased drag in it, a drag on its scrollbar
        // strip and a wheel turn all left its content offset at 0), and a tap on a row below its box closed it without choosing. So nothing
        // is ever dragged in a list. Snow's 28 px strip took a tap (the face then read Snow): tapped in the middle of the strip that shows.
        var snow = new FakeTablet(SurfaceOpen());
        snow.OnTap = _ => snow.Show(SurfaceShowing("Snow"));

        Assert.True(await TestCulture.Run(culture, () => snow.Runner().ChooseAsync(snow.Press(Named(snow.Page, SurfaceDropDown)), "Snow", CancellationToken.None)));

        Assert.Equal(new[] { new Point(1042, 523) }, snow.Taps);   // 509-537 shows: its middle; x the 130 px row's middle
        Assert.Empty(snow.Drags);
        Assert.Empty(snow.Alerts);

        // Wet shows none of itself: not chosen at once, nothing tapped, nothing dragged, and the list, open before the press (the pilot's
        // own mouse), left open. The pilot is told why, in the tablet's terms (the final review's M3).
        var wet = new FakeTablet(SurfaceOpen());
        Assert.False(await TestCulture.Run(culture, () => wet.Runner().ChooseAsync(wet.Press(Named(wet.Page, SurfaceDropDown)), "Wet", CancellationToken.None)));
        Assert.Empty(wet.Taps);
        Assert.Empty(wet.Drags);
        Assert.Equal(new[] { "Surface Condition could not be set to Wet: the tablet's list does not show it." }, wet.Alerts);
    }

    /// <summary>Where RW07L's row is drawn in the runway list open (25c): [519,189 150x38], its middle. Tapping the entry shown closes the
    /// list with nothing changed (measured 2026-11-07).</summary>
    private static readonly Point Rw07LRow = new(594, 208);

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_list_the_runner_opened_is_closed_by_its_current_row_when_the_choice_fails(string culture)
    {
        // The final review's I2: Surface Condition closed on Dry, Wet picked. The face is tapped and the list opens over the page as measured
        // (its box 357-537); Wet's row shows none of itself, so it is not chosen, said once, and the list the runner opened is closed again by
        // Dry's row (977,357 130x38, its middle 1042,376), which closes it with nothing changed. No page control is left under it.
        var dry = SurfaceShowing("Dry");
        var surface = Named(dry, SurfaceDropDown);
        var tablet = new FakeTablet(dry);
        tablet.OnTap = _ => tablet.Show(tablet.Taps.Count == 1 ? SurfaceOpen() : dry);

        Assert.False(await TestCulture.Run(culture, () => tablet.Runner().ChooseAsync(tablet.Press(surface), "Wet", CancellationToken.None)));

        Assert.Equal(new[] { surface.Hit, new Point(1042, 376) }, tablet.Taps);
        Assert.Equal(new[] { "Surface Condition could not be set to Wet: the tablet's list does not show it." }, tablet.Alerts);
        Assert.True(tablet.Flow.IndexOf("alert " + tablet.Alerts[0]) < tablet.Flow.IndexOf("tap 1042,376"));   // said before the closing tap

        // The runway list (25b closed on RW07L) opened shorter than its rows (152 px from 189: RW26's row, 379-417, past its box): RW26 is
        // not chosen and RW07L's row closes the list.
        var closed = PerfCalc();
        var clipped = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithOpenList(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"), RunwayDropDown, 189, 152))!;
        var runway = new FakeTablet(closed);
        runway.OnTap = _ => runway.Show(runway.Taps.Count == 1 ? clipped : closed);
        Assert.False(await TestCulture.Run(culture, () => runway.Runner().ChooseAsync(runway.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None)));
        Assert.Equal(new[] { Named(closed, RunwayDropDown).Hit, Rw07LRow }, runway.Taps);
        Assert.Equal(new[] { "Runway could not be set to RW26: the tablet's list does not show it." }, runway.Alerts);

        // The row tapped and the list the runner opened left open (25c, face RW07L): said once, then closed by RW07L's row.
        var stuck = new FakeTablet(closed);
        stuck.OnTap = _ => stuck.Show(stuck.Taps.Count < 3 ? PerfCalc(open: true) : closed);
        Assert.False(await TestCulture.Run(culture, () => stuck.Runner().ChooseAsync(stuck.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None)));
        Assert.Equal(new[] { Named(closed, RunwayDropDown).Hit, Rw26Row, Rw07LRow }, stuck.Taps);
        Assert.Equal(new[] { "Runway could not be set to RW26." }, stuck.Alerts);

        // A prompt drop-down (Systems' SELECT) has no current entry: a list it opened and could not choose from stays as it is.
        var apps = Bbs757EfbTreeJson.Load("30-systems");
        var systems = Bbs757EfbModelReader.Read(apps)!;
        var opened = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithOpenList(apps, "pb_menu1", 82, 152))!;
        var menu = new FakeTablet(systems);
        menu.OnTap = _ => menu.Show(opened);
        Assert.False(await TestCulture.Run(culture, () => menu.Runner().ChooseAsync(menu.Press(Named(systems, "pb_menu1")), "28 - FUEL", CancellationToken.None)));
        Assert.Equal(new[] { new Point(1247, 57), new Point(1247, 139) }, menu.Taps);
        Assert.Equal(new[] { "Select could not be set to 28 - FUEL." }, menu.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task Picking_the_entry_shown_while_its_list_is_open_closes_the_list(string culture)
    {
        // Task 7's concern 2, answered by the measurement (the final review's I2): the runway list open (25c, the pilot's own mouse, or a
        // choice that failed), RW07L, the entry shown, picked from the window: its row is tapped, which closes the list with nothing changed.
        var tablet = new FakeTablet(PerfCalc(open: true));
        tablet.OnTap = _ => tablet.Show(PerfCalc());

        Assert.True(await TestCulture.Run(culture, () => tablet.Runner().ChooseAsync(tablet.Press(Named(tablet.Page, RunwayDropDown)), "RW07L", CancellationToken.None)));

        Assert.Equal(new[] { Rw07LRow }, tablet.Taps);
        Assert.Empty(tablet.Alerts);

        // The list stayed open: said once.
        var stays = new FakeTablet(PerfCalc(open: true));
        Assert.False(await TestCulture.Run(culture, () => stays.Runner().ChooseAsync(stays.Press(Named(stays.Page, RunwayDropDown)), "RW07L", CancellationToken.None)));
        Assert.Equal(new[] { Rw07LRow }, stays.Taps);
        Assert.Equal(new[] { "Runway could not be set to RW07L." }, stays.Alerts);

        // Closed, the entry shown taps nothing (as before).
        var closed = new FakeTablet(PerfCalc());
        Assert.True(await closed.Runner().ChooseAsync(closed.Press(Named(closed.Page, RunwayDropDown)), "RW07L", CancellationToken.None));
        Assert.Empty(closed.Taps);
    }

    [Fact]
    public async Task A_row_is_tapped_only_once_the_open_list_has_laid_it_out()
    {
        // Closed, BlueBird leaves a list's rows at [0,0 50x38] (measured): a read that shows the list open before its rows are laid out is
        // waited past, so the row is tapped where the next read draws it, not judged from where it lay closed.
        var closed = PerfCalc();
        var unlaid = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithOpenList(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"),
            RunwayDropDown, 189, 228, rowsAt: new System.Drawing.PointF(0, 0)))!;
        var tablet = new FakeTablet(closed);
        int unlaidReads = 0;
        tablet.OnRead = () => { if (ReferenceEquals(tablet.Page, unlaid) && ++unlaidReads > 1) tablet.Show(PerfCalc(open: true)); };
        tablet.OnTap = _ => tablet.Show(tablet.Taps.Count == 1 ? unlaid : PerfCalc(face: "RW26"));

        Assert.True(await tablet.Runner().ChooseAsync(tablet.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None));

        Assert.Equal(new[] { Named(closed, RunwayDropDown).Hit, Rw26Row }, tablet.Taps);
        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_drop_down_showing_the_entry_taps_nothing()
    {
        var closed = PerfCalc();
        var tablet = new FakeTablet(closed);

        Assert.True(await tablet.Runner().ChooseAsync(tablet.Press(Named(closed, RunwayDropDown)), "RW07L", CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Empty(tablet.Alerts);

        // Pressed on a push showing RW07L, the tablet's fresh read shows RW26 already (B757-18: the press's own read decides).
        var moved = new FakeTablet(closed);
        var press = moved.Press(Named(closed, RunwayDropDown));
        moved.Show(PerfCalc(face: "RW26"));
        Assert.True(await moved.Runner().ChooseAsync(press, "RW26", CancellationToken.None));
        Assert.Empty(moved.Taps);
        Assert.Empty(moved.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_choice_that_did_not_take_says_so(string culture)
    {
        // The row tapped, the list closed, and the face still shows RW07L: said once.
        var closed = PerfCalc();
        var back = new FakeTablet(closed);
        back.OnTap = _ => back.Show(back.Taps.Count == 1 ? PerfCalc(open: true) : closed);
        Assert.False(await TestCulture.Run(culture, () => back.Runner().ChooseAsync(back.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None)));
        Assert.Equal(new[] { Named(closed, RunwayDropDown).Hit, Rw26Row }, back.Taps);
        Assert.Equal(new[] { "Runway could not be set to RW26." }, back.Alerts);

        // The face tapped and the list never opened: nothing more is tapped.
        var shut = new FakeTablet(closed);
        Assert.False(await TestCulture.Run(culture, () => shut.Runner().ChooseAsync(shut.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None)));
        Assert.Single(shut.Taps);
        Assert.Equal(new[] { "Runway could not be set to RW26." }, shut.Alerts);

        // The row tapped and the list left open: said once the wait is over.
        var open = new FakeTablet(PerfCalc(open: true));
        Assert.False(await TestCulture.Run(culture, () => open.Runner().ChooseAsync(open.Press(Named(open.Page, RunwayDropDown)), "RW26", CancellationToken.None)));
        Assert.Equal(new[] { Rw26Row }, open.Taps);
        Assert.Equal(new[] { "Runway could not be set to RW26." }, open.Alerts);

        // An entry the drop-down does not list: nothing tapped.
        var unlisted = new FakeTablet(closed);
        Assert.False(await TestCulture.Run(culture, () => unlisted.Runner().ChooseAsync(unlisted.Press(Named(closed, RunwayDropDown)), "RW99", CancellationToken.None)));
        Assert.Empty(unlisted.Taps);
        Assert.Equal(new[] { "Runway could not be set to RW99." }, unlisted.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_choice_that_took_says_nothing(string culture)
    {
        // The owner's ruling of 2026-11-07 for choice lists: NVDA said the entry as the pilot picked it (CORE-7, B757-20's two-choice rule).
        var closed = PerfCalc();
        var tablet = new FakeTablet(closed);
        tablet.OnTap = _ => tablet.Show(tablet.Taps.Count == 1 ? PerfCalc(open: true) : PerfCalc(face: "RW26"));

        Assert.True(await TestCulture.Run(culture, () => tablet.Runner().ChooseAsync(tablet.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None)));

        Assert.Equal(2, tablet.Taps.Count);
        Assert.Empty(tablet.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_prompt_drop_down_is_judged_by_its_list_closing(string culture)
    {
        // Systems' page menu always shows SELECT, none of its entries (30-systems): choosing an entry changes the page, not the face, so the
        // choice is judged by its list closing after the row's tap (the coordinator's ruling of the fix round). Its list opens below its
        // face (82, four rows: 152 px).
        var apps = Bbs757EfbTreeJson.Load("30-systems");
        var systems = Bbs757EfbModelReader.Read(apps)!;
        var menu = Named(systems, "pb_menu1");
        var opened = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithOpenList(apps, "pb_menu1", 82, 152))!;
        var fuel = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithText(apps, "selected_page_title_label", "FUEL"))!;
        var tablet = new FakeTablet(systems);
        tablet.OnTap = _ => tablet.Show(tablet.Taps.Count == 1 ? opened : fuel);

        Assert.True(await TestCulture.Run(culture, () => tablet.Runner().ChooseAsync(tablet.Press(menu), "28 - FUEL", CancellationToken.None)));

        Assert.Equal(new[] { new Point(1247, 57), new Point(1247, 139) }, tablet.Taps);   // the face, then the second row (120-158)
        Assert.Empty(tablet.Alerts);

        // The system shown already (ELECTRICAL) chosen: the list closes over the page as it was. That took: nothing said.
        var same = new FakeTablet(systems);
        same.OnTap = _ => same.Show(same.Taps.Count == 1 ? opened : systems);
        Assert.True(await TestCulture.Run(culture, () => same.Runner().ChooseAsync(same.Press(menu), "24 - ELECTRICAL", CancellationToken.None)));
        Assert.Equal(new[] { new Point(1247, 57), new Point(1247, 101) }, same.Taps);
        Assert.Empty(same.Alerts);

        // The row tapped and the list left open: not chosen.
        var open = new FakeTablet(opened);
        Assert.False(await TestCulture.Run(culture, () => open.Runner().ChooseAsync(open.Press(Named(opened, "pb_menu1")), "28 - FUEL", CancellationToken.None)));
        Assert.Equal(new[] { new Point(1247, 139) }, open.Taps);
        Assert.Equal(new[] { "Select could not be set to 28 - FUEL." }, open.Alerts);
    }

    [Fact]
    public async Task A_question_raised_after_the_row_tap_holds_back_any_further_step()
    {
        // The review's Minor 4: a question that comes up after the row's tap ends the choice with nothing more tapped and nothing said
        // (B757-18: the read that found it said it), and no "could not be set" over it.
        var closed = PerfCalc();
        var asked = new Bbs757EfbQuestion("Insert desired fuel level (0-2176 Gal)", new[] { Tile("Cancel", "q:CANCEL", new Rectangle(608, 589, 180, 60)) });
        var tablet = new FakeTablet(closed);
        tablet.OnTap = _ => tablet.Show(tablet.Taps.Count == 1 ? PerfCalc(open: true) : PerfCalc(open: true) with { Question = asked });

        Assert.False(await tablet.Runner().ChooseAsync(tablet.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None));

        Assert.Equal(new[] { Named(closed, RunwayDropDown).Hit, Rw26Row }, tablet.Taps);
        Assert.Empty(tablet.Alerts);
    }

    [Fact]
    public async Task A_question_over_the_page_holds_back_a_choice()
    {
        // B757-18: under a question the tablet takes no tap on its page. Up before the press: nothing tapped, nothing said (the read that
        // found it said it). Raised by the face's tap: nothing more tapped, nothing said.
        var closed = PerfCalc();
        var asked = new Bbs757EfbQuestion("Insert desired fuel level (0-2176 Gal)", new[] { Tile("Cancel", "q:CANCEL", new Rectangle(608, 589, 180, 60)) });
        var before = new FakeTablet(closed);
        var press = before.Press(Named(closed, RunwayDropDown));
        before.Show(closed with { Question = asked });
        Assert.False(await before.Runner().ChooseAsync(press, "RW26", CancellationToken.None));
        Assert.Empty(before.Taps);
        Assert.Empty(before.Alerts);

        var raised = new FakeTablet(closed);
        raised.OnTap = _ => raised.Show(PerfCalc(open: true) with { Question = asked });
        Assert.False(await raised.Runner().ChooseAsync(raised.Press(Named(closed, RunwayDropDown)), "RW26", CancellationToken.None));
        Assert.Single(raised.Taps);
        Assert.Empty(raised.Alerts);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_under_an_open_list_is_not_tapped(string culture)
    {
        // The runway list open (25c) covers the Flap Setting drop-down's face: a tap there would land on RW07R or RW08 (the review of Task
        // 5, "Noted"). Nothing is tapped, and the pilot is told why.
        var open = PerfCalc(open: true);
        var tablet = new FakeTablet(open);

        Assert.False(await TestCulture.Run(culture, () => tablet.Runner().ChooseAsync(tablet.Press(Named(open, "planning_perf_calc_flap_dropdown")), "15", CancellationToken.None)));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Flap Setting is under the open Runway list, not pressed." }, tablet.Alerts);
    }

    /// <summary>A drop-down whose list is open over <paramref name="list"/>, for a page that needs one where no capture has it.</summary>
    private static Bbs757EfbItem OpenDropDown(Rectangle list) =>
        new(Bbs757EfbKind.Choice, "Runway", new Rectangle(list.X, list.Y - 34, list.Width, 34), new Point(list.X + list.Width / 2, list.Y - 17), true,
            RunwayDropDown, Value: "RW07L", Entries: new[] { "RW07L" }, Open: true,
            List: new Bbs757EfbOpenList(list, new[] { new Bbs757EfbListRow("RW07L", list with { Height = 38 }) }));

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task The_point_a_slider_is_tapped_at_is_checked_against_an_open_list(string culture)
    {
        // The review's Minor 2: a slider is tapped at its percent's place, not at its middle. A list open over its left end leaves the
        // middle (500,515) clear, but 0 % is tapped at the track's start (300,515), under the list: nothing tapped.
        var zoom = new Bbs757EfbItem(Bbs757EfbKind.Slider, "Zoom", new Rectangle(300, 500, 400, 30), new Point(500, 515), true, "sld:ZOOM",
            Slider: new Bbs757EfbSlider(300, 700, 500));
        var page = PageOf("NAVIGATION", "PERF CALC", OpenDropDown(new Rectangle(250, 450, 100, 200)), zoom);
        var tablet = new FakeTablet(page);

        await TestCulture.Run(culture, () => tablet.Runner().SliderAsync(tablet.Press(zoom), "0", CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Zoom is under the open Runway list, not pressed." }, tablet.Alerts);

        // At 50 % the tap is at its middle, clear of the list: tapped.
        var clear = new FakeTablet(page);
        await TestCulture.Run(culture, () => clear.Runner().SliderAsync(clear.Press(zoom), "50", CancellationToken.None));
        Assert.Equal(new[] { new Point(500, 515) }, clear.Taps);
    }

    [Fact]
    public async Task A_page_drag_that_would_press_on_an_open_list_is_not_made()
    {
        // The review's Minor 2: a page drag presses at the viewport's x + 250 (500); a list open there would take the press. Dirty panel
        // states (content 1202) is dragged from y 899: with a list open over (500, 899), nothing is dragged and nothing tapped.
        var row = new Bbs757EfbItem(Bbs757EfbKind.Button, "Dirty panel states", new Rectangle(273, 1202, 600, 60), new Point(573, 1232), true, "btn:DIRTY");
        var page = PageOf("SETTINGS", "AIRCRAFT", row, OpenDropDown(new Rectangle(450, 850, 100, 100)))
            with { Scrollable = true, Viewport = new Rectangle(250, 82, 1147, 966) };
        Assert.Equal((500, 899, 232), Bbs757EfbPress.DragFor(page, row)!.Value);
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(row), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Dirty panel states is under the open Runway list, not pressed." }, tablet.Alerts);
    }

    // ---- BlueBird's notification panel (plan 21, the final review's I1) ---------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_under_a_BlueBird_notification_is_not_tapped(string culture)
    {
        // A new notification's pop-up covers the top right of the page for about 10 s ([1039.75,38 349.25x118], measured 2026-11-07). The
        // top bar app draws it from its own fields, so the fresh read still finds Livery options' first switch under it, and a tap there
        // would land on the notification. Nothing is tapped, and the pilot is told why (a precision check, B757-19).
        var apps = Bbs757EfbTreeJson.Load("52-settings-livery-options");
        var page = Bbs757EfbModelReader.Read(apps)!;
        var units = page.Items.Single(i => i.Key == "weight_units_toggle");
        var tablet = new FakeTablet(page);
        var press = tablet.Press(units);
        tablet.Show(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(apps))!);

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Units is under a BlueBird notification, not pressed." }, tablet.Alerts);

        // The switch below the pop-up (EHSI up reference, its middle at y 172, the panel's bottom 156) is tapped, and so is the first one
        // once the pop-up has gone.
        var ehsi = page.Items.Single(i => i.Key == "ehsi_up_reference_toggle");
        var below = new FakeTablet(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(apps))!);
        await below.Runner().ClickAsync(below.Press(ehsi), CancellationToken.None);
        Assert.Equal(new[] { ehsi.Hit }, below.Taps);
        var gone = new FakeTablet(page);
        await gone.Runner().ClickAsync(gone.Press(units), CancellationToken.None);
        Assert.Equal(new[] { units.Hit }, gone.Taps);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_drop_down_under_a_BlueBird_notification_is_not_opened(string culture)
    {
        // Systems' page menu (1097,32 300x50) lies inside the pop-up: its face is not tapped, so no page is chosen under the notification.
        var apps = Bbs757EfbTreeJson.Load("30-systems");
        var systems = Bbs757EfbModelReader.Read(apps)!;
        var tablet = new FakeTablet(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(apps))!);

        Assert.False(await TestCulture.Run(culture, () => tablet.Runner().ChooseAsync(tablet.Press(Named(systems, "pb_menu1")), "28 - FUEL", CancellationToken.None)));

        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Select is under a BlueBird notification, not pressed." }, tablet.Alerts);

        // The full list (480 tall) covers PERF calc's Surface drop-down (its middle at 1052,341), which the pop-up alone leaves clear.
        var perf = Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated");
        var listed = new FakeTablet(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(perf, h: 480, list: true))!);
        Assert.False(await TestCulture.Run(culture, () => listed.Runner().ChooseAsync(listed.Press(Named(listed.Page, SurfaceDropDown)), "Snow", CancellationToken.None)));
        Assert.Empty(listed.Taps);
        Assert.Equal(new[] { "Surface Condition is under a BlueBird notification, not pressed." }, listed.Alerts);

        var popped = new FakeTablet(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithNoticePanel(perf))!);
        await popped.Runner().ChooseAsync(popped.Press(Named(popped.Page, SurfaceDropDown)), "Snow", CancellationToken.None);
        Assert.Equal(new[] { Named(popped.Page, SurfaceDropDown).Hit }, popped.Taps.Take(1));
    }

    [Fact]
    public async Task A_page_drag_that_would_press_on_a_BlueBird_notification_is_not_made()
    {
        // A page drag presses at the viewport's x + 250 (500): a notification panel there would take the press. Dirty panel states (content
        // 1202) is dragged from y 899; with a panel over (500, 899), nothing is dragged and nothing tapped.
        var row = new Bbs757EfbItem(Bbs757EfbKind.Button, "Dirty panel states", new Rectangle(273, 1202, 600, 60), new Point(573, 1232), true, "btn:DIRTY");
        var page = PageOf("SETTINGS", "AIRCRAFT", row) with
        {
            Scrollable = true, Viewport = new Rectangle(250, 82, 1147, 966), NoticePanel = new Rectangle(450, 850, 100, 100),
        };
        Assert.Equal((500, 899, 232), Bbs757EfbPress.DragFor(page, row)!.Value);
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(row), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Dirty panel states is under a BlueBird notification, not pressed." }, tablet.Alerts);
    }

    // ---- BlueBird's keyboard up over a press (plan 22, the final review's I2) ----------------------------------------------------

    /// <summary>BlueBird's keyboard where the reader finds it (PERF calc, 25): kb_grid [0,648 1397x400] and its keys.</summary>
    private static readonly Bbs757EfbKeyboard Kb = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25-navigation-perf-calc"))!.Keyboard!;

    private static Point DownKey => Kb.Keys.First(k => k.Name == "btn_keyboard_down").Hit;

    /// <summary>The keyboard up typing into <paramref name="target"/>, settled when <paramref name="ready"/>, and the numeric keypad drawn
    /// at <paramref name="keypad"/> when one is given.</summary>
    private static Bbs757EfbKeyboardState KeyboardUp(string target, bool ready = true, Rectangle? keypad = null) =>
        new(true, ready, keypad != null, target, false, false, false, keypad ?? Rectangle.Empty);

    private static readonly Bbs757EfbKeyboardState KeyboardDown = new(false, false, false, "", false, false, false, Rectangle.Empty);

    /// <summary>A tile low on the Ground services page, under kb_grid (y 648 to 1048) while the keyboard is up.</summary>
    private static Bbs757EfbItem LowTile(string text = "Req fuel truck", string words = "REQ FUEL TRUCK") =>
        Tile(text, "btn:|" + words + "@56,41", new Rectangle(1132, 800, 196, 42));

    private static Bbs757EfbScreenModel WithKeyboard(Bbs757EfbScreenModel page, Bbs757EfbKeyboardState state) =>
        page with { Keyboard = Kb, KeyboardState = state };

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_control_under_the_tablets_keyboard_is_tapped_once_the_keyboard_is_down(string culture)
    {
        // BlueBird's keyboard up (the pilot's own mouse, a typing that stopped, BlueBird itself) covers kb_grid and takes a press there as a
        // key, typing into the box it serves; the window cannot show it (B757-23). A tile under it is tapped once the keyboard is put down
        // with its down key (it keeps the text) and the tile is found again in the read after. Nothing is said of the keyboard: only the
        // tile's own outcome.
        var tile = LowTile();
        var up = WithKeyboard(Ground(tile), KeyboardUp("planning_charts_request_input"));
        Assert.True(Bbs757EfbPress.UnderKeyboard(up, tile.Hit));
        var tablet = new FakeTablet(up);
        var press = tablet.Press(tile);
        tablet.OnTap = p =>
        {
            if (p == DownKey) tablet.Show(WithKeyboard(Ground(tile), KeyboardDown));
            else if (p == tile.Hit && tablet.Page.KeyboardState is { Shown: false })
                tablet.Show(WithKeyboard(Ground(LowTile("Requesting" + Ellipsis, "REQUESTING" + Ellipsis)), KeyboardDown));
        };

        await TestCulture.Run(culture, () => tablet.Runner().ClickAsync(press, CancellationToken.None));

        Assert.Equal(new[] { DownKey, tile.Hit }, tablet.Taps);
        Assert.Equal(new[] { "Requesting" + Ellipsis }, tablet.Alerts);
    }

    [Fact]
    public async Task A_dialogs_answer_under_its_keypad_is_tapped_once_the_keypads_OK_closed_it()
    {
        // The fuel input dialog (11c) with its keypad up below its box ([595.5,348 360x430], measured): INSERT ([862.5,380 180x48]) lies
        // under the keypad's 9, so a tap on it would type a 9. The keypad's OK closes the keypad alone (measured: the dialog stays up), then
        // INSERT is tapped, found again under the same question.
        var dialog = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog"))!;
        var insert = dialog.Question!.Buttons.Single(b => b.Text.Equals("Insert", StringComparison.OrdinalIgnoreCase));
        var keypad = new Rectangle(595, 348, 360, 430);
        var up = WithKeyboard(dialog, KeyboardUp("fuel_input_textbox", keypad: keypad));
        Assert.True(Bbs757EfbPress.UnderKeyboard(up, insert.Hit));
        var ok = Bbs757EfbTyping.KeypadOk(keypad)!.Value;
        var tablet = new FakeTablet(up);
        var press = tablet.Press(insert);
        tablet.OnTap = p =>
        {
            if (p == ok) tablet.Show(WithKeyboard(dialog, KeyboardDown with { Keypad = true, KeypadBox = keypad }));   // +0x1ae stays 1 when closed
        };

        await tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Equal(new[] { ok, insert.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_page_drag_that_would_press_on_the_tablets_keyboard_is_made_once_the_keyboard_is_down()
    {
        // Dirty panel states (content 1202) is dragged into view from (500, 899), inside kb_grid: with the keyboard up the drag's press
        // would be a key. The keyboard is put down first, then the drag made and the row tapped where it is drawn after it.
        var row = new Bbs757EfbItem(Bbs757EfbKind.Button, "Dirty panel states", new Rectangle(273, 1202, 600, 60), new Point(573, 1232), true, "btn:DIRTY");
        var scrolled = row with { Box = new Rectangle(273, 535, 600, 60), Hit = new Point(573, 565) };
        Bbs757EfbScreenModel Settings(Bbs757EfbItem r, Bbs757EfbKeyboardState kb) =>
            WithKeyboard(PageOf("SETTINGS", "AIRCRAFT", r) with { Scrollable = true, Viewport = new Rectangle(250, 82, 1147, 966) }, kb);
        var up = Settings(row, KeyboardUp("planning_charts_request_input"));
        Assert.Equal((500, 899, 232), Bbs757EfbPress.DragFor(up, row)!.Value);
        Assert.True(Bbs757EfbPress.UnderKeyboard(up, new Point(500, 899)));
        var tablet = new FakeTablet(up);
        tablet.OnTap = p => { if (p == DownKey) tablet.Show(Settings(row, KeyboardDown)); };
        tablet.OnDrag = (_, _, _) => tablet.Show(Settings(scrolled, KeyboardDown));

        await tablet.Runner().ClickAsync(tablet.Press(row), CancellationToken.None);

        Assert.Equal(new[] { DownKey, scrolled.Hit }, tablet.Taps);
        Assert.Equal(new[] { (500, 899, 232) }, tablet.Drags);
        Assert.True(tablet.Flow.IndexOf($"tap {DownKey.X},{DownKey.Y}") < tablet.Flow.IndexOf("drag 500,899,232"));
    }

    [Fact]
    public async Task A_keyboard_still_sliding_up_is_waited_for_and_then_put_down()
    {
        // Shown but not settled: a key tapped then lands on the page beneath (measured), so its down key is not tapped until it reads ready.
        var tile = LowTile();
        var sliding = WithKeyboard(Ground(tile), KeyboardUp("planning_charts_request_input", ready: false));
        var tablet = new FakeTablet(sliding);
        int reads = 0;
        tablet.OnRead = () => { if (++reads == 4) tablet.Show(WithKeyboard(Ground(tile), KeyboardUp("planning_charts_request_input"))); };
        bool? readyAtClose = null;
        tablet.OnTap = p =>
        {
            if (p != DownKey) return;
            readyAtClose = tablet.Page.KeyboardState?.Ready;
            tablet.Show(WithKeyboard(Ground(tile), KeyboardDown));
        };

        await tablet.Runner().ClickAsync(tablet.Press(tile), CancellationToken.None);

        Assert.Equal(new[] { DownKey, tile.Hit }, tablet.Taps);
        Assert.True(readyAtClose);
    }

    [Fact]
    public async Task A_keyboard_that_never_settles_is_never_tapped_and_the_press_goes_as_before()
    {
        // Never ready within the runner's change wait: its down key is never tapped (it would land on the page beneath, as a key tapped
        // before it settles does: measured), and the tile is tapped as before.
        var tile = LowTile();
        var never = new FakeTablet(WithKeyboard(Ground(tile), KeyboardUp("planning_charts_request_input", ready: false)));
        await never.Runner().ClickAsync(never.Press(tile), CancellationToken.None);
        Assert.Equal(new[] { tile.Hit }, never.Taps);
        Assert.True(never.Now >= Bbs757EfbPressRunner.ChangeMaxMs);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_keyboard_still_up_after_it_was_put_down_stops_the_press_and_says_so(string culture)
    {
        // The re-review's N3: the one put-down did not take (the read after still shows the keyboard shown and settled over the tile), so
        // a tap would be a key. Nothing more is tapped, no second close, and the pilot is told why (a precision stop, as under a
        // notification: B757-19).
        var tile = LowTile();
        var stays = new FakeTablet(WithKeyboard(Ground(tile), KeyboardUp("planning_charts_request_input")));

        await TestCulture.Run(culture, () => stays.Runner().ClickAsync(stays.Press(tile), CancellationToken.None));

        Assert.Equal(new[] { DownKey }, stays.Taps);
        Assert.Equal(new[] { "Req fuel truck is under the tablet's keyboard, not pressed." }, stays.Alerts);
    }

    [Fact]
    public async Task A_page_drag_under_a_keyboard_still_up_after_it_was_put_down_is_not_made()
    {
        var row = new Bbs757EfbItem(Bbs757EfbKind.Button, "Dirty panel states", new Rectangle(273, 1202, 600, 60), new Point(573, 1232), true, "btn:DIRTY");
        var up = WithKeyboard(PageOf("SETTINGS", "AIRCRAFT", row) with { Scrollable = true, Viewport = new Rectangle(250, 82, 1147, 966) },
            KeyboardUp("planning_charts_request_input"));
        var tablet = new FakeTablet(up);

        await tablet.Runner().ClickAsync(tablet.Press(row), CancellationToken.None);

        Assert.Equal(new[] { DownKey }, tablet.Taps);
        Assert.Empty(tablet.Drags);
        Assert.Equal(new[] { "Dirty panel states is under the tablet's keyboard, not pressed." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_control_clear_of_the_tablets_keyboard_closes_nothing()
    {
        // Req fuel truck (y 577 to 619) lies above kb_grid: tapped at once, the keyboard left as it is.
        var tile = ReqFuelTruck();
        var tablet = new FakeTablet(WithKeyboard(Ground(tile), KeyboardUp("planning_charts_request_input")));

        await tablet.Runner().ClickAsync(tablet.Press(tile), CancellationToken.None);

        Assert.Equal(new[] { tile.Hit }, tablet.Taps);
    }

    // ---- scrolling into view --------------------------------------------------------------------------------------------

    /// <summary>A scrolled page: rows at content positions inside a viewport of 250,82 1147x966, drawn at content less the offset;
    /// a drag moves the offset by y0 - y1, kept between 0 and <see cref="MaxOffset"/>, as the tablet's does (about 1:1).</summary>
    private sealed class Scrolled
    {
        public const int MaxOffset = 214;
        public double Offset { get; set; }
        public double Ratio { get; set; } = 1.0;
        public FakeTablet Tablet { get; }
        private readonly (string Name, int ContentY)[] _rows;

        public Scrolled(double offset, params (string Name, int ContentY)[] rows)
        {
            _rows = rows;
            Offset = offset;
            Tablet = new FakeTablet(PageAt());
            Tablet.OnDrag = (_, y0, y1) =>
            {
                Offset = Math.Clamp(Offset + (y0 - y1) * Ratio, 0, MaxOffset);
                Tablet.Show(PageAt());
            };
        }

        public Bbs757EfbItem Row(string name, double? offset = null)
        {
            int content = _rows.First(r => r.Name == name).ContentY;
            int y = (int)Math.Round(content - (offset ?? Offset));
            return new Bbs757EfbItem(Bbs757EfbKind.Button, name, new Rectangle(273, y, 600, 60), new Point(573, y + 30), true, "btn:" + name.ToUpperInvariant());
        }

        public Bbs757EfbScreenModel PageAt() =>
            PageOf("SETTINGS", "AIRCRAFT", _rows.Select(r => Row(r.Name)).ToArray()) with { Scrollable = true, Viewport = new Rectangle(250, 82, 1147, 966) };
    }

    private static Scrolled TwoRows(double offset) => new(offset, ("First row", 100), ("Dirty panel states", 1202));

    [Fact]
    public async Task A_control_below_the_viewport_is_scrolled_into_view_before_its_tap()
    {
        var s = TwoRows(0);
        var press = s.Tablet.Press(s.Row("Dirty panel states"));   // drawn at 1202, under the viewport's bottom (1048)

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.NotEmpty(s.Tablet.Drags);
        Assert.All(s.Tablet.Drags, d => { Assert.Equal(500, d.X); Assert.True(d.Y0 > d.Y1); });   // content moves up
        var tap = Assert.Single(s.Tablet.Taps);
        var row = s.Row("Dirty panel states");
        Assert.True(row.Box.Contains(tap) && s.PageAt().Viewport.Contains(tap), $"tapped {tap} where it is drawn {row.Box}");
        Assert.True(s.Tablet.Flow.FindIndex(e => e.StartsWith("drag")) < s.Tablet.Flow.FindIndex(e => e.StartsWith("tap")));
    }

    [Fact]
    public async Task A_control_above_the_viewport_scrolls_up()
    {
        var s = new Scrolled(Scrolled.MaxOffset, ("First row", 100), ("Dirty panel states", 1202));   // First row drawn at -114
        var press = s.Tablet.Press(s.Row("First row"));

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.NotEmpty(s.Tablet.Drags);
        Assert.All(s.Tablet.Drags, d => Assert.True(d.Y0 < d.Y1));   // content moves down
        var tap = Assert.Single(s.Tablet.Taps);
        Assert.True(s.Row("First row").Box.Contains(tap) && s.PageAt().Viewport.Contains(tap), $"tapped {tap}");
    }

    [Fact]
    public async Task A_control_that_never_comes_into_view_is_not_tapped()
    {
        var s = TwoRows(0);
        s.Tablet.OnDrag = null;   // the page does not move
        var press = s.Tablet.Press(s.Row("Dirty panel states"));

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        Assert.Empty(s.Tablet.Taps);
        Assert.Single(s.Tablet.Drags);   // a drag that left it where it was ends the loop
        Assert.Equal(new[] { "Dirty panel states could not be brought into view, not pressed." }, s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_control_on_a_page_scrolled_since_the_push_is_tapped_where_it_is_drawn()
    {
        var s = TwoRows(0);
        var press = s.Tablet.Press(s.Row("First row"));   // pressed at offset 0, drawn at 100
        s.Offset = 100;
        s.Tablet.Show(s.PageAt());                         // now drawn at 0: above the viewport's top (82)? 0..60 is outside

        await s.Tablet.Runner().ClickAsync(press, CancellationToken.None);

        var tap = Assert.Single(s.Tablet.Taps);
        Assert.True(s.Row("First row").Box.Contains(tap) && s.PageAt().Viewport.Contains(tap), $"tapped {tap}");
    }

    [Fact]
    public async Task A_control_left_of_the_viewport_is_tapped_where_it_is()
    {
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("30-systems"))!;
        var zoom = page.Items.First(i => i.Clickable && i.Box.Right <= page.Viewport.Left && i.Name.Contains("elec_zoom_in", StringComparison.Ordinal));
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(zoom), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Equal(new[] { zoom.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_page_without_a_scrollbox_is_never_dragged()
    {
        var below = Tile("Below", "btn:BELOW", new Rectangle(300, 1100, 200, 40));
        var tablet = new FakeTablet(PageOf("NAVIGATION", "MAP", below));   // Scrollable is false

        await tablet.Runner().ClickAsync(tablet.Press(below), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Empty(tablet.Taps);
        Assert.Equal(new[] { "Below could not be brought into view, not pressed." }, tablet.Alerts);
    }

    [Fact]
    public async Task A_control_in_view_taps_without_a_drag()
    {
        var s = TwoRows(0);
        var row = s.Row("First row");

        await s.Tablet.Runner().ClickAsync(s.Tablet.Press(row), CancellationToken.None);

        Assert.Empty(s.Tablet.Drags);
        Assert.Equal(new[] { row.Hit }, s.Tablet.Taps);
    }

    /// <summary>Settings, Aircraft read from its committed tree with the page scrolled by <paramref name="offset"/>.</summary>
    private static Bbs757EfbScreenModel SettingsAircraftAt(double offset) =>
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Scrolled(Bbs757EfbTreeJson.Load("51-settings-aircraft"), "aircraft_scrollbox", (float)offset))!;

    /// <summary>The end of Settings, Aircraft's scroll: its stackbox's 1,180 less its scrollbox's 966, read at +0x134 (B757-26;
    /// measured 2026-11-07). The scrollbar thumb at +0x158 stops at 175.19, short of it.</summary>
    private const double SettingsAircraftEnd = 214;

    /// <summary>A mid-scroll offset of Settings, Aircraft at which a 10.2 px strip of its last switch was measured to take a tap
    /// (2026-11-07).</summary>
    private const double SettingsAircraftMidScroll = 176.7;

    [Fact]
    public async Task A_partly_shown_last_row_is_tapped_in_its_shown_part()
    {
        // The real tree mid-scroll: Dirty panel states' toggle [1137,1214.5 60x35] is drawn from 1037.8 and only its top 10 px or so
        // show above the viewport's bottom (1048). It is tapped there, in the middle of that strip, with no drag.
        var page = SettingsAircraftAt(SettingsAircraftMidScroll);
        var dirty = page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        var tablet = new FakeTablet(page);

        await tablet.Runner().SwitchAsync(tablet.Press(dirty), wantOn: true, CancellationToken.None);

        Assert.Empty(tablet.Drags);
        var tap = Assert.Single(tablet.Taps);
        Assert.InRange(tap.Y, dirty.Box.Top, 1047);
        Assert.Equal(dirty.Box.Top + (1048 - dirty.Box.Top) / 2, tap.Y);
        Assert.Equal(dirty.Hit.X, tap.X);
    }

    [Fact]
    public async Task The_last_row_of_the_real_settings_page_is_scrolled_into_view_and_tapped_from_the_top()
    {
        // Review Focus 1 on the real tree: pressed at the top of the page, Dirty panel states is dragged into view (the drag moves the
        // page 1:1 up to its measured end, 214), read again and tapped where it is drawn then, inside the viewport.
        double offset = 0;
        var tablet = new FakeTablet(SettingsAircraftAt(0));
        var press = tablet.Press(tablet.Page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle));
        tablet.OnDrag = (_, y0, y1) =>
        {
            offset = Math.Clamp(offset + (y0 - y1), 0, SettingsAircraftEnd);
            tablet.Show(SettingsAircraftAt(offset));
        };

        await tablet.Runner().SwitchAsync(press, wantOn: true, CancellationToken.None);

        Assert.Single(tablet.Drags);
        var tap = Assert.Single(tablet.Taps);
        var drawn = tablet.Page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        Assert.True(tap.Y >= drawn.Box.Top && tap.Y < 1048, $"tapped {tap}, drawn {drawn.Box}");
        Assert.DoesNotContain(tablet.Alerts, a => a.Contains("could not be brought into view", StringComparison.Ordinal));
    }

    // ---- C1: a page whose only scrollbox is an inner one --------------------------------------------------------------------

    [Theory]
    [InlineData("20-navigation-acars-dashboard", "planning_acars_fetch_button")]
    [InlineData("22-navigation-weather", "planning_weather_primary_tab_button_0")]
    [InlineData("40-checklist", "checklist_reset")]
    public async Task A_control_outside_an_inner_scrollbox_is_tapped_once_with_no_drag(string fixture, string name)
    {
        // REFRESH SIMBRIEF OFP above the ACARS route's box, METAR above Weather's report box, the Checklist's RESET above its lines.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!;
        var control = Assert.Single(page.Items, i => i.Name == name);
        var tablet = new FakeTablet(page);

        await tablet.Runner().ClickAsync(tablet.Press(control), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Equal(new[] { control.Hit }, tablet.Taps);
        Assert.DoesNotContain(tablet.Alerts, a => a.Contains("could not be brought into view", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_long_way_takes_several_drags()
    {
        var s = new Scrolled(0, ("Far row", 5000));
        s.Tablet.OnDrag = (_, y0, y1) =>
        {
            s.Offset += y0 - y1;   // no end: a long page
            s.Tablet.Show(s.PageAt());
        };

        await s.Tablet.Runner().ClickAsync(s.Tablet.Press(s.Row("Far row")), CancellationToken.None);

        Assert.True(s.Tablet.Drags.Count >= 2);
        Assert.True(s.Tablet.Drags.Count <= Bbs757EfbPressRunner.MaxScrollSteps);
        Assert.All(s.Tablet.Drags, d => Assert.True(d.Y0 - d.Y1 <= 966 - 200));
        Assert.Single(s.Tablet.Taps);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task A_drag_that_cannot_run_says_so_and_taps_nothing(string culture)
    {
        var s = TwoRows(0);
        s.Tablet.DragReachable = false;

        await TestCulture.Run(culture, () => s.Tablet.Runner().ClickAsync(s.Tablet.Press(s.Row("Dirty panel states")), CancellationToken.None));

        Assert.Empty(s.Tablet.Taps);
        Assert.Equal(new[] { Bbs757EfbPress.UnreachableText }, s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_page_that_never_brings_the_control_in_stops_after_the_most_drags()
    {
        // Each drag moves the page, but the control never arrives (a page far longer than the drags can cover): at most MaxScrollSteps
        // drags, then no tap and "could not be brought into view".
        var s = new Scrolled(0, ("Far row", 100_000));
        s.Tablet.OnDrag = (_, y0, y1) =>
        {
            s.Offset += (y0 - y1) / 10.0;   // a tenth of each drag: it moves, but never far enough
            s.Tablet.Show(s.PageAt());
        };

        await s.Tablet.Runner().ClickAsync(s.Tablet.Press(s.Row("Far row")), CancellationToken.None);

        Assert.Equal(Bbs757EfbPressRunner.MaxScrollSteps, s.Tablet.Drags.Count);
        Assert.Empty(s.Tablet.Taps);
        Assert.Equal(new[] { "Far row could not be brought into view, not pressed." }, s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_press_canceled_while_it_scrolls_taps_nothing_and_says_nothing()
    {
        // The window hid (or the bridge stopped) between two drags: the next drag answers false as canceled, and nothing is said.
        var s = new Scrolled(0, ("Far row", 5000));
        using var cts = new CancellationTokenSource();
        var runner = new Bbs757EfbPressRunner(new Bbs757EfbPressIo
        {
            Read = _ => Task.FromResult<Bbs757EfbScreenModel?>(s.Tablet.Page),
            Drag = (_, y0, y1, ct) =>
            {
                if (ct.IsCancellationRequested) return Task.FromResult(false);
                s.Offset += y0 - y1;
                s.Tablet.Show(s.PageAt());
                cts.Cancel();   // canceled after the first drag
                return Task.FromResult(true);
            },
            Tap = (p, _) => { s.Tablet.Taps.Add(p); return Task.FromResult(true); },
            TapRun = (_, _, _) => Task.FromResult(true),
            ReadInstalledCycle = () => null,
            ReadVar = (_, _) => Task.FromResult<double?>(null),
            ReadRegistered = (_, _) => Task.FromResult<double?>(null),
            ReadSetting = (_, _) => Task.FromResult<Bbs757Value?>(null),
            Alert = s.Tablet.Alerts.Add,
            Delay = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            NowMs = () => 0,
            ArmServiceEcho = _ => { },
            SettleServiceEcho = (_, _) => { },
            ArmGroundPowerEcho = () => { },
            SettleGroundPowerEcho = _ => { },
            Hooked = (_, _) => { },
        });

        await runner.ClickAsync(s.Tablet.Press(s.Row("Far row")), cts.Token);

        Assert.Empty(s.Tablet.Taps);
        Assert.Empty(s.Tablet.Alerts);
    }

    [Fact]
    public async Task A_control_in_the_side_menu_taps_where_it_is_with_no_drag()
    {
        var home = new Bbs757EfbItem(Bbs757EfbKind.Menu, "Fuel", new Rectangle(43, 216, 58, 29), new Point(72, 230), true, "menu:FUEL");
        var tablet = new FakeTablet(new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { home }, new[] { ReqFuelTruck() }));

        await tablet.Runner().ClickAsync(tablet.Press(home), CancellationToken.None);

        Assert.Empty(tablet.Drags);
        Assert.Equal(new[] { home.Hit }, tablet.Taps);
    }

    [Fact]
    public async Task A_tab_press_says_its_new_words_once()
    {
        var tab = new Bbs757EfbItem(Bbs757EfbKind.Tab, "Hydraulic", new Rectangle(300, 200, 150, 40), new Point(375, 220), true, "tab:HYDRAULIC");
        var tablet = new FakeTablet(PageOf("AIRCRAFT", "MAINTENANCE", tab));
        // The reader keeps a tab's words and flips only Selected; "(current)" is the window's.
        tablet.OnTap = _ => tablet.Show(PageOf("AIRCRAFT", "MAINTENANCE", tab with { Selected = true }));

        await tablet.Runner().ClickAsync(tablet.Press(tab), CancellationToken.None);

        Assert.Equal(new[] { "Hydraulic (current)" }, tablet.Alerts);
    }

    // ---- the pushes: a press is the push's it was made on ---------------------------------------------------------------

    private static Dictionary<string, string> ItemsByText(Bbs757EfbElements.Built b)
    {
        var o = new Dictionary<string, string>();
        for (int i = 0; i < b.Count; i++) o[b.Data[$"items.{i}.text"]] = i.ToString();
        return o;
    }

    private static string Field(Bbs757EfbElements.Built b, string text, string field)
    {
        var map = ItemsByText(b);
        Assert.True(map.ContainsKey(text), "no element " + text + ": " + string.Join(" | ", map.Keys));
        return b.Data.TryGetValue($"items.{map[text]}.{field}", out var v) ? v : "";
    }

    private static List<int> Numbers(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count).Select(i => int.Parse(b.Data[$"items.{i}.aidx"])).ToList();

    [Fact]
    public void A_press_from_an_older_push_finds_that_push_and_its_page()
    {
        var pushes = new Bbs757EfbPushes();
        var first = pushes.Show(Ground(FuelTruck("off")));
        var built1 = pushes.Push(first, null, NoExtras);
        var second = pushes.Show(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true)));
        var built2 = pushes.Push(second, null, NoExtras);

        int aidx = int.Parse(Field(built1, "Fuel truck off", "aidx"));
        Assert.True(pushes.TryFind(aidx, out var action, out var page));
        Assert.Equal("Fuel truck off", action!.Item!.Text);
        Assert.Same(first, page);
        Assert.Empty(Numbers(built1).Intersect(Numbers(built2)));
        Assert.False(pushes.TryFind(0, out _, out _));
        Assert.False(pushes.TryFind(Numbers(built1).Concat(Numbers(built2)).Max() + 1, out _, out _));
    }

    [Fact]
    public void An_element_keeps_its_number_while_it_stays_on_the_page()
    {
        var pushes = new Bbs757EfbPushes();
        var ground = Ground(FuelTruck("off"), ReqTug());
        var page = pushes.Show(ground);
        var before = pushes.Push(page, null, NoExtras);
        var withAlert = pushes.Push(page, "Req PB tug: no change on the tablet.", NoExtras);
        var reread = pushes.Push(pushes.Show(ground with { }), null, NoExtras);

        Assert.Equal(Numbers(before), Numbers(withAlert).Skip(1));
        Assert.Equal(Numbers(before), Numbers(reread));
        Assert.DoesNotContain(Numbers(withAlert)[0], Numbers(before));

        var away = pushes.Push(pushes.Show(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true))), null, NoExtras);
        var back = pushes.Push(pushes.Show(ground), null, NoExtras);
        Assert.Empty(Numbers(back).Intersect(Numbers(before).Concat(Numbers(withAlert)).Concat(Numbers(away))));
    }

    [Fact]
    public void Each_question_numbers_its_answers_apart()
    {
        var pushes = new Bbs757EfbPushes();
        var defuel = pushes.Show(FuelAsked(DefuelQuestion, YesAnswer));
        var a = pushes.Push(defuel, null, NoExtras);
        var b = pushes.Push(pushes.Show(FuelAsked(UnitsQuestion, YesAnswer)), null, NoExtras);

        int yesA = int.Parse(Field(a, "Yes", "aidx")), yesB = int.Parse(Field(b, "Yes", "aidx"));
        Assert.NotEqual(yesA, yesB);
        Assert.True(pushes.TryFind(yesA, out _, out var page));
        Assert.Same(defuel, page);

        var again = pushes.Push(pushes.Show(FuelAsked(UnitsQuestion.Replace("system?", "system ?"), YesAnswer)), null, NoExtras);
        Assert.Equal(yesB, int.Parse(Field(again, "Yes", "aidx")));
    }

    [Fact]
    public void Old_pushes_are_let_go()
    {
        var pushes = new Bbs757EfbPushes();
        var builds = new List<Bbs757EfbElements.Built>();
        for (int i = 0; i < Bbs757EfbPushes.KeptPushes + 1; i++)
            builds.Add(pushes.Push(pushes.Show(PageOf("AIRCRAFT", "SECTION " + i, ReqFuelTruck())), null, NoExtras));

        Assert.False(pushes.TryFind(builds[0].Actions.Keys.First(), out _, out _));
        Assert.True(pushes.TryFind(builds[1].Actions.Keys.First(), out _, out _));
    }

    // ---- what a read says by itself: a new question, BlueBird's new notifications ----------------------------------------

    private static Bbs757EfbScreenModel Asked(string text) =>
        Ground(ReqFuelTruck()) with { Question = new Bbs757EfbQuestion(text, new[] { YesAnswer }) };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_new_question_is_said_once(string culture)
    {
        var news = new Bbs757EfbReadNews();
        var quiet = Ground(ReqFuelTruck());
        var asked = Asked("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?");

        var said = TestCulture.Run(culture, () => new[]
        {
            news.After(quiet, long.MaxValue),
            news.After(asked, long.MaxValue),
            news.After(asked with { }, long.MaxValue),
            news.After(Asked("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel ?"), long.MaxValue),
            news.After(quiet, long.MaxValue),
            news.After(asked, 100),
            news.After(Asked("Fuel adjustment is complete!"), long.MaxValue),
        });

        Assert.Equal(new[]
        {
            null,
            "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?",
            null,
            null,
            null,
            "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?",
            "Fuel adjustment is complete!",
        }, said);
    }

    [Fact]
    public void The_first_read_takes_the_notifications_as_they_are()
    {
        var news = new Bbs757EfbReadNews();
        var listed = Ground(ReqFuelTruck()) with
        {
            Notices = new[] { new Bbs757EfbNotice(2, "GPU connected.", "14:17Z"), new Bbs757EfbNotice(1, "R3 door closed.", "12:44Z") },
        };
        var more = listed with { Notices = new[] { new Bbs757EfbNotice(3, "Chocks attached.", "14:20Z") }.Concat(listed.Notices).ToList() };

        Assert.Null(news.After(listed, long.MaxValue));
        Assert.Equal("Chocks attached.", news.After(more, long.MaxValue));
        Assert.Null(news.After(more, long.MaxValue));
    }

    /// <summary>The page <paramref name="fixture"/> as the memory reader reads it, with the BlueBird notifications its capture holds.</summary>
    private static Bbs757EfbScreenModel Captured(string fixture, IReadOnlyList<Bbs757EfbNoticeRecord>? notices = null) =>
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture), notices ?? Bbs757EfbTreeJson.LoadNotices(fixture))!;

    /// <summary>The page <paramref name="fixture"/> read while BBOS's notification vector was torn (the walk read none of it).</summary>
    private static Bbs757EfbScreenModel Torn(string fixture) => Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture), null)!;

    [Fact]
    public void Notifications_that_came_while_the_window_was_hidden_are_listed_not_said()
    {
        // The window read Cabin & doors (up to id 51), was closed while the door and the chocks changed (52-55), and is shown again:
        // that read did not see them appear, so it lists them and says none, as the window's first read does; the next new one is said.
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(Captured("14-aircraft-cabin-doors"));
        h.Bridge.SetActive(false);
        h.Bridge.SetActive(true);

        h.Bridge.TakeRead(Captured("25c-navigation-perf-calc-runway-open"));

        Assert.DoesNotContain(h.Pushes[^1], kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");
        Assert.Contains("05:06Z Chocks removed.", Texts(h.Pushes[^1]));
        var again = Bbs757EfbTreeJson.LoadNotices("25c-navigation-perf-calc-runway-open")
            .Append(new Bbs757EfbNoticeRecord(56, "", "CHOCKS ATTACHED.", "05:09Z", true)).ToList();
        h.Bridge.TakeRead(Captured("25c-navigation-perf-calc-runway-open", again));
        Assert.Equal(("alert", "Chocks attached."), (h.Pushes[^1]["items.0.kind"], h.Pushes[^1]["items.0.text"]));
    }

    [Fact]
    public void A_read_while_the_window_is_hidden_does_not_spend_the_next_showings_start()
    {
        // A press still reading after the pilot closed the window (its follow-up reads run on for up to a few seconds) reads 52 while
        // hidden; 53 to 55 come while it stays closed. That read is heard by nobody, so it neither says anything nor takes the next
        // showing's start: shown again, the first read lists 53 to 55 and says none of them.
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(Captured("14-aircraft-cabin-doors"));
        h.Bridge.SetActive(false);
        h.Bridge.TakeRead(Captured("14b-aircraft-cabin-doors-door-moving"));
        Assert.DoesNotContain(h.Pushes[^1], kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");
        h.Bridge.SetActive(true);

        h.Bridge.TakeRead(Captured("25c-navigation-perf-calc-runway-open"));

        Assert.DoesNotContain(h.Pushes[^1], kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");
        Assert.Contains("05:06Z Chocks removed.", Texts(h.Pushes[^1]));
        var again = Bbs757EfbTreeJson.LoadNotices("25c-navigation-perf-calc-runway-open")
            .Append(new Bbs757EfbNoticeRecord(56, "", "CHOCKS ATTACHED.", "05:09Z", true)).ToList();
        h.Bridge.TakeRead(Captured("25c-navigation-perf-calc-runway-open", again));
        Assert.Equal(("alert", "Chocks attached."), (h.Pushes[^1]["items.0.kind"], h.Pushes[^1]["items.0.text"]));
    }

    [Fact]
    public void A_dialog_still_up_when_the_window_is_shown_again_is_said_again()
    {
        // The same Shift+T does the same every time: a dialog up when the window is shown is said once, as at its first showing.
        using var h = new BridgeHarness();
        var dialog = Captured("11c-aircraft-fuel-input-dialog");
        h.Bridge.TakeRead(dialog);
        Assert.Equal("Insert desired fuel level (0-2176 Gal)", h.Pushes[^1]["items.0.text"]);
        int pushes = h.Pushes.Count;
        h.Bridge.TakeRead(dialog);
        Assert.Equal(pushes, h.Pushes.Count);   // read again while shown: not said again

        h.Bridge.SetActive(false);
        h.Bridge.SetActive(true);
        h.Bridge.TakeRead(dialog);

        Assert.Equal(pushes + 1, h.Pushes.Count);
        Assert.Equal(("alert", "Insert desired fuel level (0-2176 Gal)"), (h.Pushes[^1]["items.0.kind"], h.Pushes[^1]["items.0.text"]));
    }

    [Fact]
    public void The_first_notification_after_BlueBird_starts_over_is_said()
    {
        // A flight reload with the app running: BlueBird's new session starts with no notification (read whole and empty), then its ids
        // start again at 1. The empty list is what the next read is compared with, so its first notification is said.
        var news = new Bbs757EfbReadNews();
        var ground = Captured("13-aircraft-ground-services", Array.Empty<Bbs757EfbNoticeRecord>());

        var said = new[]
        {
            news.After(Captured("25c-navigation-perf-calc-runway-open"), long.MaxValue),
            news.After(ground, long.MaxValue),
            news.After(Captured("13-aircraft-ground-services", new[] { new Bbs757EfbNoticeRecord(1, "", "GPU CONNECTED.", "06:00Z", true) }), long.MaxValue),
        };

        Assert.Equal(new[] { null, null, "GPU connected." }, said);
        // Clear all (the list emptied, its ids going on) is the same: the next one is said.
        var cleared = new Bbs757EfbReadNews();
        cleared.After(Captured("25c-navigation-perf-calc-runway-open"), long.MaxValue);
        cleared.After(ground, long.MaxValue);
        Assert.Equal("Chocks attached.", cleared.After(Captured("13-aircraft-ground-services",
            new[] { new Bbs757EfbNoticeRecord(56, "", "CHOCKS ATTACHED.", "05:09Z", true) }), long.MaxValue));
    }

    [Fact]
    public void A_torn_first_read_does_not_make_the_backlog_spoken()
    {
        // The window's first read found BBOS's notification vector torn: there is nothing to compare with yet, so the next whole read is
        // the backlog, listed and not said; only what comes after it is.
        var news = new Bbs757EfbReadNews();

        var said = new[]
        {
            news.After(Torn("13-aircraft-ground-services"), long.MaxValue),
            news.After(Captured("13-aircraft-ground-services"), long.MaxValue),
            news.After(Captured("25c-navigation-perf-calc-runway-open"), long.MaxValue),
        };

        Assert.Equal(new[] { null, null, "Chocks removed." }, said);
        Assert.False(Torn("13-aircraft-ground-services").NoticesWhole);
        Assert.Empty(Torn("13-aircraft-ground-services").Notices);
        Assert.True(Captured("13-aircraft-ground-services").NoticesWhole);
    }

    [Fact]
    public void A_dialog_with_no_words_is_not_said_as_an_empty_alert()
    {
        // A dialog of a text box and answers alone (none measured: every one carries words) shows them first, and says nothing: an
        // alert with no words is none.
        var page = Ground(ReqFuelTruck());
        var wordless = page with
        {
            Question = new Bbs757EfbQuestion("", new[] { Tile("OK", "q:OK", new Rectangle(608, 589, 180, 60)) }),
        };
        var news = new Bbs757EfbReadNews();

        Assert.Null(news.After(page, long.MaxValue));
        Assert.Null(news.After(wordless, long.MaxValue));
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(page);
        h.Bridge.TakeRead(wordless);
        Assert.DoesNotContain(h.Pushes[^1], kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");
        Assert.Equal("OK", h.Pushes[^1]["items.0.text"]);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void New_notifications_are_spoken_once_and_the_backlog_is_not(string culture)
    {
        // The captures of 2026-11-07 in the order they were made: Cabin & doors with 49 notifications (up to id 51), the AFT cargo door
        // opening (52), closed with the chocks attached (53, 54), the chocks removed (55). The window's first read lists the backlog and
        // says none of it; each id after it is said once, when it first shows, as the window's alert. A read whose notifications were
        // torn (none read) says nothing, and the next whole read says only what is new.
        var moving = Captured("14b-aircraft-cabin-doors-door-moving");
        var news = new Bbs757EfbReadNews();
        var said = TestCulture.Run(culture, () => new[]
        {
            news.After(Captured("14-aircraft-cabin-doors"), long.MaxValue),
            news.After(moving, long.MaxValue),
            news.After(Torn("14b-aircraft-cabin-doors-door-moving"), long.MaxValue),
            news.After(Captured("13-aircraft-ground-services"), long.MaxValue),
            news.After(Captured("25c-navigation-perf-calc-runway-open"), long.MaxValue),
            news.After(Captured("51b-settings-aircraft-scrolled"), long.MaxValue),
        });
        Assert.Equal(new[] { null, "AFT cargo door opened.", null, "AFT cargo door closed. Chocks attached.", "Chocks removed.", null }, said);

        // Through the bridge: the first read is pushed with the backlog listed and no alert; the next says the new one as its alert.
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(Captured("14-aircraft-cabin-doors"));
        Assert.DoesNotContain(h.Pushes[^1], kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");
        Assert.Contains("03:16Z AFT cargo door closed.", Texts(h.Pushes[^1]));
        h.Bridge.TakeRead(moving);
        Assert.Equal(("alert", "AFT cargo door opened."), (h.Pushes[^1]["items.0.kind"], h.Pushes[^1]["items.0.text"]));
        Assert.Contains("05:05Z AFT cargo door opened.", Texts(h.Pushes[^1]));
    }

    [Fact]
    public void A_repeated_notification_is_spoken_per_appearance()
    {
        // A second CHOCKS ATTACHED. in the same minute as the first is a new record with its own id: said too, once.
        var records = Bbs757EfbTreeJson.LoadNotices("25c-navigation-perf-calc-runway-open");
        var again = records.Append(new Bbs757EfbNoticeRecord(56, "", "CHOCKS ATTACHED.", "05:06Z", true)).ToList();
        var news = new Bbs757EfbReadNews();

        var said = new[]
        {
            news.After(Captured("13-aircraft-ground-services"), long.MaxValue),
            news.After(Captured("13-aircraft-ground-services", records), long.MaxValue),
            news.After(Captured("13-aircraft-ground-services", again), long.MaxValue),
            news.After(Captured("13-aircraft-ground-services", again), long.MaxValue),
        };

        Assert.Equal(new[] { null, "Chocks removed.", "Chocks attached.", null }, said);
        Assert.Equal(2, Captured("13-aircraft-ground-services", again).Notices.Count(n => n.Text == "Chocks attached." && n.Time == "05:06Z"));
    }

    [Fact]
    public async Task A_typing_run_keeps_the_notifications_quiet_from_its_last_tap()
    {
        // The final review's M2: a run of 40 typing taps at Bbs757EfbTyping.TapGapMs lasts 4 s, as long as QuietAfterPressMs. Counted from
        // the run's start, the quiet window had run out by its last tap, and the notification its typing raised (Charts acts at the first
        // valid code) was said over the typing's own read-back. It counts from the run's end.
        using var h = new BridgeHarness();
        h.RunTaps = (seq, _) =>
        {
            h.Now += seq.Sum(s => s.DelayAfterMs);   // the run lasts its taps' gaps
            return Task.FromResult((Bbs757EfbRunResult.Done, false));
        };
        var quiet = Ground(ReqFuelTruck());
        h.Bridge.TakeRead(quiet);

        var keys = Enumerable.Repeat(new Point(40, 750), Bbs757EfbTyping.MaxTapsPerRun).ToList();
        Assert.True(await h.Bridge.TapRunAsync(keys, Bbs757EfbTyping.TapGapMs, CancellationToken.None));
        Assert.True(h.Now >= Bbs757EfbNotices.QuietAfterPressMs);

        h.Bridge.TakeRead(quiet with { Notices = new[] { new Bbs757EfbNotice(1, "MUNICH charts received.", "14:17Z") } });
        Assert.DoesNotContain(h.Pushes[^1], kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");

        // Once the quiet window has passed, a new notification is said as before.
        h.Now += Bbs757EfbNotices.QuietAfterPressMs;
        h.Bridge.TakeRead(quiet with
        {
            Notices = new[] { new Bbs757EfbNotice(2, "GPU connected.", "14:18Z"), new Bbs757EfbNotice(1, "MUNICH charts received.", "14:17Z") },
        });
        Assert.Equal(("alert", "GPU connected."), (h.Pushes[^1]["items.0.kind"], h.Pushes[^1]["items.0.text"]));
    }

    [Fact]
    public void A_notification_right_after_the_pilots_own_press_is_not_said()
    {
        var news = new Bbs757EfbReadNews();
        var quiet = Ground(ReqFuelTruck());
        var toast = quiet with { Notices = new[] { new Bbs757EfbNotice(1, "GPU connected.", "14:17Z") } };

        Assert.Null(news.After(quiet, long.MaxValue));
        Assert.Null(news.After(toast, Bbs757EfbNotices.QuietAfterPressMs - 1));
        Assert.Null(news.After(toast, long.MaxValue));
    }

    [Fact]
    public void A_question_and_a_notification_together_are_one_text_the_question_first()
    {
        var news = new Bbs757EfbReadNews();
        var quiet = Ground(ReqFuelTruck());
        var both = quiet with
        {
            Question = new Bbs757EfbQuestion("Switch the aircraft unit system?", new[] { YesAnswer }),
            Notices = new[] { new Bbs757EfbNotice(1, "SIMBRIEF OFP received.", "14:17Z") },
        };

        Assert.Null(news.After(quiet, long.MaxValue));
        Assert.Equal("Switch the aircraft unit system? SIMBRIEF OFP received.", news.After(both, long.MaxValue));
    }

    // ---- the bridge itself: what a read pushes, and the AIRAC page's extras ------------------------------------------------

    /// <summary>A bridge whose loop never started, made with no synchronization context (its pushes raised inline), each push kept;
    /// the memory read, sign-in state and flow are the test's. Its window is shown (<see cref="Bbs757EfbBridge.SetActive"/>), as it is
    /// whenever the bridge reads.</summary>
    private sealed class BridgeHarness : IDisposable
    {
        private readonly CoherentBbs757EfbClient _client = new(() => "");

        public BridgeHarness()
        {
            SynchronizationContext? saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                Bridge = new Bbs757EfbBridge(_client, new Bbs757EfbBridgeIo
                {
                    ReadModel = _ => Task.FromResult(ModelRead()),
                    ReadRegistered = (_, _) => Task.FromResult<double?>(null),
                    ReadSetting = (_, _) => Task.FromResult<Bbs757Value?>(null),
                    SignedIn = _ => { SignedInReads++; return Task.FromResult(SignedInAnswer); },
                    SignIn = ct => SignInFlow(ct),
                    Hooked = (hook, _) => Hooks.Add(hook),
                    ArmServiceEcho = _ => { },
                    SettleServiceEcho = (_, _) => { },
                    ArmGroundPowerEcho = () => { },
                    SettleGroundPowerEcho = _ => { },
                    NowMs = () => Now,
                    WaitForCommand = (ms, _) => { Now += ms; return Task.CompletedTask; },
                    Run = (seq, ct) => RunTaps?.Invoke(seq, ct) ?? Task.FromResult((Bbs757EfbRunResult.Unreachable, false)),
                });
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(saved);
            }
            Bridge.StateUpdated += (_, e) => Pushes.Add(e.Data);
            Bridge.SetActive(true);
        }

        public Bbs757EfbBridge Bridge { get; }
        /// <summary>The clock the window's commands are timed by; a wait for a set to settle moves it.</summary>
        public long Now { get; set; }
        public bool? SignedInAnswer { get; set; }
        public int SignedInReads { get; private set; }
        public Func<Bbs757EfbModelRead> ModelRead { get; set; } = () => new Bbs757EfbModelRead(null, "no simulator in this test");
        public Func<CancellationToken, Task<string>> SignInFlow { get; set; } = _ => Task.FromResult("");
        /// <summary>The EFB agent's runs of taps: how one ended and whether it may have clicked; null, none reaches the tablet (Unreachable),
        /// as with the harness's client, which never connects.</summary>
        public Func<IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>, CancellationToken, Task<(Bbs757EfbRunResult, bool)>>? RunTaps { get; set; }
        public List<Bbs757EfbHook> Hooks { get; } = new();
        public List<Dictionary<string, string>> Pushes { get; } = new();

        public void Dispose()
        {
            Bridge.Dispose();
            _client.Dispose();
        }
    }

    private static List<string> Texts(Dictionary<string, string> push) =>
        Enumerable.Range(0, push.Keys.Count(k => k.EndsWith(".text", StringComparison.Ordinal))).Select(i => push[$"items.{i}.text"]).ToList();

    private static int IndexOf(Dictionary<string, string> push, string text)
    {
        int index = Texts(push).IndexOf(text);
        Assert.True(index >= 0, $"No element '{text}': " + string.Join(" | ", Texts(push)));
        return index;
    }

    private static Bbs757EfbScreenModel AiracPage() => PageOf("SETTINGS", "AIRAC DATA",
        new Bbs757EfbItem(Bbs757EfbKind.Row, "SimBrief ID number", new Rectangle(273, 200, 250, 30), new Point(398, 215), false, "row:SIMBRIEF ID NUMBER", Value: "set"));

    [Fact]
    public void A_read_with_a_new_question_is_pushed_with_it_as_the_one_alert()
    {
        using var h = new BridgeHarness();
        var quiet = PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@25,47", new Rectangle(377, 929, 280, 45)));
        h.Bridge.TakeRead(quiet);
        var asked = quiet with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { YesAnswer }) };

        h.Bridge.TakeRead(asked);

        var push = h.Pushes[^1];
        Assert.Equal("alert", push["items.0.kind"]);
        Assert.Equal(asked.Question!.Text, push["items.0.text"]);
        Assert.Equal(asked.Question.Text, push["items.1.text"]);
        Assert.Equal("", push["items.1.kind"]);
        Assert.Single(push, kv => kv.Key.EndsWith(".kind", StringComparison.Ordinal) && kv.Value == "alert");

        int pushes = h.Pushes.Count;
        h.Bridge.TakeRead(asked with { });
        Assert.Equal(pushes, h.Pushes.Count);
    }

    [Fact]
    public void Another_page_gets_other_numbers()
    {
        using var h = new BridgeHarness();
        h.Bridge.TakeRead(Ground(FuelTruck("off")));
        h.Bridge.TakeRead(PageOf("SETTINGS", "CALLOUTS", Switch("80 KTS", "tgl:80 KTS", on: true)));

        var first = h.Pushes[^2].Where(kv => kv.Key.EndsWith(".aidx", StringComparison.Ordinal)).Select(kv => kv.Value);
        var second = h.Pushes[^1].Where(kv => kv.Key.EndsWith(".aidx", StringComparison.Ordinal)).Select(kv => kv.Value);
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public void A_read_that_could_not_be_made_shows_its_reason_as_a_status()
    {
        using var h = new BridgeHarness { ModelRead = () => new Bbs757EfbModelRead(null, "The BlueBird tablet could not be read.") };

        var model = h.Bridge.ReadNowAsync(CancellationToken.None, asked: false).GetAwaiter().GetResult();

        Assert.Null(model);
        Assert.Equal("The BlueBird tablet could not be read.", h.Pushes[^1]["items.0.text"]);
        Assert.Equal("alert", h.Pushes[^1]["items.0.kind"]);
    }

    [Fact]
    public async Task The_AIRAC_page_offers_the_sign_in_while_signed_out_and_shows_its_code_while_it_is_spoken()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        var push = h.Pushes[^1];
        int signIn = IndexOf(push, Bbs757EfbExtras.SignInText);
        Assert.Equal("x:navigraph", push[$"items.{signIn}.key"]);
        Assert.Equal("button", push[$"items.{signIn}.kind"]);

        List<string>? whileSpoken = null;
        h.SignInFlow = _ =>
        {
            h.Bridge.ShowSignInLine("Navigraph code: ABCD-1234");
            whileSpoken = Texts(h.Pushes[^1]);
            return Task.FromResult("Navigraph code: ABCD-1234");
        };
        await h.Bridge.HandleAsync("click_display_element", new Dictionary<string, string> { ["index"] = push[$"items.{signIn}.aidx"] }, CancellationToken.None);

        Assert.Contains("Navigraph code: ABCD-1234", whileSpoken!);
        var after = h.Pushes[^1];
        int code = IndexOf(after, "Navigraph code: ABCD-1234");
        Assert.Equal("x:navigraph-code", after[$"items.{code}.key"]);
        Assert.Equal("", after[$"items.{code}.kind"]);
    }

    [Fact]
    public async Task The_airac_page_from_memory_offers_the_sign_in()
    {
        // The real tree of Settings > AIRAC data, read by the model reader: its page name is "Settings, AIRAC data". The tree predates
        // plan 21 (not captured again: a sign-in code now shows there), so its entry is marked as a live read marks it.
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithSelected(Bbs757EfbTreeJson.Load("55-settings-airac-data"), "btn_airac_data"))!;
        Assert.Equal("Settings, AIRAC data", model.PageName);
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(model);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        var push = h.Pushes[^1];
        int signIn = IndexOf(push, Bbs757EfbExtras.SignInText);
        Assert.Equal("x:navigraph", push[$"items.{signIn}.key"]);
    }

    [Fact]
    public async Task A_press_queued_when_the_window_hid_never_runs()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        int signIns = 0;
        h.SignInFlow = _ => { signIns++; return Task.FromResult("Navigraph code: ABCD-1234"); };
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        var push = h.Pushes[^1];
        var press = new Dictionary<string, string> { ["index"] = push[$"items.{IndexOf(push, Bbs757EfbExtras.SignInText)}.aidx"] };

        h.Bridge.EnqueueCommand("click_display_element", press);
        h.Bridge.SetActive(false);
        h.Bridge.SetActive(true);

        Assert.False(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));
        Assert.Equal(0, signIns);

        h.Bridge.EnqueueCommand("click_display_element", press);
        Assert.True(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));
        Assert.Equal(1, signIns);
    }

    // ---- the window's sets settle (the final review's I3) ------------------------------------------------------------------

    private static Bbs757EfbCommandQueue.Command Set(string index, string value, long at, int showing = 0) =>
        new("set_element_value", new Dictionary<string, string> { ["index"] = index, ["value"] = value }, showing, at);

    private static Bbs757EfbCommandQueue.Command Click(string index, long at, int showing = 0) =>
        new("click_display_element", new Dictionary<string, string> { ["index"] = index }, showing, at);

    [Fact]
    public void Five_sets_for_one_element_within_300_ms_make_one_the_last()
    {
        // In WebView2 a closed choice list sends a set on every arrow: the pilot arrows from RW07L down to RW26, five sets 50 ms apart. Only
        // the last is handed out, 300 ms after it was sent (as the 757's panel combos act), so the tablet chooses RW26 alone.
        var queue = new Bbs757EfbCommandQueue();
        string[] runways = { "RW07R", "RW08", "RW25L", "RW25R", "RW26" };
        for (int i = 0; i < runways.Length; i++) queue.Add(Set("12", runways[i], at: 50 * i));

        Assert.Null(queue.Next(200, 0, out int wait));
        Assert.Equal(300, wait);
        Assert.Null(queue.Next(499, 0, out wait));
        Assert.Equal(1, wait);
        var chosen = queue.Next(500, 0, out _);
        Assert.Equal("RW26", chosen!.Payload!["value"]);
        Assert.Null(queue.Next(10_000, 0, out wait));
        Assert.Equal(0, wait);
    }

    [Fact]
    public void Sets_settle_per_element_and_keep_the_window_order()
    {
        // A set for another element is its own; a click is never held but behind a set before it, and is a barrier: a set before it is
        // never replaced by one after it (the re-review's N1), so each settles in its own place.
        var queue = new Bbs757EfbCommandQueue();
        queue.Add(Set("12", "RW07R", at: 0));
        queue.Add(Click("40", at: 50));
        queue.Add(Set("12", "RW08", at: 100));
        queue.Add(Set("13", "15", at: 120));

        Assert.Null(queue.Next(100, 0, out int wait));   // RW07R settles: the click behind it waits too
        Assert.Equal(200, wait);
        Assert.Equal("RW07R", queue.Next(300, 0, out _)!.Payload!["value"]);
        Assert.Equal("click_display_element", queue.Next(300, 0, out _)!.Name);
        Assert.Null(queue.Next(300, 0, out wait));
        Assert.Equal(100, wait);
        Assert.Equal("RW08", queue.Next(400, 0, out _)!.Payload!["value"]);
        Assert.Null(queue.Next(400, 0, out wait));
        Assert.Equal(20, wait);
        Assert.Equal("15", queue.Next(420, 0, out _)!.Payload!["value"]);

        // Only another kind of command is a barrier: a set for another element between two of one element's leaves them neighbors, and
        // the newer of the two takes the older one's place.
        queue.Add(Set("12", "RW25L", at: 500));
        queue.Add(Set("13", "20", at: 510));
        queue.Add(Set("12", "RW25R", at: 520));
        Assert.Equal("20", queue.Next(900, 0, out _)!.Payload!["value"]);
        Assert.Equal("RW25R", queue.Next(900, 0, out _)!.Payload!["value"]);
        Assert.Equal(0, queue.Count);

        // A set that settled while the loop was busy (a press running) is handed out at once; two that came meanwhile, only the last.
        queue.Add(Set("12", "RW25L", at: 1000));
        queue.Add(Set("12", "RW25R", at: 1100));
        Assert.Equal("RW25R", queue.Next(5000, 0, out _)!.Payload!["value"]);
        Assert.Equal(0, queue.Count);

        // A command of an earlier showing of the window is dropped.
        queue.Add(Set("12", "RW26", at: 6000, showing: 0));
        Assert.Null(queue.Next(7000, 1, out wait));
        Assert.Equal((0, 0), (wait, queue.Count));
    }

    [Fact]
    public void A_click_between_two_sets_of_one_element_keeps_both_in_order()
    {
        // The re-review's N1: while a press runs, the pilot picks Runway RW07R, presses Calculate, then picks RW08. Merged across the click,
        // RW07R would be dropped and Calculate run on the runway the tablet still showed. A click is a barrier: the three run in the order
        // the window sent them, Calculate on RW07R.
        var queue = new Bbs757EfbCommandQueue();
        queue.Add(Set("12", "RW07R", at: 0));
        queue.Add(Click("40", at: 50));
        queue.Add(Set("12", "RW08", at: 100));

        var ran = new List<string>();
        while (queue.Next(5000, 0, out _) is { } command)
            ran.Add(command.Name == "set_element_value" ? command.Payload!["value"] : "click " + command.Payload!["index"]);

        Assert.Equal(new[] { "RW07R", "click 40", "RW08" }, ran);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Arrowing_through_a_choice_list_chooses_only_the_entry_it_stops_on()
    {
        // Through the bridge, on PERF calc as captured (25b): five sets 50 ms apart on the Runway drop-down make one press, 300 ms after
        // the last. The harness has no tablet to tap: the press reads the tablet once and its face's tap ends "cannot be reached".
        var page = PerfCalc();
        int reads = 0;
        using var h = new BridgeHarness { ModelRead = () => { reads++; return new Bbs757EfbModelRead(page, null); } };
        h.Bridge.TakeRead(page);
        var push = h.Pushes[^1];
        string runway = push[$"items.{IndexOf(push, "Runway")}.aidx"];
        string[] entries = { "RW07R", "RW08", "RW25L", "RW25R", "RW26" };
        for (int i = 0; i < entries.Length; i++)
        {
            h.Now = 50 * i;
            h.Bridge.EnqueueCommand("set_element_value", new Dictionary<string, string> { ["index"] = runway, ["value"] = entries[i] });
        }

        Assert.True(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));

        Assert.Equal(1, reads);    // one press: one fresh read
        Assert.Equal(500, h.Now);  // 300 ms after the last set
        Assert.Equal(Bbs757EfbPress.UnreachableText, h.Pushes[^1]["items.0.text"]);
        Assert.False(await h.Bridge.HandleNextCommandAsync(CancellationToken.None));

        // The one made is the last: arrowing back up to RW07L, the entry shown, ends in no tap at all (four earlier entries would each have
        // tried one and said "cannot be reached").
        using var back = new BridgeHarness { ModelRead = () => { reads++; return new Bbs757EfbModelRead(page, null); } };
        await back.Bridge.ReadNowAsync(CancellationToken.None, asked: false);   // the window's first read: connected, the page pushed
        reads = 0;
        int pushes = back.Pushes.Count;
        string again = back.Pushes[^1][$"items.{IndexOf(back.Pushes[^1], "Runway")}.aidx"];
        foreach (string entry in new[] { "RW25R", "RW25L", "RW08", "RW07R", "RW07L" })
        {
            back.Now += 50;
            back.Bridge.EnqueueCommand("set_element_value", new Dictionary<string, string> { ["index"] = again, ["value"] = entry });
        }
        Assert.True(await back.Bridge.HandleNextCommandAsync(CancellationToken.None));
        Assert.Equal(1, reads);
        Assert.Equal(pushes, back.Pushes.Count);   // nothing tapped, nothing said
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task No_sign_in_is_offered_signed_in_or_unread(bool? signedIn)
    {
        using var h = new BridgeHarness { SignedInAnswer = signedIn };
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        Assert.Equal(1, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }

    [Fact]
    public async Task The_sign_in_is_read_only_on_the_AIRAC_page()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(Ground(FuelTruck("off")));
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);

        Assert.Equal(0, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }

    [Fact]
    public async Task A_read_asked_for_on_a_still_AIRAC_page_reads_the_sign_in_again()
    {
        using var h = new BridgeHarness { SignedInAnswer = false };
        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        Assert.Equal(1, h.SignedInReads);

        h.Bridge.TakeRead(AiracPage());
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        Assert.Equal(1, h.SignedInReads);

        h.SignedInAnswer = true;
        h.Bridge.TakeRead(AiracPage(), asked: true);
        await h.Bridge.RefreshSignInAsync(CancellationToken.None);
        Assert.Equal(2, h.SignedInReads);
        Assert.DoesNotContain(Bbs757EfbExtras.SignInText, Texts(h.Pushes[^1]));
    }
}
