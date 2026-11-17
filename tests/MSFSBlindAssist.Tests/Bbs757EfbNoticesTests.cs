using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What the tablet puts over its page: BlueBird's notifications, read from BBOS's records into
/// <see cref="Bbs757EfbScreenModel.Notices"/> (plan 21, Task 6) and spoken once each, by id (<see cref="Bbs757EfbNotices.ToSpeak"/>), and
/// a question box, read into <see cref="Bbs757EfbScreenModel.Question"/> with its answers. The panel and question tests run against
/// plan 18's captures (Task 1, 2026-10-29, page models).
/// </summary>
public class Bbs757EfbNoticesTests
{
    private static readonly Bbs757EfbNotice[] None = Array.Empty<Bbs757EfbNotice>();

    private static Bbs757EfbNotice N(int id, string text, string? time) => new(id, text, time);

    private static void AssertNear(Point hit, int x, int y)
    {
        Assert.InRange(hit.X, x - 15, x + 15);
        Assert.InRange(hit.Y, y - 15, y + 15);
    }

    // ---------------------------------------------------------------- reading a notification

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_notification_reads_its_words_and_time(string culture)
    {
        // BBOS's records on the home page (captured 2026-11-07): each its words made readable as plan 18's were ("GPU connected."), its
        // title before them when it has one (the autobrake ones), its time; newest first, by id.
        var m = TestCulture.Run(culture, () =>
            Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("01-home"), Bbs757EfbTreeJson.LoadNotices("01-home"))!);

        Assert.Equal(49, m.Notices.Count);
        Assert.Equal(new (int, string, string?)[]
            {
                (51, "AFT cargo door closed.", "03:16Z"), (50, "AFT cargo door opened.", "03:15Z"), (49, "GPU connected.", "01:16Z"),
                (48, "Brake thermal efficiency: Left 96.2% | right 96.2%", "00:58Z"), (47, "Autobrake 1: TARGET 4.5 | ACTUAL 4.5 ft/s2", "00:58Z"),
            },
            m.Notices.Take(5).Select(n => (n.Id, n.Text, n.Time)));
        Assert.Equal(m.Notices.Select(n => n.Id).OrderByDescending(id => id), m.Notices.Select(n => n.Id));
        Assert.Equal((1, "GPU connected.", (string?)"19:34Z"), (m.Notices[^1].Id, m.Notices[^1].Text, m.Notices[^1].Time));
    }

    [Fact]
    public void A_record_with_no_words_is_no_notification()
    {
        var records = new[]
        {
            new Bbs757EfbNoticeRecord(1, "", "", "10:00Z", false),
            new Bbs757EfbNoticeRecord(2, "AUTOBRAKE 1", "", "", true),
            new Bbs757EfbNoticeRecord(3, "", "GPU CONNECTED.", "", true),
        };

        Assert.Equal(new (int, string, string?)[] { (3, "GPU connected.", null), (2, "Autobrake 1", null) },
            Bbs757EfbNotices.From(records).Select(n => (n.Id, n.Text, n.Time)));
    }

    // ---------------------------------------------------------------- speaking a notification

    [Fact]
    public void A_notification_is_spoken_once_per_appearance()
    {
        var first = new[] { N(16, "Chocks attached.", "12:06Z") };

        Assert.Equal("Chocks attached.", Bbs757EfbNotices.ToSpeak(None, first, 60_000));
        // Still listed at the next read: not again.
        Assert.Null(Bbs757EfbNotices.ToSpeak(first, first, 60_000));
        // The same words at the same time under a new id are a new appearance.
        Assert.Equal("Chocks attached.", Bbs757EfbNotices.ToSpeak(first, new[] { N(17, "Chocks attached.", "12:06Z") }.Concat(first).ToList(), 60_000));
    }

    [Fact]
    public void No_notification_is_spoken_within_4_s_of_the_pilots_press()
    {
        var now = new[] { N(1, "GPU connected.", "14:17Z") };

        Assert.Equal(4000, Bbs757EfbNotices.QuietAfterPressMs);
        Assert.Null(Bbs757EfbNotices.ToSpeak(None, now, 3_999));
        Assert.Equal("GPU connected.", Bbs757EfbNotices.ToSpeak(None, now, 4_000));
    }

    [Fact]
    public void New_notifications_are_said_together_as_they_came()
    {
        // Listed newest first; said oldest first, as they came.
        var before = new[] { N(1, "R3 door closed.", "12:44Z") };
        var now = new[] { N(3, "GPU disconnected.", "14:17Z"), N(2, "GPU connected.", "14:17Z"), N(1, "R3 door closed.", "12:44Z") };

        Assert.Equal("GPU connected. GPU disconnected.", Bbs757EfbNotices.ToSpeak(before, now, 10_000));
        // A notification that went away says nothing.
        Assert.Null(Bbs757EfbNotices.ToSpeak(now, None, 10_000));
        Assert.Null(Bbs757EfbNotices.ToSpeak(None, None, 10_000));
    }

    [Fact]
    public void A_list_whose_newest_id_went_back_is_taken_as_it_is()
    {
        // BlueBird's ids rise; a list whose newest is older than the last read's (the EFB started over, or the newest was deleted) holds
        // no new appearance: nothing is said, and the next id after it is.
        var before = new[] { N(55, "CHOCKS REMOVED.", "05:06Z"), N(54, "Chocks attached.", "05:06Z") };
        var restarted = new[] { N(2, "GPU connected.", "06:00Z"), N(1, "SIMBRIEF OFP received.", "06:00Z") };

        Assert.Null(Bbs757EfbNotices.ToSpeak(before, restarted, 60_000));
        Assert.Null(Bbs757EfbNotices.ToSpeak(before, before.Skip(1).ToList(), 60_000));
        Assert.Equal("Chocks attached.", Bbs757EfbNotices.ToSpeak(restarted, new[] { N(3, "Chocks attached.", "06:01Z") }.Concat(restarted).ToList(), 60_000));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void What_is_said_does_not_follow_the_pcs_culture(string culture)
    {
        var now = new[] { N(2, "R3 door opened.", "12:44Z"), N(1, "SIMBRIEF OFP received.", "14:21Z") };

        Assert.Equal("SIMBRIEF OFP received. R3 door opened.", TestCulture.Run(culture, () => Bbs757EfbNotices.ToSpeak(None, now, 4_000)));
    }

    // ---------------------------------------------------------------- the notification panel

    [Fact]
    public void BlueBirds_notification_is_a_notice_and_not_a_button()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification");

        var notice = Assert.Single(m.Notices);
        Assert.Equal("GPU connected.", notice.Text);
        Assert.Equal("14:17Z", notice.Time);
        Assert.DoesNotContain(m.Items, i => i.Text.StartsWith("Notifications:", StringComparison.Ordinal));
        Assert.DoesNotContain(m.Items, i => i.Text.Contains("14:17", StringComparison.Ordinal));
        // The page under it is still read: the GPU tile the notification is about.
        Assert.Equal("Aircraft, Ground services", m.PageName);
        Assert.Contains(m.Items, i => i.Kind == Bbs757EfbKind.Button && i.Text == "GPU connected" && i.Tint == Bbs757EfbTint.Green);
    }

    [Fact]
    public void The_open_list_gives_every_entry_top_first()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "19-notification-list");

        // The entry cut off at the list's bottom (its time only) is no entry.
        Assert.Equal(new (string, string?)[]
            {
                ("GPU disconnected.", "14:17Z"), ("GPU connected.", "14:17Z"), ("R3 door closed.", "12:44Z"),
                ("R3 door opened.", "12:44Z"), ("L3 door closed.", "12:44Z"),
            },
            m.Notices.Select(n => (n.Text, n.Time)));
        // Nothing of the list reaches the page: its title, Clear all, the times, the texts, the entries' boxes.
        Assert.DoesNotContain(m.Items, i => i.Box.Left >= 1040 && i.Box.Top < 515);
    }

    [Fact]
    public void A_control_the_list_covers_in_part_is_not_offered_under_a_cut_name()
    {
        // With the list open, REQ CARGO SVC and RIGHT AIR START show only "REQ CARGO" and "RIGHT AIF" left of it, which
        // snapped to FWD CARGO and RIGHT MID: a press would have tapped another control than the one named.
        var m = Bbs757EfbScreenTests.Load("bright", "19-notification-list");

        Assert.DoesNotContain(m.Items, i => i.Box.IntersectsWith(new Rectangle(1036, 32, 361, 487)));
        Assert.DoesNotContain(m.Items, i => i.Text is "FWD cargo" or "Right mid");
        // The controls clear of it stay.
        Assert.Contains(m.Items, i => i.Text == "Req fuel truck");
        Assert.Contains(m.Items, i => i.Text == "Left air start");
    }

    [Fact]
    public void A_notification_over_a_question_is_read_beside_it()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "23-units-question");

        var notice = Assert.Single(m.Notices);
        Assert.Equal("SIMBRIEF OFP received.", notice.Text);
        Assert.Equal("14:21Z", notice.Time);
        Assert.NotNull(m.Question);
    }

    [Theory]
    [InlineData("bright", "12-aircraft-fuel-boarding")]
    [InlineData("bright", "13-aircraft-ground-services")]
    [InlineData("bright", "20-fuel-start-boarding")]
    [InlineData("bright", "40-checklist")]
    [InlineData("dim", "50-settings-general")]
    public void A_page_with_nothing_over_it_has_no_notice_and_no_question(string set, string name)
    {
        var m = Bbs757EfbScreenTests.Load(set, name);

        Assert.Empty(m.Notices);
        Assert.Null(m.Question);
    }

    // ---------------------------------------------------------------- questions

    [Fact]
    public void The_defuel_question_is_read_with_both_its_answers()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");

        var q = Assert.IsType<Bbs757EfbQuestion>(m.Question);
        Assert.Equal("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?", q.Text);
        Assert.Equal(new[] { "Yes", "No" }, q.Buttons.Select(b => b.Text));
        Assert.All(q.Buttons, b => Assert.Equal(Bbs757EfbKind.Button, b.Kind));
        Assert.All(q.Buttons, b => Assert.True(b.Clickable));
        // YES is green; NO is dark blue on the question's dark box.
        AssertNear(q.Buttons[0].Hit, 424, 619);
        AssertNear(q.Buttons[1].Hit, 971, 619);
        // Its words and answers are the question's, not the page's.
        Assert.DoesNotContain(m.Items, i => i.Text.StartsWith("Yes", StringComparison.Ordinal) || i.Text == "No"
            || i.Text.Contains("defuel", StringComparison.OrdinalIgnoreCase) || i.Text.Contains("exceeds", StringComparison.Ordinal));
    }

    [Fact]
    public void A_message_box_is_a_question_with_its_ok()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "22-fuel-adjustment-complete");

        var q = Assert.IsType<Bbs757EfbQuestion>(m.Question);
        Assert.Equal("Fuel adjustment is complete!", q.Text);
        var ok = Assert.Single(q.Buttons);
        Assert.Equal("OK", ok.Text);
        Assert.True(ok.Clickable);
        AssertNear(ok.Hit, 823, 662);
        Assert.DoesNotContain(m.Items, i => i.Text == "OK" || i.Text.Contains("adjustment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_units_question_is_read_with_both_its_answers()
    {
        var m = Bbs757EfbScreenTests.Load("bright", "23-units-question");

        var q = Assert.IsType<Bbs757EfbQuestion>(m.Question);
        Assert.Equal("Mismatch between flight plan and aircraft weight units! The loaded flight plan uses LBS, but the aircraft "
            + "is currently set to KG. Do you want to switch the aircraft unit system?", q.Text);
        Assert.Equal(new[] { "Yes", "No" }, q.Buttons.Select(b => b.Text));
        AssertNear(q.Buttons[0].Hit, 393, 633);
        AssertNear(q.Buttons[1].Hit, 1001, 633);
        Assert.DoesNotContain(m.Items, i => i.Text.StartsWith("Yes", StringComparison.Ordinal) || i.Text == "No"
            || i.Text.Contains("Mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void A_questions_answers_keep_their_keys()
    {
        var defuel = Bbs757EfbScreenTests.Load("bright", "21-defuel-question").Question!;
        var units = Bbs757EfbScreenTests.Load("bright", "23-units-question").Question!;
        var done = Bbs757EfbScreenTests.Load("bright", "22-fuel-adjustment-complete").Question!;

        Assert.Equal(new[] { "q:YES", "q:NO" }, defuel.Buttons.Select(b => b.Key));
        Assert.Equal(defuel.Buttons.Select(b => b.Key), units.Buttons.Select(b => b.Key));
        Assert.Equal("q:OK", Assert.Single(done.Buttons).Key);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Questions_and_notices_do_not_follow_the_pcs_culture(string culture)
    {
        var defuel = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "21-defuel-question"));
        var done = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "22-fuel-adjustment-complete"));
        var gpu = TestCulture.Run(culture, () => Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification"));

        Assert.Equal("Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?", defuel.Question!.Text);
        Assert.Equal(new[] { "Yes", "No" }, defuel.Question.Buttons.Select(b => b.Text));
        Assert.Equal("Fuel adjustment is complete!", done.Question!.Text);
        Assert.Equal(("GPU connected.", (string?)"14:17Z"), (gpu.Notices[0].Text, gpu.Notices[0].Time));
    }
}
