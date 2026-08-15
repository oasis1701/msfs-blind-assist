using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the persistent-connection (jetway / GPU) timer callouts restored from the
/// pre-Remote-API transport: started, still running every 15 minutes, stopped —
/// baseline-first like every other MSFSBA monitor.
/// </summary>
public class GsxBillingTimerAnnouncerTests
{
    private static readonly DateTime T0 = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

    private static GsxBilling Billing(params (string friendly, double hours, bool running, double amount)[] timers)
    {
        string json = "{\"timers\":[" + string.Join(",", timers.Select(t =>
            $"{{\"subService\":\"{t.friendly.Split(' ')[0]}\",\"friendly\":\"{t.friendly}\",\"hours\":{t.hours.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"running\":{(t.running ? "true" : "false")},\"amount\":{t.amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"))
            + "]}";
        using var doc = JsonDocument.Parse(json);
        return GsxBilling.Parse(doc.RootElement.Clone());
    }

    [Fact]
    public void First_update_is_a_silent_baseline_even_with_a_running_timer()
    {
        var a = new GsxBillingTimerAnnouncer();
        Assert.Empty(a.Update(Billing(("Jetway operations", 0.1, true, 0)), T0));
    }

    [Fact]
    public void A_timer_that_starts_running_is_announced()
    {
        var a = new GsxBillingTimerAnnouncer();
        a.Update(Billing(("Jetway operations", 0, false, 0)), T0);
        var said = a.Update(Billing(("Jetway operations", 0, true, 0)), T0.AddSeconds(5));
        Assert.Equal("Jetway operations timer running.", Assert.Single(said));
    }

    [Fact]
    public void A_timer_that_appears_already_running_after_baseline_is_announced_as_started()
    {
        var a = new GsxBillingTimerAnnouncer();
        a.Update(GsxBilling.Empty, T0);
        var said = a.Update(Billing(("GPU operations", 0.02, true, 0)), T0.AddSeconds(5));
        Assert.Equal("GPU operations timer running.", Assert.Single(said));
    }

    [Fact]
    public void A_running_timer_is_reminded_every_interval_not_every_patch()
    {
        var a = new GsxBillingTimerAnnouncer();
        a.Update(Billing(("Jetway operations", 0.1, true, 0)), T0);      // baseline, silent
        Assert.Empty(a.Update(Billing(("Jetway operations", 0.2, true, 3.5)), T0.AddMinutes(6)));
        Assert.Empty(a.Update(Billing(("Jetway operations", 0.24, true, 4.2)), T0.AddMinutes(14)));

        var said = a.Update(Billing(("Jetway operations", 1.1, true, 116.97)),
            T0 + GsxBillingTimerAnnouncer.ReminderInterval);
        Assert.Equal("Jetway operations still running, 1 hour 6 minutes, amount 116.97.", Assert.Single(said));

        // The interval restarts from the LAST SPOKEN reminder.
        Assert.Empty(a.Update(Billing(("Jetway operations", 1.2, true, 120)),
            T0 + GsxBillingTimerAnnouncer.ReminderInterval + TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void A_timer_that_stops_is_announced_with_its_duration_and_amount()
    {
        var a = new GsxBillingTimerAnnouncer();
        a.Update(Billing(("Jetway operations", 0.5, true, 50)), T0);
        var said = a.Update(Billing(("Jetway operations", 0.75, false, 75.5)), T0.AddMinutes(20));
        Assert.Equal("Jetway operations timer stopped, 45 minutes, amount 75.50.", Assert.Single(said));
    }

    [Fact]
    public void A_zero_amount_is_not_spoken()
    {
        var a = new GsxBillingTimerAnnouncer();
        a.Update(Billing(("Jetway operations", 0.1, true, 0)), T0);
        var said = a.Update(Billing(("Jetway operations", 0.1, false, 0)), T0.AddMinutes(1));
        Assert.Equal("Jetway operations timer stopped, 6 minutes.", Assert.Single(said));
    }

    [Fact]
    public void Reset_re_baselines()
    {
        var a = new GsxBillingTimerAnnouncer();
        a.Update(Billing(("Jetway operations", 0.1, false, 0)), T0);
        a.Reset();
        Assert.Empty(a.Update(Billing(("Jetway operations", 0.1, true, 0)), T0.AddSeconds(5)));
    }

    [Fact]
    public void A_snapshot_with_no_billing_key_does_not_baseline_so_the_first_reading_is_silent()
    {
        // Observed live: a snapshot taken during a Couatl boot carries no billing key, and
        // the /billing patch with the already-running jetway arrives a moment later — that
        // first reading must be the baseline, not a "timer running." announcement.
        var a = new GsxBillingTimerAnnouncer();
        Assert.Empty(a.Update(GsxBilling.Empty, T0, billingPublished: false));
        Assert.Empty(a.Update(Billing(("Jetway operations", 0.1, true, 0)), T0.AddSeconds(1)));
        // …but a timer that starts AFTER that baseline announces.
        var said = a.Update(Billing(("Jetway operations", 0.1, true, 0), ("GPU operations", 0, true, 0)), T0.AddSeconds(30));
        Assert.Equal("GPU operations timer running.", Assert.Single(said));
    }

    [Fact]
    public void Two_timers_sharing_a_subservice_are_tracked_separately()
    {
        var a = new GsxBillingTimerAnnouncer();
        a.Update(Billing(("Jetway operations", 0.1, true, 0), ("Jetway operations", 0.1, true, 0)), T0);
        // Second jetway disconnects: exactly one "stopped", then nothing flip-flops.
        var said = a.Update(Billing(("Jetway operations", 0.2, true, 0), ("Jetway operations", 0.2, false, 12.5)), T0.AddMinutes(6));
        Assert.Equal("Jetway operations timer stopped, 12 minutes, amount 12.50.", Assert.Single(said));
        Assert.Empty(a.Update(Billing(("Jetway operations", 0.25, true, 0), ("Jetway operations", 0.2, false, 12.5)), T0.AddMinutes(9)));
    }

    [Theory]
    [InlineData(0.0, "under a minute")]
    [InlineData(0.01, "under a minute")]
    [InlineData(0.1, "6 minutes")]
    [InlineData(1.0, "1 hour")]
    [InlineData(1.0167, "1 hour 1 minute")]
    [InlineData(2.5, "2 hours 30 minutes")]
    public void Duration_reads_in_hours_and_minutes(double hours, string expected)
        => Assert.Equal(expected, GsxBillingTimerAnnouncer.DescribeDuration(hours));
}
