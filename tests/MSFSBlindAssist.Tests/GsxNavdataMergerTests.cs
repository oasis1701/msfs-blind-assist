// Characterization tests for MSFSBlindAssist.Services.Gsx.GsxNavdataMerger.
//
// No dedicated probe exists; cases derived by reading Merge/FindNavMatch (the
// position-priority chain: this_parking_pos -> navdata -> stop position as LAST
// resort; and the never-cross-concourse-borrow SAFETY guard) and confirmed by running
// the tests. This is characterization, not spec verification: if a literal ever
// disagrees with actual output, the test must be corrected to match real output, not
// the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

public class GsxNavdataMergerTests
{
    private static ParkingSpot Nav(string name, int number, double lat = 1.0, double lon = 2.0, double hdg = 90.0)
        => new ParkingSpot { Name = name, Number = number, Latitude = lat, Longitude = lon, Heading = hdg };

    private static GsxGate Gate(
        string concourse, int number, string suffix = "",
        bool hasParkingPos = false, double lat = 0, double lon = 0, double hdg = 0,
        double? stopLat = null, double? stopLon = null, double? stopHdg = null)
        => new GsxGate
        {
            Concourse = concourse,
            Number = number,
            Suffix = suffix,
            HasParkingPos = hasParkingPos,
            Latitude = lat,
            Longitude = lon,
            Heading = hdg,
            StopLatitude = stopLat,
            StopLongitude = stopLon,
            StopHeading = stopHdg,
        };

    // --- Never-cross-concourse-borrow invariant --------------------------------

    [Fact]
    public void Same_concourse_navdata_candidate_donates_its_coordinates()
    {
        var nav = new List<ParkingSpot> { Nav("A", 12, lat: 10.0, lon: 20.0, hdg: 45.0) };
        var gates = new List<GsxGate> { Gate("A", 12) }; // no parking pos, no stop pos

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        var spot = Assert.Single(result);
        Assert.Equal(10.0, spot.Latitude);
        Assert.Equal(20.0, spot.Longitude);
        Assert.Equal(45.0, spot.Heading);
    }

    [Fact]
    public void Cross_concourse_navdata_candidate_is_dropped_not_borrowed()
    {
        // GSX gate is concourse A12; the ONLY navdata candidate for number 12 is concourse B.
        // A mislabeled coordinate on another pier is worse than omission -> the spot must be
        // dropped entirely (no parking pos / stop pos to fall back on).
        var nav = new List<ParkingSpot> { Nav("B", 12, lat: 10.0, lon: 20.0) };
        var gates = new List<GsxGate> { Gate("A", 12) };

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        // The GSX A12 gate is dropped (unplaceable); the navdata-only B12 stand survives untouched.
        var spot = Assert.Single(result);
        Assert.Equal("B", spot.Name);
        Assert.Equal(GateSource.Navdata, spot.Source);
    }

    [Fact]
    public void Cross_concourse_candidate_is_dropped_even_with_multiple_same_number_candidates()
    {
        var nav = new List<ParkingSpot>
        {
            Nav("B", 12, lat: 10.0, lon: 20.0),
            Nav("C", 12, lat: 30.0, lon: 40.0),
        };
        var gates = new List<GsxGate> { Gate("A", 12) };

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        // Neither B12 nor C12 may donate coordinates to the A12 GSX gate.
        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Equal(GateSource.Navdata, s.Source));
    }

    [Fact]
    public void Gate_with_no_concourse_borrows_any_same_number_candidate()
    {
        // GSX gate has NO concourse letter -> best-effort: any same-number candidate is fine
        // (documented as the intentional exception to the concourse-safety guard).
        var nav = new List<ParkingSpot> { Nav("B", 51, lat: 5.0, lon: 6.0) };
        var gates = new List<GsxGate> { Gate("", 51) };

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        var spot = Assert.Single(result);
        Assert.Equal(5.0, spot.Latitude);
        Assert.Equal(6.0, spot.Longitude);
    }

    // --- Position-priority chain -------------------------------------------------

    [Fact]
    public void GSX_parking_pos_wins_over_navdata_when_both_present()
    {
        var nav = new List<ParkingSpot> { Nav("A", 12, lat: 10.0, lon: 20.0) };
        var gates = new List<GsxGate> { Gate("A", 12, hasParkingPos: true, lat: 99.0, lon: 88.0, hdg: 270.0) };

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        var spot = Assert.Single(result);
        Assert.Equal(99.0, spot.Latitude);
        Assert.Equal(88.0, spot.Longitude);
        Assert.Equal(270.0, spot.Heading);
    }

    [Fact]
    public void Stop_position_is_used_only_as_the_last_resort()
    {
        // No parking pos, no navdata match at all (no bucket for number 12) -> falls to stop pos.
        var nav = new List<ParkingSpot>();
        var gates = new List<GsxGate> { Gate("A", 12, stopLat: 1.5, stopLon: 2.5, stopHdg: 180.0) };

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        var spot = Assert.Single(result);
        Assert.Equal(1.5, spot.Latitude);
        Assert.Equal(2.5, spot.Longitude);
        Assert.Equal(180.0, spot.Heading);
    }

    [Fact]
    public void Unplaceable_gate_with_no_position_source_at_all_is_dropped()
    {
        var nav = new List<ParkingSpot>();
        var gates = new List<GsxGate> { Gate("A", 12) }; // no parking pos, no nav match, no stop pos

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        Assert.Empty(result);
    }

    [Fact]
    public void Navdata_only_stands_with_no_matching_gate_are_appended_unchanged()
    {
        var nav = new List<ParkingSpot> { Nav("Z", 99, lat: 1.0, lon: 1.0) };
        var gates = new List<GsxGate>();

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        var spot = Assert.Single(result);
        Assert.Equal("Z", spot.Name);
        Assert.Equal(99, spot.Number);
        Assert.Equal(GateSource.Navdata, spot.Source);
    }

    [Fact]
    public void GSX_gate_with_no_concourse_letter_borrows_navdatas_display_name()
    {
        var nav = new List<ParkingSpot> { Nav("P", 209, lat: 1.0, lon: 2.0) };
        var gates = new List<GsxGate> { Gate("", 209) }; // GSX "P 209" style: no concourse letter

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        var spot = Assert.Single(result);
        Assert.Equal("P", spot.Name);
    }

    [Fact]
    public void Matched_navdata_stand_is_not_also_appended_separately()
    {
        var nav = new List<ParkingSpot> { Nav("A", 12, lat: 10.0, lon: 20.0) };
        var gates = new List<GsxGate> { Gate("A", 12) };

        var result = GsxNavdataMerger.Merge(nav, gates, "EDDF");

        Assert.Single(result); // not two entries for the same physical stand
    }
}
