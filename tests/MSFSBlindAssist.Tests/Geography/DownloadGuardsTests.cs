using System.Globalization;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class DownloadGuardsTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Progress_speaks_each_quarter_once_and_never_100()
    {
        var gate = new DownloadProgressGate();
        var spoken = new List<int>();
        for (long done = 0; done <= 1000; done += 7)
            if (gate.Observe(done, 1000) is int m) spoken.Add(m);
        Assert.Equal(new[] { 25, 50, 75 }, spoken);
        Assert.Null(gate.Observe(1000, 1000));
    }

    [Fact]
    public void A_resumed_download_does_not_speak_milestones_it_already_passed()
    {
        var gate = new DownloadProgressGate();
        gate.Restart(600, 1000);
        Assert.Null(gate.Observe(700, 1000));
        Assert.Equal(75, gate.Observe(760, 1000));
    }

    [Fact]
    public void Unknown_total_never_speaks()
    {
        var gate = new DownloadProgressGate();
        Assert.Null(gate.Observe(500, 0));
    }

    [Fact]
    public void Stall_is_announced_once_after_20_seconds_then_aborted_at_60()
    {
        var d = new StallDetector(T0);
        Assert.Equal(StallVerdict.Flowing, d.Check(T0.AddSeconds(19)));
        Assert.Equal(StallVerdict.AnnounceStall, d.Check(T0.AddSeconds(20)));
        Assert.Equal(StallVerdict.Stalled, d.Check(T0.AddSeconds(40)));
        Assert.Equal(StallVerdict.Abort, d.Check(T0.AddSeconds(60)));
    }

    [Fact]
    public void Data_after_an_announced_stall_reports_resumed_once()
    {
        var d = new StallDetector(T0);
        d.Check(T0.AddSeconds(25));
        Assert.True(d.OnBytes(T0.AddSeconds(26)));
        Assert.False(d.OnBytes(T0.AddSeconds(27)));
        Assert.Equal(StallVerdict.Flowing, d.Check(T0.AddSeconds(30)));
    }

    [Fact]
    public void Retries_count_only_attempts_without_progress()
    {
        var budget = new RetryBudget();
        for (int i = 1; i <= RetryBudget.MaxNoProgressRetries; i++)
        {
            Assert.True(budget.AttemptFailed(0));
            Assert.Equal(i, budget.RetryNumber);
        }
        Assert.False(budget.AttemptFailed(0));

        var moving = new RetryBudget();
        for (int i = 0; i < 20; i++)
            Assert.True(moving.AttemptFailed(RetryBudget.ProgressBytes));
        Assert.Equal(1, moving.RetryNumber);
    }

    [Fact]
    public void Backoff_grows_and_caps_at_30_seconds()
    {
        var budget = new RetryBudget();
        var waits = new List<double>();
        for (int i = 0; i < 5; i++) { budget.AttemptFailed(0); waits.Add(budget.Backoff.TotalSeconds); }
        Assert.Equal(new double[] { 2, 4, 8, 16, 30 }, waits);
    }

    [Theory]
    [InlineData(200, 0, null, null, 1000L, ResumeOutcome.Fresh)]
    [InlineData(200, 400, null, null, 1000L, ResumeOutcome.RestartChanged)]   // If-Range failed: new file
    [InlineData(206, 400, 400L, 1000L, 1000L, ResumeOutcome.Append)]
    [InlineData(206, 400, 300L, 1000L, 1000L, ResumeOutcome.DiscardPartial)] // wrong start: never written
    [InlineData(206, 400, 400L, 1200L, 1000L, ResumeOutcome.DiscardPartial)] // different file size
    [InlineData(416, 1000, null, null, 1000L, ResumeOutcome.AlreadyComplete)]
    [InlineData(416, 400, null, 1000L, 1000L, ResumeOutcome.DiscardPartial)]
    [InlineData(206, 0, 0L, 1000L, null, ResumeOutcome.Fail)]                 // a 206 to a request with no Range: no loop
    [InlineData(416, 0, null, 1000L, null, ResumeOutcome.Fail)]
    [InlineData(503, 400, null, null, 1000L, ResumeOutcome.Fail)]
    public void Resume_accepts_only_the_exact_tail_of_the_same_file(
        int status, long part, long? from, long? total, long? known, ResumeOutcome expected)
    {
        Assert.Equal(expected, ResumeDecision.Decide(status, part, from, total, known));
    }

    [Fact]
    public void Disk_needs_cover_the_download_and_both_build_files()
    {
        long need = DiskSpaceRequirement.BeforeDownload(400L * 1024 * 1024);
        Assert.True(need > DiskSpaceRequirement.BeforeBuild() + 400L * 1024 * 1024);
        Assert.Equal(DiskSpaceRequirement.BeforeDownload(0), DiskSpaceRequirement.BeforeDownload(-5));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Spoken_sizes_use_a_decimal_point_under_any_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1.5 gigabytes", DiskSpaceRequirement.Spoken(1536L * 1024 * 1024));
            Assert.Equal("402 megabytes", DiskSpaceRequirement.Spoken(402L * 1024 * 1024));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
