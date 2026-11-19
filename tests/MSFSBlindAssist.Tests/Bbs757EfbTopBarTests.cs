using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The tablet's top bar. From memory (plan 21, Task 6): the status bar is the top bar app's visible labels, the time line as BlueBird
/// writes it, then the departure countdown and the boarding box (measured and decoded 2026-11-07). Plan 18's captures (Task 1,
/// 2026-10-29, page models) carry the departure countdown, the boarding control and the notification control as plan 18's reader made
/// them; the memory reader does not feed the notification control, so its tests pin <see cref="Bbs757EfbTopBar"/>'s keys and words on
/// those models. The keys are what keeps the screen reader's focus on an item while its words change, so they are pinned as strings.
/// </summary>
public class Bbs757EfbTopBarTests
{
    private static Bbs757EfbItem Top(Bbs757EfbScreenModel m, string key) =>
        m.TopBar.FirstOrDefault(i => i.Key == key)
        ?? throw new Xunit.Sdk.XunitException($"No top bar item '{key}' on {m.PageName}: "
            + string.Join(" | ", m.TopBar.Select(i => i.Key + " = " + i.Text)));

    // ---- the status bar from memory (plan 21, Task 6) ----

    [Theory]
    [InlineData("DEP 00:28", "DEP 00:28", "top:dep")]
    [InlineData("DEP -00:46", "DEP -00:46", "top:dep")]
    [InlineData("ETA 1234Z", "ETA 1234Z", "top:flight_time")]
    public void The_status_bar_reads_the_departure_countdown_after_the_time_line(string read, string said, string key)
    {
        // The countdown is the top bar app's own string (measured 2026-11-07), no label: read after the time line.
        var m = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithFlightTime(Bbs757EfbTreeJson.Load("13-aircraft-ground-services"), read))!;
        Assert.Equal(new[] { "top:time_label", key }, m.TopBar.Select(i => i.Key));
        Assert.Equal(said, m.TopBar[1].Text);
        Assert.False(m.TopBar[1].Clickable);
        // Empty, or none: nothing is added.
        Assert.Single(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithFlightTime(Bbs757EfbTreeJson.Load("13-aircraft-ground-services"), ""))!.TopBar);
        Assert.Single(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("13-aircraft-ground-services"))!.TopBar);
    }

    /// <summary>The boarding box as read live on 2026-11-07 with automatic boarding off and a fresh OFP: [1144,4 176x24], idle.</summary>
    private static Bbs757EfbMemory.BoardingBox Box(int state = 0, bool deboarding = false, bool flown = false, long remaining = 0) =>
        new(1144, 4, 176, 24, state, deboarding, flown, remaining);

    private static Bbs757EfbScreenModel WithBox(Bbs757EfbMemory.BoardingBox? box, string flightTime = "") =>
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithBoarding(
            Bbs757EfbTreeJson.WithFlightTime(Bbs757EfbTreeJson.Load("13-aircraft-ground-services"), flightTime), box))!;

    [Fact]
    public void The_status_bar_offers_start_boarding_as_a_button_after_the_countdown()
    {
        // Automatic boarding off and a fresh OFP: BlueBird draws START BOARDING in its top bar (no control: the top bar app's own
        // rectangle and BBOS's loading state, decoded 2026-11-07). The window offers it where BlueBird draws it, after the countdown.
        var m = WithBox(Box(), "DEP 00:42");
        Assert.Equal(new[] { "top:time_label", "top:dep", "top:boarding" }, m.TopBar.Select(i => i.Key));
        var start = m.TopBar[2];
        Assert.Equal(Bbs757EfbKind.Button, start.Kind);
        Assert.Equal("Start boarding", start.Text);
        Assert.True(start.Clickable);
        Assert.Equal(new Rectangle(1144, 4, 176, 24), start.Box);
        Assert.Equal(new Point(1232, 16), start.Hit);
        // Pressing it is BlueBird's START BOARDING: the load watch starts as when the pilot taps it on the tablet.
        Assert.Equal(Bbs757EfbHook.StartBoarding, Bbs757EfbExtras.HookFor(m.PageName, start));
    }

    [Theory]
    [InlineData(1, false, false, 960L, "Boarding… 16 min", false)]
    [InlineData(2, false, false, 961L, "Boarding… 17 min", false)]
    [InlineData(2, false, false, 0L, "Boarding...", false)]
    [InlineData(3, false, false, 600L, "Boarding paused", false)]
    [InlineData(4, false, true, 0L, "Start deboarding", true)]
    [InlineData(0, false, true, 0L, "Start deboarding", true)]
    [InlineData(2, true, true, 300L, "Deboarding...", false)]
    [InlineData(3, true, true, 300L, "Deboarding paused", false)]
    public void The_boarding_box_reads_as_BlueBird_draws_it(int state, bool deboarding, bool flown, long remaining, string said, bool button)
    {
        // A running or paused loading is a status (text); an offer to start is a button. The minutes are the largest remaining time
        // rounded up, as BlueBird's draw rounds them.
        var item = Assert.Single(WithBox(Box(state, deboarding, flown, remaining)).TopBar, i => i.Key == Bbs757EfbTopBar.BoardingKey);
        Assert.Equal(said, item.Text);
        Assert.Equal(button, item.Clickable);
        Assert.Equal(button ? Bbs757EfbKind.Button : Bbs757EfbKind.Text, item.Kind);
        Assert.Equal(new Rectangle(1144, 4, 176, 24), item.Box);
    }

    [Fact]
    public void No_box_adds_nothing_to_the_status_bar()
    {
        // BlueBird zeros the rectangle on every frame it draws no box: the walk reads none, and the status bar has no boarding item.
        Assert.Equal(new[] { "top:time_label" }, WithBox(null).TopBar.Select(i => i.Key));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_boarding_minutes_read_the_same_under_a_comma_decimal_culture(string culture)
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            Assert.Equal("BOARDING... 1440 MIN", Bbs757EfbTopBar.BoardingWords(Box(2, remaining: 86_400)));
            Assert.Equal("Boarding… 16 min", Assert.Single(WithBox(Box(2, remaining: 960)).TopBar, i => i.Key == Bbs757EfbTopBar.BoardingKey).Text);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    private static Bbs757EfbMemory.Node Leaf(string kind, string name, string text, float x, float y, float w, float h) =>
        new(kind, name, text, x, y, w, h, 0, Array.Empty<Bbs757EfbMemory.Node>());

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_status_bar_reads_the_time_line(string culture)
    {
        // The top bar app (app_overlay, measured 2026-11-07) on every recaptured page: overlay_grid's time_label, BlueBird's words as
        // presented, a read-out. The empty keyboard_locked_label and the hidden etd_label are no line.
        var m = TestCulture.Run(culture, () => Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("13-aircraft-ground-services"))!);

        var time = Assert.Single(m.TopBar);
        Assert.Equal((Bbs757EfbKind.Text, "Sat, 7th Nov 2026 | 23:06L | 05:06Z | OAT 4 C", "top:time_label", false),
            (time.Kind, time.Text, time.Key, time.Clickable));
        Assert.Equal(new Rectangle(15, 0, 1382, 32), time.Box);
        // Its key stays as the minutes go by, so the window keeps the screen reader on it.
        var later = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25c-navigation-perf-calc-runway-open"))!;
        Assert.Equal("top:time_label", Assert.Single(later.TopBar).Key);
        Assert.NotEqual(time.Text, later.TopBar[0].Text);

        // The departure label when it shows (not seen live yet), after the time line, as written; a separator between them is none.
        var overlay = new Bbs757EfbMemory.App(1, Bbs757EfbMemory.TopBarClass, new[]
        {
            new Bbs757EfbMemory.Node("grid", "overlay_grid", "", 0, 0, 1397, 32, 0, new[]
            {
                Leaf("label", "time_label", "Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C", 15, 0, 1382, 32),
                Leaf("label", "etd_separator_label", "|", 333, 0, 24, 32),
                Leaf("label", "etd_label", "ETD 0315Z", 343, 0, 120, 32),
            }),
            Leaf("label", "keyboard_locked_label", "", 0, 0, 1397, 32),
        });
        var apps = Bbs757EfbTreeJson.Load("13-aircraft-ground-services").Where(a => a.Class != Bbs757EfbMemory.TopBarClass).Append(overlay).ToList();
        var bar = Bbs757EfbModelReader.Read(apps)!.TopBar;
        Assert.Equal(new[] { ("top:time_label", "Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C"), ("top:etd_label", "ETD 0315Z") },
            bar.Select(i => (i.Key, i.Text)));
    }

    [Fact]
    public void Keyboard_locked_reads_in_the_status_bar_while_it_shows()
    {
        // Under the fuel input dialog (11c, captured 2026-11-07) BlueBird writes KEYBOARD LOCKED into its top bar label: a sighted
        // pilot sees it, so it reads after the time line, as written.
        var m = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11c-aircraft-fuel-input-dialog"))!;

        Assert.Equal(new[] { "top:time_label", "top:keyboard_locked_label" }, m.TopBar.Select(i => i.Key));
        Assert.Equal("KEYBOARD LOCKED", m.TopBar[1].Text);
    }

    [Fact]
    public void A_page_captured_before_plan_21_has_no_status_bar()
    {
        // The trees captured before the layout knew the top bar's app (11, 25, 55, 55b) hold none of it.
        Assert.Empty(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("11-aircraft-fuel"))!.TopBar);
    }

    [Fact]
    public void During_boarding_the_bar_reads_the_countdown_the_minutes_left_and_the_count()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "12-aircraft-fuel-boarding");

        var departure = Top(m, "top:dep");
        Assert.Equal(Bbs757EfbKind.Text, departure.Kind);
        Assert.Equal("DEP -00:46", departure.Text);   // read "DEP-00:46"
        Assert.False(departure.Clickable);

        var boarding = Top(m, "top:boarding");
        Assert.Equal(Bbs757EfbKind.Text, boarding.Kind);
        Assert.Equal("Boarding… 10 min", boarding.Text);   // read "30 B0ARDIHG... 10 MIN": the icon, then a misread
        Assert.False(boarding.Clickable);

        var count = Top(m, "top:notifications");
        Assert.Equal(Bbs757EfbKind.Button, count.Kind);
        Assert.Equal("Notifications: 10", count.Text);
        Assert.True(count.Clickable);
        Assert.Equal(new Point(1373, 15), count.Hit);

        // Left to right, as the bar shows them.
        Assert.Equal(new[] { "top:dep", "top:boarding", "top:notifications" }, m.TopBar.Select(i => i.Key));
    }

    [Fact]
    public void Start_boarding_is_a_button_at_its_green_box()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding");

        var boarding = Top(m, "top:boarding");
        Assert.Equal(Bbs757EfbKind.Button, boarding.Kind);
        Assert.Equal("Start boarding", boarding.Text);   // read "3) START BOARDING": the icon's glyph is no word
        Assert.True(boarding.Clickable);
        // The green box spans x 1142-1321, y 2-30 on the capture; START BOARDING was tapped live at (1231,24).
        Assert.InRange(boarding.Box.Left, 1138, 1146);
        Assert.InRange(boarding.Box.Right, 1316, 1326);
        Assert.InRange(boarding.Hit.X, 1200, 1262);
        Assert.InRange(boarding.Hit.Y, 8, 26);
        Assert.Equal("DEP -03:19", Top(m, "top:dep").Text);
    }

    [Fact]
    public void Top_bar_keys_stay_when_the_words_change()
    {
        var boarding = Bbs757EfbScreenTests.Load("bright", "12-aircraft-fuel-boarding");
        var start = Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding");
        var open = Bbs757EfbScreenTests.Load("bright", "19-notification-list");

        // Text while boarding runs, a button before it starts: one key.
        Assert.NotEqual(Top(boarding, "top:boarding").Text, Top(start, "top:boarding").Text);
        Assert.NotEqual(Top(boarding, "top:boarding").Kind, Top(start, "top:boarding").Kind);
        Assert.NotEqual(Top(boarding, "top:dep").Text, Top(start, "top:dep").Text);
        // The count while the list is closed, Close notifications while it is open: one key, one place.
        Assert.NotEqual(Top(boarding, "top:notifications").Text, Top(open, "top:notifications").Text);
        Assert.Equal(Top(boarding, "top:notifications").Hit, Top(open, "top:notifications").Hit);

        Assert.Equal("top:dep", Bbs757EfbTopBar.DepartureKey);
        Assert.Equal("top:boarding", Bbs757EfbTopBar.BoardingKey);
        Assert.Equal("top:notifications", Bbs757EfbTopBar.NotificationsKey);
    }

    [Fact]
    public void An_open_notification_list_is_closed_at_the_arrow()
    {
        // The list is open: BlueBird's panel shows and the count pill does not.
        var n = Top(Bbs757EfbScreenTests.Load("bright", "19-notification-list"), "top:notifications");

        Assert.Equal(Bbs757EfbKind.Button, n.Kind);
        Assert.Equal("Close notifications", n.Text);
        Assert.True(n.Clickable);
        Assert.Equal(new Point(1373, 15), n.Hit);
    }

    [Theory]
    [InlineData("18-aircraft-ground-services-notification")]   // BlueBird's popup over the page, the count still showing
    [InlineData("20-fuel-start-boarding")]                     // a count with no figure
    [InlineData("40-checklist")]                               // no count at all
    public void A_closed_list_is_opened_at_the_arrow(string name)
    {
        var n = Top(Bbs757EfbScreenTests.Load("bright", name), "top:notifications");

        Assert.Equal(Bbs757EfbKind.Button, n.Kind);
        Assert.StartsWith("Notifications", n.Text, StringComparison.Ordinal);
        Assert.True(n.Clickable);
        Assert.Equal(new Point(1373, 15), n.Hit);
    }

    [Theory]
    [InlineData("DEP-00:46", "DEP -00:46")]
    [InlineData("DEP -03:21", "DEP -03:21")]
    [InlineData("DEP+00:05", "DEP +00:05")]
    [InlineData("DEP −00:46", "DEP -00:46")]   // a minus sign read as U+2212
    [InlineData("DEP 01:10", "DEP 01:10")]
    [InlineData("Thu, 29th Oct 2026 | 12:46L | 11:46Z | OAT 26 C", null)]
    [InlineData("10", null)]
    public void The_countdown_is_said_as_the_tablet_writes_it(string read, string? expected) =>
        Assert.Equal(expected, Bbs757EfbTopBar.DepartureText(read));

    [Theory]
    [InlineData("3) START BOARDING", true, "Start boarding")]
    [InlineData("» START BOARDING", true, "Start boarding")]
    [InlineData("START BOARDING", true, "Start boarding")]
    [InlineData("30 B0ARDIHG... 10 MIN", false, "Boarding… 10 min")]
    [InlineData("» BOARDING… 3 MIN", false, "Boarding… 3 min")]
    [InlineData("BOARDING... 12 MIN", false, "Boarding… 12 min")]
    // Words the box was never seen with are said as read, as text.
    [InlineData("» DEBOARDING... 5 MIN", false, "Deboarding... 5 min")]
    public void The_boarding_box_is_a_button_only_while_it_offers_to_start(string read, bool button, string text)
    {
        var said = Bbs757EfbTopBar.BoardingText(read);

        Assert.NotNull(said);
        Assert.Equal(button, said!.Value.IsButton);
        Assert.Equal(text, said.Value.Text);
    }

    [Fact]
    public void A_boarding_box_with_no_words_read_says_nothing() =>
        Assert.Null(Bbs757EfbTopBar.BoardingText("3)"));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_top_bar_texts_do_not_follow_the_pcs_culture(string culture)
    {
        var boarding = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "12-aircraft-fuel-boarding"));
        var open = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "19-notification-list"));
        var start = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding"));

        Assert.Equal(new[] { "DEP -00:46", "Boarding… 10 min", "Notifications: 10" }, boarding.TopBar.Select(i => i.Text));
        Assert.Equal("Close notifications", Top(open, "top:notifications").Text);
        Assert.Equal("Start boarding", Top(start, "top:boarding").Text);
        Assert.Equal("DEP -00:46", TestCulture.Run(culture, () => Bbs757EfbTopBar.DepartureText("DEP−00:46")));
    }
}
