using System.Globalization;
using MSFSBlindAssist.Forms.Volanta;
using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Volanta's public web map in a hidden browser feeding the link: pins what blocks the page's trackers, what counts
// as the cached second copy of a file, the download summary (under de-DE), and how the link takes what the web map
// hands in (the pause, the log out, the active flight).
public sealed class VolantaWebMapTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    public VolantaWebMapTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");
    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    [Theory]
    [InlineData("https://connect.facebook.net/en_US/fbevents.js", true)]
    [InlineData("https://www.facebook.com/tr/?id=1&ev=PageView", true)]
    [InlineData("https://ad.doubleclick.net/ccm/s/collect", true)]
    [InlineData("https://www.google.com/ccm/collect?en=page_view", true)]
    [InlineData("https://www.google.de/ads/ga-audiences?v=1", true)]
    [InlineData("https://www.googleadservices.com/pagead/conversion/1/", true)]
    [InlineData("https://region1.analytics.google.com/g/collect", true)]
    [InlineData("https://static.klaviyo.com/onsite/js/klaviyo.js", true)]
    [InlineData("https://alb.reddit.com/rp?id=x", true)]
    [InlineData("https://us.i.posthog.com/flags/", true)]
    [InlineData("https://o1334761.ingest.sentry.io/api/6601689/envelope/", true)]
    [InlineData("https://events.mapbox.com/events/v2", true)]
    // What the map and a login need stays.
    [InlineData("https://fly.volanta.app/map", false)]
    [InlineData("https://api.volanta.app/api/v1/Flights/Feeds", false)]
    [InlineData("https://webassets.volanta.app/volanta-flight-positions-slim.json", false)]
    [InlineData("https://api.mapbox.com/styles/v1/x", false)]
    [InlineData("https://www.google.com/recaptcha/api.js", false)]
    [InlineData("https://accounts.google.com/o/oauth2/auth", false)]
    [InlineData("https://www.facebook.com/dialog/oauth", false)]
    public void The_page_s_trackers_are_blocked_and_nothing_the_map_or_a_login_needs(string url, bool blocked)
        => Assert.Equal(blocked, VolantaWebMap.IsTracker(new Uri(url)));

    [Fact]
    public void The_cached_second_copy_of_a_file_is_recognized()
    {
        string file = "{\"time\":1792176574,\"data\":[" + string.Join(",", Enumerable.Repeat("{\"id\":\"a\"}", 500)) + "]}";
        Assert.Equal(VolantaWebMap.BodyKey(file), VolantaWebMap.BodyKey(new string(file.AsSpan())));
        Assert.NotEqual(VolantaWebMap.BodyKey(file), VolantaWebMap.BodyKey(file.Replace("1792176574", "1792176580")));
        Assert.Equal("short", VolantaWebMap.BodyKey("short"));
    }

    [Fact]
    public void The_download_summary_counts_the_last_minute_in_invariant_numbers()
    {
        var now = new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
        var stats = new VolantaWebMapStats();
        stats.Add(now.AddSeconds(-90), VolantaWebMapStats.VolantaUsers, 5_000_000);   // out of the window
        for (int i = 0; i < 10; i++) stats.Add(now.AddSeconds(-6 * i), VolantaWebMapStats.VolantaUsers, 696_320);
        stats.Add(now.AddSeconds(-20), VolantaWebMapStats.NetworkPilots, 228_352);
        stats.Add(now.AddSeconds(-30), VolantaWebMapStats.Atc, 49_152);
        stats.Add(now.AddSeconds(-5), VolantaWebMapStats.Other, 104_858);
        Assert.Equal("In the last minute: 10 Volanta users files, 6.6 MB; 1 network pilots file, 0.2 MB; 1 ATC file, "
            + "0.0 MB; everything else 0.1 MB. In all 7.0 MB, about 420 MB an hour.", stats.Summary(now));
        Assert.Equal("Nothing downloaded in the last minute.", new VolantaWebMapStats().Summary(now));
    }

    [Fact]
    public void The_web_map_feeds_the_link()
    {
        using var link = new VolantaLink();
        link.Start();
        Assert.Equal(VolantaLinkStatus.WaitingForPage, link.Snapshot.Status);
        link.SetSourceStatus(VolantaLinkStatus.Connected);
        Assert.Equal(VolantaLinkStatus.Connected, link.Snapshot.Status);
        Assert.NotNull(link.Snapshot.ConnectedAt);

        link.IngestBody(VolantaLink.FeedKind.VolantaFlights,
            """{"time":1792176574,"data":[{"id":"f-1","callsign":"DAL1022","position":{"latitude":44.5,"longitude":-88.1}}]}""");
        Assert.Equal("DAL1022", link.Snapshot.VolantaFlights!.Items.Single().Callsign);
        Assert.NotNull(link.Snapshot.VolantaFlightsReceived);

        // The page's storage check: one per-user key.
        link.IngestStorageCheck("""{"u":["73e6c47e-9b8d-416b-a257-b8e9dd32b6f9"],"a":true}""");
        Assert.Equal("73e6c47e-9b8d-416b-a257-b8e9dd32b6f9", link.Snapshot.OwnUserId);
        // A second account's key appears: neither can be told for sure, so the id is dropped (VOL-4).
        link.IngestStorageCheck("""{"u":["73e6c47e-9b8d-416b-a257-b8e9dd32b6f9","0f1d2c3b-4a59-6877-8695-a4b3c2d1e0f9"],"a":true}""");
        Assert.Null(link.Snapshot.OwnUserId);

        link.Stop();
        Assert.Equal(VolantaLinkStatus.Stopped, link.Snapshot.Status);
        // Stopped, it takes nothing more.
        link.IngestBody(VolantaLink.FeedKind.Controllers, """{"time":1,"data":[]}""");
        Assert.Null(link.Snapshot.Controllers);
    }

    [Fact]
    public void A_resume_restarts_the_count_since_the_page_came_up()
    {
        // The web map polls only while the Volanta window is in the foreground: coming back after a pause, the last
        // file is as old as the pause, but nothing is overdue yet, so no list may call itself stale (VOL-12).
        using var link = new VolantaLink();
        link.Start();
        link.Resumed();   // the page is not up yet: nothing to restart
        Assert.Null(link.Snapshot.ConnectedAt);
        link.SetSourceStatus(VolantaLinkStatus.Connected);
        var connected = link.Snapshot.ConnectedAt!.Value;
        Thread.Sleep(20);
        link.Resumed();
        var resumed = link.Snapshot.ConnectedAt!.Value;
        Assert.True(resumed > connected);

        var afterPause = link.Snapshot with { VolantaFlightsReceived = resumed.AddMinutes(-10) };
        Assert.Null(VolantaLiveMapForm.NotUpdatingText(afterPause, resumed.AddSeconds(5)));
        // Nothing at all for 30 s after coming back is a real standstill: the page caught up within 3 s in the probe.
        Assert.Contains("sent nothing since it opened", VolantaLiveMapForm.NotUpdatingText(afterPause, resumed.AddSeconds(31)));
    }

    [Fact]
    public void Logging_out_forgets_the_friends_list_and_the_pilot_s_own_flight()
    {
        using var link = new VolantaLink();
        link.Start();
        link.SetSourceStatus(VolantaLinkStatus.Connected);
        link.IngestBody(VolantaLink.FeedKind.Friends, """[{"friendId":"a1","userName":"pilot_one","state":"Friend"}]""");
        link.IngestStorageCheck("""{"u":["73e6c47e-9b8d-416b-a257-b8e9dd32b6f9"],"a":true}""");
        link.IngestHubFrame("{\"type\":1,\"target\":\"flight-live-status-update\",\"arguments\":[{\"flightId\":\"f-9\",\"callsign\":\"DLH4\"}]}\u001e");
        Assert.Single(link.Snapshot.Friends!);
        Assert.NotNull(link.Snapshot.OwnUserId);
        Assert.Equal("f-9", link.Snapshot.OwnFlight!.FlightId);

        link.LoggedOut();
        var after = link.Snapshot;
        Assert.Null(after.Friends);
        Assert.Null(after.FriendsReceived);
        Assert.Null(after.OwnUserId);
        Assert.Null(after.OwnFlight);
        Assert.Equal(VolantaLinkStatus.Connected, after.Status);   // the map itself carries on, logged out
    }

    [Fact]
    public void The_active_flight_leaves_the_pilot_s_own_flight_out_of_the_first_list()
    {
        // Logged in on the web map there is no per-user key (nobody dismisses "what's new" in a hidden page), so the
        // pilot's flight was listed until the hub named it. Volanta's page asks for the active flight at once.
        using var link = new VolantaLink();
        link.Start();
        link.SetSourceStatus(VolantaLinkStatus.Connected);
        link.IngestBody(VolantaLink.FeedKind.ActiveFlight, """{"id":"own-1","callsign":"DLH4LP"}""");
        link.IngestBody(VolantaLink.FeedKind.VolantaFlights, """
            {"time":1792176574,"data":[
              {"id":"own-1","callsign":"DLH4LP","position":{"latitude":37.62,"longitude":-122.37}},
              {"id":"other-1","callsign":"UAL1","position":{"latitude":37.70,"longitude":-122.40}}]}
            """);
        var snap = link.Snapshot;
        Assert.Equal("own-1", snap.OwnFlight!.FlightId);
        Assert.Null(snap.OwnUserId);
        var me = new VolantaOwnPosition(37.62, -122.37, 20, 0, 0, true, DateTimeOffset.UtcNow);
        var traffic = VolantaTrafficCalculator.Compute(snap.VolantaFlights!.Items, me, DateTimeOffset.UtcNow,
            snap.OwnFlight?.FlightId, snap.OwnVolantaPosition, snap.OwnUserId);
        Assert.Equal(new[] { "other-1" }, traffic.Select(t => t.Flight.Id));
    }

    [Fact]
    public void A_link_not_started_takes_nothing()
    {
        using var link = new VolantaLink();
        link.IngestBody(VolantaLink.FeedKind.Controllers, """{"time":1,"data":[]}""");
        link.SetSourceStatus(VolantaLinkStatus.Connected);
        Assert.Null(link.Snapshot.Controllers);
        Assert.Equal(VolantaLinkStatus.Stopped, link.Snapshot.Status);
    }
}
