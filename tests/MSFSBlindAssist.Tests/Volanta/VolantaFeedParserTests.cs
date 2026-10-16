using System.Globalization;
using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Characterization tests for Services/Volanta/VolantaFeedParser and VolantaLink's pure helpers. The fixtures are
// hand-written in the shapes measured from Volanta 1.19.2 on 2026-10-15 (docs/volanta.md); no captured user data.
public class VolantaFeedParserTests
{
    private const string VolantaFile = """
        {"time":1792098879,"data":[
          {"id":"f-1","callsign":"DAL1022","flightNumber":"DL1022","originIcao":"KATL","destinationIcao":"KGRB",
           "atcRoute":"DCT","network":"Volanta","networkUserId":"u-1","networkUserName":"pilot_one","isPremium":false,
           "position":{"latitude":38.92,"longitude":-86.06,"altitude":36000.0,"altitudeAgl":36897.2,"headingTrue":327.3,
             "groundSpeed":442.6,"verticalSpeed":-457.7,"onGround":false,"time":"2026-10-15T21:12:46.259Z",
             "transponder":"6431","simZuluTime":"2026-10-15T21:12:18Z","positionType":"Aircraft"},
           "aircraftIcao":"B738","aircraftRegistration":"N738BW","simulatorAbbreviation":"msfs24"},
          {"id":"f-2","originIcao":"KLDR","network":"Volanta","networkUserName":"heli","multiplayerNetwork":"Vatsim",
           "position":{"latitude":32.7,"longitude":-117.2,"altitude":10.0,"altitudeAgl":5.3,"headingTrue":200.0,
             "groundSpeed":0.0,"verticalSpeed":0.0,"onGround":true,"time":"2026-10-15T21:14:02.333Z"},
           "aircraftIcao":"AS365","simulatorAbbreviation":"xp12"},
          {"id":"f-3","network":"Volanta","networkUserName":"no_position"},
          {"id":"","network":"Volanta","position":{"latitude":1.0,"longitude":1.0}}
        ]}
        """;

    private const string NetworkFile = """
        {"time":1792098853,"data":[
          {"id":"n-1","callsign":"DAL160","originIcao":"KLEX","destinationIcao":"KIND","network":"Vatsim",
           "networkUserName":"1354450 KIND",
           "position":{"latitude":39.7,"longitude":-86.3,"altitude":801.0,"altitudeAgl":0.0,"headingTrue":51.0,
             "groundSpeed":9.0,"onGround":false,"verticalSpeed":0.0,"transponder":null,"time":"2026-10-15T21:14:06.011683Z"},
           "aircraftIcao":"A339"},
          {"id":"n-2","callsign":"UAL16","network":"Ivao","networkUserName":"Jane Doe",
           "position":{"latitude":40.0,"longitude":-74.0,"altitude":33000.0,"altitudeAgl":0.0,"headingTrue":90.0,
             "groundSpeed":480.0,"onGround":false,"verticalSpeed":0.0,"time":"2026-10-15T21:14:00Z"},
           "aircraftIcao":"B77W"},
          {"id":"n-3","callsign":"PE1","network":"PilotEdge","position":{"latitude":34.0,"longitude":-118.0,"groundSpeed":120.0}},
          {"id":"n-4","callsign":"X1","network":"SomethingNew","position":{"latitude":1.0,"longitude":2.0,"groundSpeed":200.0}}
        ]}
        """;

    private const string ControllersFile = """
        {"time":1792098900,"data":[
          {"airspace":[],"polygon":[],
           "session":{"callsign":"EDDP_GND","networkUserName":"Controller One","network":"Vatsim","facilityType":"Ground",
             "atisMessage":"Leipzig Ground\r\nBriefing at example","radioFrequency":"121.805",
             "firstSeen":"2026-10-15T17:28:39.030577","airportIcao":"EDDP","id":"c-1"},
           "location":{"airportIcaoCode":"EDDP","latitude":51.423889,"longitude":12.236389}},
          {"session":{"callsign":"LFFF_CTR","network":"Ivao","facilityType":"Center","radioFrequency":"128.100","id":"c-2"},
           "location":{"airportIcaoCode":"LFPG"}},
          {"session":{"callsign":"","id":""}}
        ]}
        """;

    [Fact]
    public void A_Volanta_user_keeps_every_field_and_Volanta_s_own_ground_flag()
    {
        var feed = VolantaFeedParser.ParseVolantaFlights(VolantaFile)!;
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792098879), feed.Time);
        var f = feed.Items.Single(x => x.Id == "f-1");
        Assert.Equal(VolantaSource.Volanta, f.Source);
        Assert.Null(f.AlsoOnNetwork);
        Assert.Equal("DAL1022", f.Callsign);
        Assert.Equal("DL1022", f.FlightNumber);
        Assert.Equal("pilot_one", f.UserName);
        Assert.Equal("u-1", f.UserId);
        Assert.Equal("B738", f.AircraftIcao);
        Assert.Equal("N738BW", f.Registration);
        Assert.Equal(("KATL", "KGRB"), (f.Origin, f.Destination));
        Assert.Equal("msfs24", f.Simulator);
        Assert.Equal(36000.0, f.AltitudeFt);
        Assert.Equal(36897.2, f.AltitudeAglFt);
        Assert.Equal(-457.7, f.VerticalSpeedFpm);
        Assert.Equal(442.6, f.GroundSpeedKt);
        Assert.False(f.OnGround);
        Assert.Equal("6431", f.Transponder);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 21, 12, 46, 259, TimeSpan.Zero), f.PositionTime);

        var heli = feed.Items.Single(x => x.Id == "f-2");
        Assert.True(heli.OnGround);
        Assert.Equal(VolantaSource.Vatsim, heli.AlsoOnNetwork);
    }

    [Fact]
    public void Entries_without_a_position_or_an_id_are_dropped()
        => Assert.Equal(new[] { "f-1", "f-2" }, VolantaFeedParser.ParseVolantaFlights(VolantaFile)!.Items.Select(f => f.Id));

    [Fact]
    public void A_network_pilot_s_ground_flag_comes_from_ground_speed_since_the_file_always_says_airborne()
    {
        var feed = VolantaFeedParser.ParseNetworkFlights(NetworkFile)!;
        var parked = feed.Items.Single(f => f.Id == "n-1");
        Assert.Equal(VolantaSource.Vatsim, parked.Source);
        Assert.True(parked.OnGround);            // 9 kt, though the file says onGround false
        Assert.Null(parked.AltitudeAglFt);       // the file's 0 means "none", not "on the ground"
        Assert.Null(parked.VerticalSpeedFpm);
        Assert.Equal("", parked.Transponder);    // JSON null

        var cruising = feed.Items.Single(f => f.Id == "n-2");
        Assert.Equal(VolantaSource.Ivao, cruising.Source);
        Assert.False(cruising.OnGround);
    }

    [Fact]
    public void Every_network_name_maps_and_an_unknown_one_is_other()
    {
        var feed = VolantaFeedParser.ParseNetworkFlights(NetworkFile)!;
        Assert.Equal(VolantaSource.PilotEdge, feed.Items.Single(f => f.Id == "n-3").Source);
        Assert.Equal(VolantaSource.Other, feed.Items.Single(f => f.Id == "n-4").Source);
        Assert.Equal(VolantaSource.Apoc, VolantaFeedParser.ParseSource("Apoc"));
        Assert.Equal(VolantaSource.Volanta, VolantaFeedParser.ParseSource(" VOLANTA "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"time\":1}")]
    [InlineData("[1,2,3]")]
    public void A_file_that_is_not_a_feed_parses_to_null(string json)
    {
        Assert.Null(VolantaFeedParser.ParseVolantaFlights(json));
        Assert.Null(VolantaFeedParser.ParseControllers(json));
    }

    [Fact]
    public void Numbers_sent_as_text_parse_with_the_invariant_culture_on_a_German_PC()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var feed = VolantaFeedParser.ParseVolantaFlights(
                """{"time":1,"data":[{"id":"a","position":{"latitude":"51.5","longitude":"-0.25","altitude":"1234.5","groundSpeed":"442,6"}}]}""")!;
            var f = feed.Items.Single();
            Assert.Equal(51.5, f.Latitude);
            Assert.Equal(-0.25, f.Longitude);
            Assert.Equal(1234.5, f.AltitudeFt);
            Assert.Equal(442.6, f.GroundSpeedKt);   // a lone comma is a decimal separator (CORE-17's external parse)
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData("1792098879000")]    // milliseconds: past the range FromUnixTimeSeconds takes
    [InlineData("-99999999999999")]
    public void A_feed_time_outside_the_unix_seconds_range_reads_as_now_instead_of_throwing(string stamp)
    {
        var feed = VolantaFeedParser.ParseVolantaFlights($$"""{"time":{{stamp}},"data":[]}""")!;
        Assert.Empty(feed.Items);
        Assert.InRange((DateTimeOffset.UtcNow - feed.Time).TotalSeconds, -1, 60);
    }

    [Fact]
    public void Controllers_keep_station_frequency_ATIS_and_location()
    {
        var feed = VolantaFeedParser.ParseControllers(ControllersFile)!;
        Assert.Equal(2, feed.Items.Count);
        var gnd = feed.Items[0];
        Assert.Equal("c-1", gnd.Id);
        Assert.Equal("EDDP_GND", gnd.Callsign);
        Assert.Equal(VolantaSource.Vatsim, gnd.Network);
        Assert.Equal("Ground", gnd.Facility);
        Assert.Equal("121.805", gnd.Frequency);
        Assert.Equal("Leipzig Ground\nBriefing at example", gnd.Atis);
        Assert.Equal("EDDP", gnd.AirportIcao);
        Assert.Equal(51.423889, gnd.Latitude);
        // firstSeen carries no zone: Volanta writes UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 17, 28, 39, TimeSpan.Zero).AddTicks(305770), gnd.OnlineSince);

        var ctr = feed.Items[1];
        Assert.Equal(VolantaSource.Ivao, ctr.Network);
        Assert.Equal("LFPG", ctr.AirportIcao);   // from the location when the session has none
        Assert.Null(ctr.Latitude);
    }

    [Fact]
    public void A_hub_frame_with_two_messages_yields_the_position_report_and_the_live_status()
    {
        string frame =
            """{"type":1,"target":"position-update","arguments":[{"latitude":38.92,"longitude":-86.06,"altitude":36000.0,"headingTrue":327.28,"groundSpeed":442.58,"onGround":false,"time":"2026-10-15T21:14:46.262Z"}]}"""
            + "\u001e"
            + """{"type":1,"target":"flight-live-status-update","arguments":[{"flightId":"f-1","callsign":"DAL1022","aircraftIcao":"B738","originIcao":"KATL","destinationIcao":"KGRB"}]}"""
            + "\u001e";
        var update = VolantaFeedParser.ParseHubFrame(frame);
        Assert.True(update.Any);
        var position = update.Position!.Value;
        Assert.Equal(38.92, position.Latitude);
        Assert.Equal(327.28, position.HeadingTrue);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 21, 14, 46, 262, TimeSpan.Zero), position.Time);
        Assert.Equal(new VolantaOwnFlight("f-1", "DAL1022", "B738", "KATL", "KGRB"), update.Flight);
    }

    private const string FriendsFile = """
        [{"friendId":"a1","profileImage":"x.png","userName":"friend_one","state":"Friend","initiatedTime":"2026-01-01T00:00:00Z",
          "acceptedTime":"2026-01-02T00:00:00Z","isOutbound":false,"currentStreak":3,"isPremium":false,"badge":null,
          "richStatus":{"state":"CruisingOverCountry","value":"United States","taxiing":false,"gateName":false,"oceanName":"",
            "countryName":"United States","nearestAirport":"KIND","altitude":35000.0,"sidIdentifier":"","climbing":false,
            "starIdentifier":"","departureAirport":"KATL","arrivalAirport":"KGRB","online":false}},
         {"friendId":"b2","userName":"pending_person","state":"FriendPending","richStatus":null},
         {"friendId":"c3","userName":"friend_two","state":"Friend","richStatus":{"state":"Offline","value":"","online":false}},
         {"userName":"no_id","state":"Friend"}]
        """;

    [Fact]
    public void The_friends_list_keeps_accepted_friends_with_their_live_status()
    {
        var friends = VolantaFeedParser.ParseFriends(FriendsFile)!;
        Assert.Equal(new[] { "a1", "c3" }, friends.Select(f => f.FriendId));
        var one = friends[0];
        Assert.Equal("friend_one", one.UserName);
        Assert.Equal("CruisingOverCountry", one.Status!.State);
        Assert.Equal("United States", one.Status.CountryName);
        Assert.Equal("KATL", one.Status.DepartureAirport);
        Assert.Equal("KGRB", one.Status.ArrivalAirport);
        Assert.Equal("", one.Status.GateName);           // Volanta sends false for no gate
        Assert.Equal(35000.0, one.Status.Altitude);
        Assert.False(one.Status.IsOffline);
        Assert.True(friends[1].Status!.IsOffline);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"data\":[]}")]
    [InlineData("not json")]
    public void Anything_but_the_friends_array_parses_to_null(string json) => Assert.Null(VolantaFeedParser.ParseFriends(json));

    [Fact]
    public void Hub_friend_messages_carry_added_removed_and_status_changes_in_order()
    {
        string frame =
            """{"type":1,"target":"friend-added","arguments":[{"friendId":"d4","userName":"new_friend","state":"Friend"}]}""" + "\u001e"
            + """{"type":1,"target":"friend-removed","arguments":["c3"]}""" + "\u001e"
            + """{"type":1,"target":"friend-removed","arguments":[{"friendId":"e5"}]}""" + "\u001e"
            + """{"type":1,"target":"rich-status-update","arguments":[{"friendId":"a1","richStatus":{"state":"Descending","arrivalAirport":"KGRB"}}]}""" + "\u001e";
        var update = VolantaFeedParser.ParseHubFrame(frame);
        Assert.Equal(new[] { "d4" }, update.FriendsAdded.Select(f => f.FriendId));
        Assert.Equal("new_friend", update.FriendsAdded[0].UserName);
        Assert.Equal(new[] { "c3", "e5" }, update.FriendsRemoved);
        Assert.Equal("a1", update.FriendStatuses.Single().FriendId);
        Assert.Equal("Descending", update.FriendStatuses.Single().Status.State);
        Assert.Null(update.Position);
    }

    [Theory]
    [InlineData("{\"type\":6}\u001e")]
    [InlineData("{\"type\":1,\"target\":\"rich-status-update\",\"arguments\":[{\"friendId\":\"x\"}]}\u001e")]
    [InlineData("{\"type\":1,\"target\":\"friend-removed\",\"arguments\":[\"\"]}")]
    [InlineData("{\"type\":1,\"target\":\"friend-added\",\"arguments\":[{\"userName\":\"no id\"}]}")]
    [InlineData("{\"type\":1,\"target\":\"flight-live-status-update\",\"arguments\":[{\"callsign\":\"NOID\"}]}")]
    [InlineData("{\"type\":1,\"target\":\"position-update\",\"arguments\":[{\"altitude\":100}]}")]
    [InlineData("{cut short")]
    [InlineData("")]
    public void Keep_alives_other_messages_and_incomplete_ones_yield_nothing(string frame)
    {
        var update = VolantaFeedParser.ParseHubFrame(frame);
        Assert.False(update.Any);
        Assert.Null(update.Position);
        Assert.Null(update.Flight);
    }

    [Fact]
    public void The_link_attaches_only_to_the_page_Volanta_serves_itself()
    {
        string list = """
            [{"type":"iframe","url":"https://pay.google.com/x","webSocketDebuggerUrl":"ws://127.0.0.1:9223/devtools/page/A"},
             {"type":"page","url":"","webSocketDebuggerUrl":"ws://127.0.0.1:9223/devtools/page/B"},
             {"type":"page","url":"http://127.0.0.1:17580/index.html#/flights","webSocketDebuggerUrl":"ws://127.0.0.1:9223/devtools/page/C"},
             {"type":"worker","url":"","webSocketDebuggerUrl":"ws://127.0.0.1:9223/devtools/page/D"}]
            """;
        Assert.Equal("ws://127.0.0.1:9223/devtools/page/C", VolantaLink.FindVolantaPage(list));
        Assert.Null(VolantaLink.FindVolantaPage("""[{"type":"page","url":"https://example.com"}]"""));
        Assert.Null(VolantaLink.FindVolantaPage("not json"));
    }

    [Theory]
    [InlineData("http://localhost:17581/index.html#/map", true)]   // Volanta's local server on another port, or name
    [InlineData("http://[::1]:17580/", true)]
    [InlineData("https://127.0.0.1:17580/index.html", true)]
    [InlineData("devtools://devtools/bundled/inspector.html", false)]
    [InlineData("file:///C:/Volanta/index.html", false)]
    [InlineData("http://192.168.1.20:17580/index.html", false)]
    [InlineData("", false)]
    public void Any_loopback_page_is_Volanta_s_so_a_local_port_change_never_leaves_the_link_waiting(string url, bool taken)
    {
        string list = $$"""[{"type":"page","url":"{{url}}","webSocketDebuggerUrl":"ws://127.0.0.1:9223/devtools/page/X"}]""";
        Assert.Equal(taken ? "ws://127.0.0.1:9223/devtools/page/X" : null, VolantaLink.FindVolantaPage(list));
    }

    [Theory]
    [InlineData("""{"s":"paused","u":"73e6c47e-9b8d-416b-a257-b8e9dd32b6f9"}""", true, "73e6c47e-9b8d-416b-a257-b8e9dd32b6f9")]
    [InlineData("""{"s":"live","u":""}""", false, null)]
    [InlineData("""{"s":"","u":"not-a-user-id"}""", null, null)]
    [InlineData("""{"s":42}""", null, null)]
    [InlineData("", null, null)]
    public void The_window_check_reads_paused_or_live_and_only_a_GUID_as_the_user_id(string value, bool? paused, string? userId)
        => Assert.Equal((paused, userId), VolantaLink.ParseWindowCheck(value));

    [Theory]
    [InlineData("https://webassets.volanta.app/volanta-flight-positions-slim.json", "VolantaFlights")]
    [InlineData("https://webassets.volanta.app/volanta-flight-positions.json", "VolantaFlights")]
    [InlineData("https://webassets.volanta.app/network-flight-positions.json", "NetworkFlights")]
    [InlineData("https://webassets.volanta.app/network-controllers.json", "Controllers")]
    [InlineData("https://api.volanta.app/api/v1/Relationships/Friends", "Friends")]
    [InlineData("https://api.volanta.app/api/v1/relationships/friends/", "Friends")]
    [InlineData("https://api.volanta.app/api/v1/Relationships/Friends/abc", null)]
    [InlineData("https://api.volanta.app/api/v1/Flights/Active", null)]
    [InlineData("https://cdn.volanta.app/notams/main.json", null)]
    [InlineData("https://example.com/volanta-flight-positions-slim.json", null)]
    [InlineData("not a url", null)]
    public void Only_Volanta_s_three_traffic_files_are_read(string url, string? expected)
        => Assert.Equal(expected, VolantaLink.FeedKindForUrl(url)?.ToString());
}
