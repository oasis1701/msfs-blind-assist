// "Does this handoff route drive back across the runway we just landed on?"
//
// Live KATL 2026-08-27. Landed 26R, planned exit B1 (south side, 8,276 ft). Rolled past it
// without turning, so the overshoot monitor retargeted to exit A at 8,843 ft -- which leaves
// the runway on the NORTH side. The taxi graph holds no runway edges, so A* could not route
// along the runway to A's junction; it routed B1 south -> taxiway B west -> taxiway H north,
// ACROSS the 08L threshold, and back up to A. 427 m, a 180 degree arc, and a crossing of the
// landing runway 15-20 m inside its own threshold at 22 kt.
//
// IsHandoffRouteReachable did not catch it because it measures only the FIRST segment's
// cross-track, and B1 started right at the aircraft (commit 425217ca says so explicitly).

using System.Collections.Generic;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class RolloutRunwayReCrossingTests
{
    // KATL 08L/26R, fs2024 navdata: lat 33.649532, lon -84.439072 (08L) to -84.409378 (26R).
    private static TaxiGraph.RunwayCenterline Katl08L26R() => new()
    {
        Lat1 = 33.649532, Lon1 = -84.439072, Name1 = "08L",
        Lat2 = 33.649536, Lon2 = -84.409378, Name2 = "26R",
    };

    private static TaxiNode N(double lat, double lon) =>
        new() { NodeId = 1, Latitude = lat, Longitude = lon };

    private static TaxiRouteSegment Seg(double aLat, double aLon, double bLat, double bLon) =>
        new() { FromNode = N(aLat, aLon), ToNode = N(bLat, bLon), PathWidth = 75.0 };

    [Fact]
    public void The_live_KATL_handoff_route_re_crosses_the_landing_runway()
    {
        // The three legs that matter: south down B1, west along B, then north on H across
        // the 08L threshold onto taxiway A.
        var segments = new List<TaxiRouteSegment>
        {
            Seg(33.649509, -84.436646, 33.648414, -84.437675),  // B1 southbound
            Seg(33.648414, -84.437675, 33.648414, -84.438011),  // B westbound
            Seg(33.649303, -84.438919, 33.649719, -84.438911),  // H northbound ACROSS 08L
        };
        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, Katl08L26R()));
    }

    [Fact]
    public void A_normal_vacate_that_only_moves_away_from_the_axis_is_not_a_crossing()
    {
        // Exit B1 southbound and onward down B -- never returns to the north side.
        var segments = new List<TaxiRouteSegment>
        {
            Seg(33.649509, -84.436646, 33.648685, -84.437339),
            Seg(33.648685, -84.437339, 33.648414, -84.437675),
        };
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, Katl08L26R()));
    }

    [Fact]
    public void Segments_before_fromSegmentIndex_are_not_judged()
    {
        var segments = new List<TaxiRouteSegment>
        {
            Seg(33.649303, -84.438919, 33.649719, -84.438911),  // crosses -- but behind us
            Seg(33.649719, -84.438911, 33.650093, -84.438911),  // north up A
        };
        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, Katl08L26R()));
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 1, Katl08L26R()));
    }

    [Fact]
    public void An_empty_route_never_crosses()
    {
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(
            new List<TaxiRouteSegment>(), 0, Katl08L26R()));
    }

    [Fact]
    public void An_out_of_range_index_never_crosses()
    {
        var segments = new List<TaxiRouteSegment> { Seg(33.649303, -84.438919, 33.649719, -84.438911) };
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 5, Katl08L26R()));
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, -1, Katl08L26R()));
    }

    [Fact]
    public void The_landing_runway_is_found_by_either_designator()
    {
        var all = new List<TaxiGraph.RunwayCenterline> { Katl08L26R() };
        Assert.NotNull(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "26R"));
        Assert.NotNull(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "08L"));
        // Unpadded spellings occur in the DB ecosystem.
        Assert.NotNull(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "8L"));
    }

    [Fact]
    public void A_different_runway_is_not_the_landing_runway()
    {
        var all = new List<TaxiGraph.RunwayCenterline> { Katl08L26R() };
        Assert.Null(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "09L"));
        Assert.Null(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, null));
        Assert.Null(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, ""));
    }
}

// The sentence a runway-re-crossing DECLINE speaks, once, on the pilot's behalf.
//
// The decline keeps the aircraft in LandingRollout on the reasoning that the rollout tone
// is a live cue. That only holds inside RolloutExitGate.ExitToneArmFeet (300 ft). Beyond it
// SelectToneMode has two states that make no sound for a stopped, aligned aircraft -- the
// 300-1,000 ft turn-window Silent, and a sub-DriftToneSilentDeg DriftCorrection, which is a
// heading cue at zero volume -- and `trulyStopped` carries no distance gate. So a pilot who
// brakes to a stop 1,500 ft short of the exit could sit in the decline loop indefinitely,
// stationary on an ACTIVE RUNWAY with no tone and no words.
//
// These pin the three safety-bearing wording constraints: never claim the aircraft is clear
// of the runway, never say "stop" or "hold" (the other landing-exit closures do, and that
// wording is only safe off the pavement), and always carry BOTH the exit name and the
// distance. DistanceFormatter.UnitProvider is process-global, hence the shared collection.
[Collection("DistanceUnitGlobalState")]
public class RolloutCrossingDeclinePhraseTests
{
    [Fact]
    public void It_names_the_exit_and_the_distance_ahead()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal(
            "Continue rolling to taxiway B1, 900 feet ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 900));
    }

    [Fact]
    public void It_follows_the_active_distance_unit()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Metres;
        Assert.Equal(
            "Continue rolling to taxiway B1, 250 metres ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 820));
    }

    [Fact]
    public void An_unnamed_exit_still_reads_as_a_sentence()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal(
            "Continue rolling to the exit, 500 feet ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit(null, 500));
        Assert.Equal(
            "Continue rolling to the exit, 500 feet ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("   ", 500));
    }

    [Fact]
    public void A_non_positive_distance_drops_the_clause_rather_than_saying_zero_feet()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal("Continue rolling to taxiway B1.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 0));
        Assert.Equal("Continue rolling to taxiway B1.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", -30));
    }

    // The safety constraints, stated as tests so a later reword cannot quietly break them.
    [Theory]
    [InlineData("B1", 1500.0)]
    [InlineData(null, 1500.0)]
    [InlineData("B1", 0.0)]
    public void It_never_says_stop_or_hold_and_never_claims_the_runway_is_clear(
        string? name, double feet)
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        string s = RolloutRunwayReCrossing.ComposeContinueToExit(name, feet);
        Assert.DoesNotContain("stop", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hold", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clear", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vacat", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Continue rolling to ", s, System.StringComparison.Ordinal);
    }
}
