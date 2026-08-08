// Per-event gating and wording for VATSIM announcements. The wording is carried over
// verbatim from the vPilot-to-TTS project, so these tests are characterization tests as
// much as unit tests — a pilot who used the standalone app must hear the same phrases.

using MSFSBlindAssist.Services.VPilot;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class VatsimAnnouncementFormatterTests
{
    private static VatsimAnnouncementOptions AllOn() => new()
    {
        AnnounceConnect = true,
        AnnounceDisconnect = true,
        AnnouncePrivateMessages = true,
        AnnounceRadioMessages = true,
        AnnounceSelcal = true,
    };

    [Fact]
    public void Connect_names_the_callsign()
        => Assert.Equal("Connected as BAW123",
            VatsimAnnouncementFormatter.Format("connected", "BAW123", "", AllOn()));

    [Fact]
    public void Disconnect_wording_is_unchanged_from_the_standalone_app()
        => Assert.Equal("Disconnected from network",
            VatsimAnnouncementFormatter.Format("disconnected", "", "", AllOn()));

    [Fact]
    public void Private_message_names_the_sender_then_the_text()
        => Assert.Equal("Private message from LON_CTR: Contact me on 133.60",
            VatsimAnnouncementFormatter.Format("private_message", "LON_CTR", "Contact me on 133.60", AllOn()));

    [Fact]
    public void Radio_message_is_sender_then_text_with_no_preamble()
        => Assert.Equal("EGLL_TWR: BAW123 line up and wait runway 27L",
            VatsimAnnouncementFormatter.Format("radio_message", "EGLL_TWR", "BAW123 line up and wait runway 27L", AllOn()));

    [Fact]
    public void Selcal_names_the_station()
        => Assert.Equal("SELCAL alert from NAT_FSS",
            VatsimAnnouncementFormatter.Format("selcal", "NAT_FSS", "", AllOn()));

    [Theory]
    [InlineData("connected", "BAW123", "")]
    [InlineData("disconnected", "", "")]
    [InlineData("private_message", "LON_CTR", "hi")]
    [InlineData("radio_message", "LON_CTR", "hi")]
    [InlineData("selcal", "NAT_FSS", "")]
    public void Every_event_type_is_silent_when_its_own_toggle_is_off(string type, string from, string message)
    {
        var off = new VatsimAnnouncementOptions
        {
            AnnounceConnect = false,
            AnnounceDisconnect = false,
            AnnouncePrivateMessages = false,
            AnnounceRadioMessages = false,
            AnnounceSelcal = false,
        };
        Assert.Null(VatsimAnnouncementFormatter.Format(type, from, message, off));
    }

    [Fact]
    public void One_toggle_off_does_not_silence_the_others()
    {
        var noRadio = AllOn() with { AnnounceRadioMessages = false };
        Assert.Null(VatsimAnnouncementFormatter.Format("radio_message", "X", "hi", noRadio));
        Assert.NotNull(VatsimAnnouncementFormatter.Format("private_message", "X", "hi", noRadio));
    }

    [Fact]
    public void Unknown_event_type_is_silent_rather_than_read_raw()
        => Assert.Null(VatsimAnnouncementFormatter.Format("aircraft_added", "BAW123", "", AllOn()));

    [Fact]
    public void Connect_without_a_callsign_still_says_something_useful()
        => Assert.Equal("Connected to the network",
            VatsimAnnouncementFormatter.Format("connected", "  ", "", AllOn()));

    [Fact]
    public void Private_message_with_no_text_still_reports_the_sender()
        => Assert.Equal("Private message from LON_CTR",
            VatsimAnnouncementFormatter.Format("private_message", "LON_CTR", "   ", AllOn()));

    [Fact]
    public void Private_message_with_neither_sender_nor_text_is_silent()
        => Assert.Null(VatsimAnnouncementFormatter.Format("private_message", "", "", AllOn()));

    [Fact]
    public void Radio_message_with_no_text_is_silent_because_there_is_nothing_to_hear()
        => Assert.Null(VatsimAnnouncementFormatter.Format("radio_message", "EGLL_TWR", "", AllOn()));

    [Fact]
    public void Selcal_without_a_station_still_alerts()
        => Assert.Equal("SELCAL alert",
            VatsimAnnouncementFormatter.Format("selcal", "", "", AllOn()));

    [Fact]
    public void Options_are_read_straight_off_UserSettings()
    {
        var settings = new UserSettings
        {
            VatsimAnnounceConnect = false,
            VatsimAnnounceDisconnect = true,
            VatsimAnnouncePrivateMessages = false,
            VatsimAnnounceRadioMessages = true,
            VatsimAnnounceSelcal = false,
        };
        var options = VatsimAnnouncementOptions.From(settings);
        Assert.False(options.AnnounceConnect);
        Assert.True(options.AnnounceDisconnect);
        Assert.False(options.AnnouncePrivateMessages);
        Assert.True(options.AnnounceRadioMessages);
        Assert.False(options.AnnounceSelcal);
    }
}
