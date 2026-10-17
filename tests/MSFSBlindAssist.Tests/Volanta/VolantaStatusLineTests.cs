using MSFSBlindAssist.Forms.Volanta;
using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// VOL-2: the line at the top of each Volanta list says why it is standing still, for every reason the web map can
// be in, and is absent while the list updates; the status boxes give the same reason.
public class VolantaStatusLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static string? Line(VolantaLinkSnapshot snap) => VolantaLiveMapForm.NotUpdatingText(snap, Now);

    private static VolantaLinkSnapshot Connected(double? flightsAgeSeconds = null, double? connectedSecondsAgo = 600) => new()
    {
        Status = VolantaLinkStatus.Connected,
        ConnectedAt = connectedSecondsAgo is double c ? Now.AddSeconds(-c) : null,
        VolantaFlightsReceived = flightsAgeSeconds is double s ? Now.AddSeconds(-s) : null,
    };

    [Fact]
    public void Connected_and_updating_has_no_line()
        => Assert.Null(Line(Connected(5)));

    [Fact]
    public void Nothing_at_all_for_thirty_seconds_since_the_page_came_up_says_so()
    {
        string? line = Line(Connected(connectedSecondsAgo: 31));
        Assert.Contains("sent nothing since it opened", line);
        Assert.Null(Line(Connected(connectedSecondsAgo: 10)));   // still waiting for the first file
        Assert.Null(Line(Connected(connectedSecondsAgo: null)));
    }

    [Fact]
    public void Files_from_before_a_pause_or_reload_do_not_count_as_sent_since()
    {
        // Live again after a pause (or reloaded): the snapshot still holds the earlier files, but nothing came since.
        var resumed = Connected(flightsAgeSeconds: 300, connectedSecondsAgo: 40) with
        {
            NetworkFlightsReceived = Now.AddSeconds(-310),
            ControllersReceived = Now.AddSeconds(-320),
        };
        Assert.Contains("sent nothing", Line(resumed));
        Assert.Equal(VolantaLiveMapForm.Standstill.Silent, VolantaLiveMapForm.StandstillOf(resumed, Now));
    }

    [Fact]
    public void Just_after_a_pause_old_files_are_not_called_stale()
        => Assert.Null(Line(Connected(flightsAgeSeconds: 300, connectedSecondsAgo: 10)));

    [Fact]
    public void Any_file_received_since_the_page_came_up_means_it_is_not_silent()
        => Assert.Null(Line(Connected() with { ControllersReceived = Now.AddSeconds(-5) }));

    [Fact]
    public void No_new_file_for_thirty_seconds_is_stale()
    {
        Assert.Contains("not sent anything new", Line(Connected(31)));
        Assert.Null(Line(Connected(29)));
    }

    [Theory]
    [InlineData(VolantaLinkStatus.WaitingForPage, "loading")]
    [InlineData(VolantaLinkStatus.NoAccess, "could not start")]
    public void Every_other_link_state_has_its_reason(VolantaLinkStatus status, string expected)
        => Assert.Contains(expected, Line(new VolantaLinkSnapshot { Status = status }));

    [Fact]
    public void A_stopped_link_has_no_line()
        => Assert.Null(Line(new VolantaLinkSnapshot { Status = VolantaLinkStatus.Stopped }));

    [Theory]
    [InlineData(VolantaLinkStatus.NoAccess, "could not start")]
    [InlineData(VolantaLinkStatus.WaitingForPage, "loading")]
    public void The_status_boxes_give_the_list_line_s_reason_with_traffic_still_listed(VolantaLinkStatus status, string expected)
    {
        // Traffic received earlier stays in the snapshot: one reason feeds both, so they cannot drift apart.
        var snap = new VolantaLinkSnapshot { Status = status, VolantaFlightsReceived = Now.AddMinutes(-5) };
        Assert.Contains(expected, VolantaLiveMapForm.StatusWarning(snap, Now));
        Assert.Contains(expected, Line(snap));
    }

    [Fact]
    public void The_status_boxes_say_when_nothing_came_since_the_page_came_up()
        => Assert.Contains("sent nothing", VolantaLiveMapForm.StatusWarning(Connected(flightsAgeSeconds: 300, connectedSecondsAgo: 40), Now));

    [Fact]
    public void An_updating_link_has_no_status_warning()
        => Assert.Equal("", VolantaLiveMapForm.StatusWarning(Connected(5), Now));
}
