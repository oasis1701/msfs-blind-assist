// Pins Navigation.BacktrackConnectionHandoff — the connection-node leg of the post-landing
// backtrack (what is said when the taxiway comes into range, where the tone steers from then on,
// and when the backtrack hands off).
//
// The case that made it exist: CYYZ 05, 2026-10-03 (taxi-landing-port review, finding 1). The
// corridor-filtered connection node sat on taxiway H 70-88 m off the centreline. The tone steered
// the centreline, the callout named no side, and the 25 m hand-off was never reached: the pilot
// passed the node abeam at 69 m and the distance climbed for good (…, 73, 69, 73, 84, 99, 117, 128).
//
// Also pins TaxiGraph.PreferredTaxiwayNameAt, the ranking Build has always used to name a hold
// node, now shared with the backtrack's "Taxiway H ahead".

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class BacktrackConnectionHandoffTests
{
    // ---- the approach window --------------------------------------------------------------

    [Theory]
    [InlineData(200.0, true)]
    [InlineData(199.0, true)]
    [InlineData(0.0, true)]
    [InlineData(200.1, false)]
    [InlineData(213.0, false)]   // CYYZ: the turnaround completed 213 m from the node
    [InlineData(-1.0, false)]    // no node
    public void Approach_window_is_the_announce_distance_and_needs_a_node(double distM, bool expected)
        => Assert.Equal(expected, BacktrackConnectionHandoff.InApproachWindow(distM));

    // ---- the side word --------------------------------------------------------------------

    [Theory]
    [InlineData(226.5, 250.0, "right")]   // node 23.5° right of the backtrack heading
    [InlineData(226.5, 203.0, "left")]
    [InlineData(226.5, 236.4, null)]      // 9.9°: below the floor, no side spoken
    [InlineData(226.5, 236.5, "right")]   // exactly at the floor is spoken
    [InlineData(226.5, 216.5, "left")]
    [InlineData(10.0, 350.0, "left")]     // across the 0/360 wrap
    [InlineData(350.0, 10.0, "right")]
    public void Side_word_is_the_bearing_to_the_node_against_the_backtrack_heading(
        double backtrackHdg, double bearingToNode, string? expected)
        => Assert.Equal(expected, BacktrackConnectionHandoff.SideWord(bearingToNode, backtrackHdg));

    // ---- the approach sentence ------------------------------------------------------------

    [Fact]
    public void Approach_sentence_names_the_taxiway_and_the_side()
        => Assert.Equal("Taxiway H ahead on the left. Vacate runway.",
            BacktrackConnectionHandoff.ComposeApproach("H", "left"));

    [Fact]
    public void Approach_sentence_with_no_side_names_the_taxiway_only()
        => Assert.Equal("Taxiway H ahead. Vacate runway.",
            BacktrackConnectionHandoff.ComposeApproach("H", null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Approach_sentence_with_no_name_says_taxiway_ahead(string? name)
        => Assert.Equal("Taxiway ahead on the right. Vacate runway.",
            BacktrackConnectionHandoff.ComposeApproach(name, "right"));

    // ---- the tone -------------------------------------------------------------------------

    [Fact]
    public void Tone_keeps_the_centreline_law_until_the_approach_is_announced()
        => Assert.Equal(-0.06,
            BacktrackConnectionHandoff.ToneHeadingError(
                approachAnnounced: false, bearingToNodeTrue: 250.0, headingTrue: 226.5, centerlineHeadingError: -0.06),
            3);

    [Fact]
    public void Tone_steers_at_the_node_once_the_approach_is_announced()
        => Assert.Equal(23.5,
            BacktrackConnectionHandoff.ToneHeadingError(
                approachAnnounced: true, bearingToNodeTrue: 250.0, headingTrue: 226.5, centerlineHeadingError: -0.06),
            3);

    [Fact]
    public void Tone_error_to_the_node_is_normalised_across_the_wrap()
        => Assert.Equal(-20.0,
            BacktrackConnectionHandoff.ToneHeadingError(
                approachAnnounced: true, bearingToNodeTrue: 350.0, headingTrue: 10.0, centerlineHeadingError: 0.0),
            3);

    // ---- the hand-off ---------------------------------------------------------------------

    [Fact]
    public void Passing_the_node_abeam_hands_off_as_vacated_once_clear_of_the_runway()
    {
        // CYYZ: closest approach 69 m, the aircraft off the pavement on the H turn-off.
        var action = BacktrackConnectionHandoff.Decide(
            distToNodeMetres: 69.0, approachAnnounced: true, clearOfAllRunwayCorridors: true);
        Assert.Equal(BacktrackHandoffAction.Vacated, action);
    }

    [Fact]
    public void Still_on_the_pavement_beyond_the_handoff_distance_keeps_backtracking()
    {
        var action = BacktrackConnectionHandoff.Decide(
            distToNodeMetres: 69.0, approachAnnounced: true, clearOfAllRunwayCorridors: false);
        Assert.Equal(BacktrackHandoffAction.Continue, action);
    }

    [Fact]
    public void Clear_of_the_runway_before_the_approach_is_announced_is_not_a_handoff()
    {
        // LGZA 16: backtracking on the grass beside the pavement, clear of every corridor from
        // the start, with the node still far ahead. Must be steered back, never told "vacated".
        var action = BacktrackConnectionHandoff.Decide(
            distToNodeMetres: 600.0, approachAnnounced: false, clearOfAllRunwayCorridors: true);
        Assert.Equal(BacktrackHandoffAction.Continue, action);
    }

    [Fact]
    public void Within_the_handoff_distance_and_clear_is_vacated_even_before_the_announce()
    {
        var action = BacktrackConnectionHandoff.Decide(
            distToNodeMetres: 20.0, approachAnnounced: false, clearOfAllRunwayCorridors: true);
        Assert.Equal(BacktrackHandoffAction.Vacated, action);
    }

    [Fact]
    public void Within_the_handoff_distance_but_still_on_pavement_hands_into_the_clearing_phase()
    {
        var action = BacktrackConnectionHandoff.Decide(
            distToNodeMetres: 25.0, approachAnnounced: true, clearOfAllRunwayCorridors: false);
        Assert.Equal(BacktrackHandoffAction.ClearingAhead, action);
    }

    [Fact]
    public void No_node_never_hands_off()
    {
        Assert.Equal(BacktrackHandoffAction.Continue,
            BacktrackConnectionHandoff.Decide(-1.0, approachAnnounced: false, clearOfAllRunwayCorridors: false));
        // With no node the approach is never announced, so this combination cannot occur in the
        // manager; pinned only so the distance guard is not read as the gate for the clear case.
        Assert.Equal(BacktrackHandoffAction.Continue,
            BacktrackConnectionHandoff.Decide(-1.0, approachAnnounced: false, clearOfAllRunwayCorridors: true));
    }

    // ---- TaxiGraph.PreferredTaxiwayNameAt -------------------------------------------------

    private const double OffLat = 0.0006;

    private static TaxiGraph BuildNamedGraph()
    {
        var paths = new List<TaxiPath>
        {
            // Node at (OffLat, 0.006) carries H, H3 and KILO5A; node at (OffLat, 0.012) carries
            // only the single-letter names B and MAIN; node at (OffLat, 0.018) is unnamed.
            new TaxiPath { Name = "H",      StartLat = OffLat, StartLon = 0.000, EndLat = OffLat, EndLon = 0.006 },
            new TaxiPath { Name = "H3",     StartLat = OffLat, StartLon = 0.006, EndLat = 0,      EndLon = 0.006 },
            new TaxiPath { Name = "KILO5A", StartLat = OffLat, StartLon = 0.006, EndLat = OffLat, EndLon = 0.012 },
            new TaxiPath { Name = "B",      StartLat = OffLat, StartLon = 0.012, EndLat = 0,      EndLon = 0.012 },
            new TaxiPath { Name = "MAIN",   StartLat = OffLat, StartLon = 0.012, EndLat = OffLat, EndLon = 0.018 },
            new TaxiPath { Name = "",       StartLat = OffLat, StartLon = 0.018, EndLat = 0,      EndLon = 0.018 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static int NodeAt(TaxiGraph g, double lat, double lon)
        => g.Nodes.Values.First(n => Math.Abs(n.Latitude - lat) < 1e-7 && Math.Abs(n.Longitude - lon) < 1e-7).NodeId;

    [Fact]
    public void Preferred_name_is_the_shortest_connector_designator()
    {
        var g = BuildNamedGraph();
        Assert.Equal("H3", g.PreferredTaxiwayNameAt(NodeAt(g, OffLat, 0.006)));
    }

    [Fact]
    public void Preferred_name_falls_back_to_the_longest_plain_name()
    {
        var g = BuildNamedGraph();
        // KILO5A is a connector-style name, so it wins at (OffLat, 0.012) over B and MAIN.
        Assert.Equal("KILO5A", g.PreferredTaxiwayNameAt(NodeAt(g, OffLat, 0.012)));
        // The node at (OffLat, 0.018) carries MAIN and an unnamed edge: MAIN.
        Assert.Equal("MAIN", g.PreferredTaxiwayNameAt(NodeAt(g, OffLat, 0.018)));
    }

    [Fact]
    public void Preferred_name_is_empty_for_an_unnamed_node_and_an_unknown_node()
    {
        var g = BuildNamedGraph();
        Assert.Equal("", g.PreferredTaxiwayNameAt(NodeAt(g, 0, 0.018)));
        Assert.Equal("", g.PreferredTaxiwayNameAt(-12345));
    }
}
