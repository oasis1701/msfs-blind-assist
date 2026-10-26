// Characterization tests for FenixFailureText: every string the Fenix Failures window speaks.
// Run under de-DE (and fi-FI for the time separator): CI's en-US would hide a culture leak.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixFailureTextTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public FenixFailureTextTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private const string FanId = "F_PNEUMATIC_LAV_GAL_FAN";

    private static FenixFailureCatalog Fixture() => FenixFailureCatalog.Parse(FenixFailureCatalogTests.Load())!;

    private static FenixFailure Fan(FenixFailureCatalog c) => c.Find(FanId)!;

    private static FenixFailureCondition Cond(int? ias = null, int? above = null, int? below = null,
        string? ev = null, int? secs = null, string? time = null)
        => new(ias, above, below, ev, secs, time);

    private static FenixFailureCatalog InlineCatalog(string chaptersJson)
        => FenixFailureCatalog.Parse("{\"atas\":[" + chaptersJson + "]}")!;

    [Fact]
    public void Nodes()
    {
        var cat = Fixture();
        var fan = Fan(cat);
        var ch21 = cat.Chapters[0];
        Assert.Equal("Air Conditioning, ATA 21", FenixFailureText.ChapterNode(ch21));

        var failedFan = cat.With(fan with { Failed = true });
        Assert.Equal("Air Conditioning, ATA 21, 1 failed", FenixFailureText.ChapterNode(failedFan.Chapters[0]));

        var recirc = cat.Chapters[0].AllFailures.First(f => f.Id == "F_PNEUMATIC_CPC_1");
        var both = failedFan.With(recirc with { Failed = false, Condition = Cond(ias: 100) });
        Assert.Equal("Air Conditioning, ATA 21, 1 armed, 1 failed", FenixFailureText.ChapterNode(both.Chapters[0]));

        Assert.Equal("CPC", FenixFailureText.GroupNode(ch21.Groups[1]));
        var cpcFailed = cat.With(recirc with { Failed = true });
        Assert.Equal("CPC, 1 failed", FenixFailureText.GroupNode(cpcFailed.Chapters[0].Groups[1]));

        Assert.Equal("Lavatory/Galley fan", FenixFailureText.FailureNode(fan, Zone));
        Assert.Equal("Lavatory/Galley fan, failed", FenixFailureText.FailureNode(fan with { Failed = true }, Zone));
        Assert.Equal("Lavatory/Galley fan, armed: above IAS 250",
            FenixFailureText.FailureNode(fan with { Condition = Cond(ias: 250) }, Zone));
        Assert.Equal("Lavatory/Galley fan, armed",
            FenixFailureText.FailureNode(fan with { Condition = Cond() }, Zone));
        Assert.Equal("Lavatory/Galley fan, armed at 14:32:05",
            FenixFailureText.FailureNode(fan with { Condition = Cond(time: "2026-10-21T14:32:05Z") }, Zone));
    }

    [Theory]
    [InlineData(250, 10000, 20000, null, null, null, "above IAS 250, above altitude 10000 AMSL, below altitude 20000 AMSL")]
    [InlineData(null, null, null, "Takeoff", 30, null, "30 seconds after take-off")]
    [InlineData(null, null, null, "V1", null, null, "at V1")]
    [InlineData(null, null, null, "Landing", null, null, "at landing")]
    [InlineData(null, null, null, "Rotation", null, null, "at Rotation")]
    [InlineData(null, null, null, null, 90, null, "after 90 seconds")]
    [InlineData(null, null, null, null, 90, "2026-10-21T14:32:05Z", "armed at 14:32:05")]
    [InlineData(null, null, null, null, null, "30", "fails in 30 seconds")]
    [InlineData(null, null, null, null, null, "19.800000214", "fails in 20 seconds")]
    [InlineData(null, null, null, null, null, "19.8", "fails in 20 seconds")]
    [InlineData(null, null, null, null, null, "0.78", "fails in 1 second")]
    [InlineData(null, null, null, null, null, "1", "fails in 1 second")]
    [InlineData(null, null, null, null, 90, "30", "fails in 30 seconds")]
    [InlineData(null, null, null, "V1", null, "30", "at V1")]
    [InlineData(null, null, null, null, null, "garbage", "armed at garbage")]
    [InlineData(null, null, null, null, null, null, "")]
    public void Condition_uses_the_EFB_labels(int? ias, int? above, int? below, string? ev, int? secs, string? time, string expected)
        => Assert.Equal(expected, FenixFailureText.Condition(Cond(ias, above, below, ev, secs, time), Zone));

    [Fact]
    public void Countdown_node_and_saved_line_use_the_colon_form()
    {
        var fan = Fan(Fixture());
        Assert.Equal("Lavatory/Galley fan, armed: fails in 30 seconds",
            FenixFailureText.FailureNode(fan with { Condition = Cond(time: "30") }, Zone));
        Assert.Equal("Lavatory/Galley fan armed: fails in 30 seconds.",
            FenixFailureText.SavedLine(fan with { Condition = Cond(time: "30") }, Zone));
    }

    [Fact]
    public void Changes_ignore_a_ticking_countdown()
    {
        var cat = Fixture();
        var fan = Fan(cat);
        var a = cat.With(fan with { Condition = Cond(time: "30") });
        Assert.Null(FenixFailureText.Changes(a, cat.With(fan with { Condition = Cond(time: "20.8") }), null, Zone));
        Assert.Equal("Lavatory/Galley fan armed: above IAS 300, fails in 20 seconds.",
            FenixFailureText.Changes(cat.With(fan with { Condition = Cond(ias: 250, time: "30") }),
                cat.With(fan with { Condition = Cond(ias: 300, time: "19.8") }), null, Zone));
        Assert.Equal("Failure triggered: Lavatory/Galley fan.",
            FenixFailureText.Changes(a, cat.With(fan with { Failed = true }), null, Zone));
    }

    [Fact]
    public void Times_and_numbers_are_invariant()
    {
        CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
        Assert.Equal("armed at 14:32:05",
            FenixFailureText.Condition(Cond(time: "2026-10-21T14:32:05Z"), Zone));

        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        Assert.Equal("above altitude 10000 AMSL", FenixFailureText.Condition(Cond(above: 10000), Zone));
        Assert.Equal("1234 failures shown.", FenixFailureText.Shown(1234));
    }

    [Fact]
    public void Status_lines()
    {
        var cat = Fixture();
        Assert.Equal("57 failures in 1 chapter. 0 armed, 0 failed.", FenixFailureText.Summary(cat));
        Assert.Equal("60 failures in 3 chapters. 0 armed, 0 failed.", FenixFailureText.Summary(FenixFailureCatalogTests.Multi()));

        var one = InlineCatalog("{\"id\":1,\"title\":\"T\",\"shortTitle\":\"T\",\"groups\":[{\"groupName\":\"G\",\"failures\":[{\"id\":\"A\",\"title\":\"a\",\"failed\":false}]}]}");
        Assert.Equal("1 failure in 1 chapter. 0 armed, 0 failed.", FenixFailureText.Summary(one));

        Assert.Equal("Failures refreshed. 0 armed, 0 failed.", FenixFailureText.Refreshed(cat));
        Assert.Equal("3 failures match fan.", FenixFailureText.Matches(3, "fan"));
        Assert.Equal("1 failure matches cpc 1.", FenixFailureText.Matches(1, "cpc 1"));
        Assert.Equal("No failures match zzz.", FenixFailureText.Matches(0, "zzz"));
        Assert.Equal("2 failures shown.", FenixFailureText.Shown(2));
        Assert.Equal("1 failure shown.", FenixFailureText.Shown(1));
        Assert.Equal("No failures shown.", FenixFailureText.Shown(0));
    }

    [Fact]
    public void Summary_counts_only_chapters_with_a_failure()
    {
        var cat = InlineCatalog(
            "{\"id\":1,\"title\":\"T\",\"shortTitle\":\"T\",\"groups\":[{\"groupName\":\"G\",\"failures\":[{\"id\":\"A\",\"title\":\"a\",\"failed\":false}]}]}," +
            "{\"id\":2,\"title\":\"Empty\",\"shortTitle\":\"E\",\"groups\":[]}");
        Assert.Equal(2, cat.Chapters.Count);
        Assert.Equal("1 failure in 1 chapter. 0 armed, 0 failed.", FenixFailureText.Summary(cat));
    }

    [Fact]
    public void Automatic_failures_from_settings()
    {
        var rows = new List<FenixSetting>
        {
            new("auto_failure_mode", "Realism", "None", new[] { "None", "Minor", "All" }, "Enum", "UserFile", true),
            new("failure_rate", "Realism", "Realistic", new[] { "Realistic", "High" }, "Enum", "UserFile", true),
        };
        Assert.Equal("Automatic failure severity: None. Automatic failure rate: Realistic.", FenixFailureText.AutomaticFailures(rows));
        Assert.Equal("", FenixFailureText.AutomaticFailures(new List<FenixSetting>()));
    }

    [Fact]
    public void Saved_and_confirm_lines()
    {
        var fan = Fan(Fixture());
        Assert.Equal("Lavatory/Galley fan failed.", FenixFailureText.SavedLine(fan with { Failed = true }, Zone));
        Assert.Equal("Lavatory/Galley fan armed: above IAS 250.", FenixFailureText.SavedLine(fan with { Condition = Cond(ias: 250) }, Zone));
        Assert.Equal("Lavatory/Galley fan armed at 14:32:05.", FenixFailureText.SavedLine(fan with { Condition = Cond(time: "2026-10-21T14:32:05Z") }, Zone));
        Assert.Equal("Lavatory/Galley fan armed.", FenixFailureText.SavedLine(fan with { Condition = Cond() }, Zone));
        Assert.Equal("Lavatory/Galley fan cleared.", FenixFailureText.SavedLine(fan, Zone));

        Assert.Equal("Fail Lavatory/Galley fan now? This changes your flight.",
            FenixFailureText.Confirm(fan, FenixFailureAction.FailNow, null, Zone));
        Assert.Equal("Arm Lavatory/Galley fan: above IAS 250? This changes your flight.",
            FenixFailureText.Confirm(fan, FenixFailureAction.Arm, Cond(ias: 250), Zone));
        Assert.Equal("Clear Lavatory/Galley fan? This changes your flight.",
            FenixFailureText.Confirm(fan, FenixFailureAction.Clear, null, Zone));
        Assert.Equal("The Fenix did not confirm the change to Lavatory/Galley fan.",
            FenixFailureText.NotConfirmed("Lavatory/Galley fan"));
    }

    [Fact]
    public void Changes_speak_what_changed()
    {
        var cat = Fixture();
        var fan = Fan(cat);

        Assert.Equal("Lavatory/Galley fan failed.",
            FenixFailureText.Changes(cat, cat.With(fan with { Failed = true }), null, Zone));

        var armed = cat.With(fan with { Condition = Cond(ias: 250) });
        Assert.Equal("Failure triggered: Lavatory/Galley fan.",
            FenixFailureText.Changes(armed, cat.With(fan with { Condition = Cond(ias: 250), Failed = true }), null, Zone));

        var failed = cat.With(fan with { Failed = true });
        Assert.Equal("Lavatory/Galley fan cleared.", FenixFailureText.Changes(failed, cat, null, Zone));

        Assert.Null(FenixFailureText.Changes(cat, cat, null, Zone));
        Assert.Null(FenixFailureText.Changes(cat, failed, FanId, Zone));
    }

    [Fact]
    public void Changes_cap_at_three()
    {
        var cat = Fixture();
        var next = cat;
        foreach (FenixFailure f in cat.Chapters[0].AllFailures.Take(5))
            next = next.With(f with { Failed = true });

        Assert.Equal("Lavatory/Galley fan failed. CPC 1 failed. CPC 2 failed. And 2 more changes.",
            FenixFailureText.Changes(cat, next, null, Zone));
    }

    [Fact]
    public void Changes_with_four_say_one_more_change()
    {
        var cat = Fixture();
        var next = cat;
        foreach (FenixFailure f in cat.Chapters[0].AllFailures.Take(4))
            next = next.With(f with { Failed = true });

        Assert.Equal("Lavatory/Galley fan failed. CPC 1 failed. CPC 2 failed. And 1 more change.",
            FenixFailureText.Changes(cat, next, null, Zone));
    }

    [Fact]
    public void Changes_failed_in_both_reads_ignore_a_condition_change()
    {
        var cat = Fixture();
        var fan = Fan(cat);
        var before = cat.With(fan with { Condition = Cond(ias: 250), Failed = true });
        var after = cat.With(fan with { Failed = true });
        Assert.Null(FenixFailureText.Changes(before, after, null, Zone));
    }

    [Fact]
    public void Changes_armed_in_both_reads_speak_a_new_condition()
    {
        var cat = Fixture();
        var fan = Fan(cat);
        var a = cat.With(fan with { Condition = Cond(ias: 250) });
        var b = cat.With(fan with { Condition = Cond(ias: 300) });
        Assert.Equal("Lavatory/Galley fan armed: above IAS 300.", FenixFailureText.Changes(a, b, null, Zone));
    }

    [Fact]
    public void Changes_ignore_ids_in_only_one_read()
    {
        var other = InlineCatalog("{\"id\":1,\"title\":\"T\",\"shortTitle\":\"T\",\"groups\":[{\"groupName\":\"G\",\"failures\":[{\"id\":\"F_NEW_ONE\",\"title\":\"New\",\"failed\":true}]}]}");
        Assert.Null(FenixFailureText.Changes(Fixture(), other, null, Zone));
    }
}
