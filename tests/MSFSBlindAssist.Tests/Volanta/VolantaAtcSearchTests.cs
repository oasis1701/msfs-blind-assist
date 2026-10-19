using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// The ATC tab's search: an airport's ATC, wherever it is.
public class VolantaAtcSearchTests
{
    private static VolantaController Station(string callsign, string airport = "", string user = "", string frequency = "")
        => new() { Id = callsign, Callsign = callsign, AirportIcao = airport, UserName = user, Frequency = frequency };

    private static readonly VolantaController[] Stations =
    {
        Station("EGLL_TWR", airport: "EGLL", frequency: "118.500"),
        Station("EGLL_N_APP", airport: "EGLL"),
        Station("LON_S_CTR", airport: "EGLL", user: "Jane Doe"),
        Station("KATL_ATIS", airport: "KATL"),
        Station("ATL_TWR"),
        Station("ATL_APP"),
        Station("SATL_CTR"),
        Station("EDDP_GND", airport: "EDDP"),
    };

    private static string[] Found(string search)
        => Stations.Where(s => VolantaAtcSearch.Matches(s, search)).Select(s => s.Callsign).ToArray();

    [Fact]
    public void An_airport_finds_the_stations_volanta_gives_it()
        => Assert.Equal(new[] { "EGLL_TWR", "EGLL_N_APP", "LON_S_CTR" }, Found(" egll "));

    [Fact]
    public void A_us_airport_also_finds_the_stations_named_without_its_k()
        => Assert.Equal(new[] { "KATL_ATIS", "ATL_TWR", "ATL_APP" }, Found("katl"));

    [Fact]
    public void Callsign_controller_and_frequency_match_too()
    {
        Assert.Equal(new[] { "EDDP_GND" }, Found("eddp_g"));
        Assert.Equal(new[] { "LON_S_CTR" }, Found("jane"));
        Assert.Equal(new[] { "EGLL_TWR" }, Found("118.5"));
    }

    [Fact]
    public void A_blank_search_matches_every_station()
        => Assert.Equal(Stations.Length, Found("  ").Length);
}
