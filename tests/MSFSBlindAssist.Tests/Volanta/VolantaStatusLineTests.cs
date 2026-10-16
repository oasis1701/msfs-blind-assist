using MSFSBlindAssist.Forms.Volanta;
using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// VOL-2: the line at the top of each Volanta list says why it is standing still, for every reason the link can
// be in, and is absent while the list updates.
public class VolantaStatusLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static string? Line(VolantaLinkSnapshot snap, DateTimeOffset? connectedAt = null)
        => VolantaLiveMapForm.NotUpdatingText(snap, Now, connectedAt);

    private static VolantaLinkSnapshot Connected(double? flightsAgeSeconds = null) => new()
    {
        Status = VolantaLinkStatus.Connected,
        VolantaFlightsReceived = flightsAgeSeconds is double s ? Now.AddSeconds(-s) : null,
    };

    [Fact]
    public void Connected_and_updating_has_no_line()
    {
        Assert.Null(Line(Connected(5), connectedAt: Now.AddMinutes(-10)));
        Assert.Null(Line(Connected(5) with { VolantaPaused = false }));
    }

    [Fact]
    public void Minimized_comes_before_every_other_reason()
        => Assert.Contains("minimized", Line(Connected(45) with { VolantaPaused = true }, connectedAt: Now.AddMinutes(-10)));

    [Fact]
    public void Nothing_at_all_for_thirty_seconds_since_connecting_says_to_log_in()
    {
        // Volanta's login screen serves the same page: the link connects and nothing ever comes.
        string? line = Line(Connected(), connectedAt: Now.AddSeconds(-31));
        Assert.Contains("sent nothing since connecting", line);
        Assert.Contains("logged in", line);
        Assert.Null(Line(Connected(), connectedAt: Now.AddSeconds(-10)));   // still waiting for the first file
        Assert.Null(Line(Connected(), connectedAt: null));
    }

    [Fact]
    public void Any_file_received_means_Volanta_is_not_silent()
        => Assert.Null(Line(Connected() with { ControllersReceived = Now.AddSeconds(-5) }, connectedAt: Now.AddMinutes(-10)));

    [Fact]
    public void No_new_file_for_thirty_seconds_is_stale()
    {
        Assert.Contains("not sent anything new", Line(Connected(31), connectedAt: Now.AddMinutes(-10)));
        Assert.Null(Line(Connected(29), connectedAt: Now.AddMinutes(-10)));
    }

    [Theory]
    [InlineData(VolantaLinkStatus.WaitingForPage, "starting")]
    [InlineData(VolantaLinkStatus.PortTaken, "port 9223")]
    [InlineData(VolantaLinkStatus.NoAccess, "Connection tab")]
    public void Every_other_link_state_has_its_reason(VolantaLinkStatus status, string expected)
        => Assert.Contains(expected, Line(new VolantaLinkSnapshot { Status = status }, connectedAt: Now.AddMinutes(-10)));

    [Fact]
    public void A_stopped_link_has_no_line()
        => Assert.Null(Line(new VolantaLinkSnapshot { Status = VolantaLinkStatus.Stopped }));
}
