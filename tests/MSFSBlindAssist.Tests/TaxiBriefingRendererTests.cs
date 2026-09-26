using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

public class TaxiBriefingRendererTests
{
    private static readonly AircraftProfile Md11F = AircraftSizeClass.Resolve("MD1F", "MD-11F", 0);
    private static readonly AircraftProfile B738 = AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189);

    private static LandingExit Exit(string name, double thresholdFt, string type, string side) => new()
    {
        TaxiwayName = name, DistanceFromThresholdFeet = thresholdFt, DistanceFromTouchdownFeet = thresholdFt - 1000,
        ExitType = type, ExitSide = side, VacatesRunway = true,
    };

    private static StandChoice Cargo() =>
        new(new ParkingSpot { Name = "C", Number = 1, Type = 6 }, StandChoiceSource.AirlineMatch, Array.Empty<string>());

    private static TaxiBriefing FullBriefing()
    {
        var aa = Exit("AA", 6200, "High-speed", "Right");
        var ab = Exit("AB", 7100, "Normal", "Right");
        var taxiOut = new TaxiLegBriefing
        {
            Icao = "KMEM", Runway = "36L", Tier = BriefingTier.Navdata,
            EndpointDescription = "representative stand C 1 (Ramp Cargo, UPS)", Stand = Cargo(),
            Taxiways = new[] { "N", "M", "A", "B" }, DistanceMetres = 2400,
            HoldShorts = new[] { new HoldShortNote("27", "M", false), new HoldShortNote("36L", "B", true) },
            NarrowTaxiways = new[] { new NarrowTaxiwayNote("K", 15.0, 18.0) },
        };
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KLAX", Runway = "25L", Tier = BriefingTier.Navdata,
            EndpointDescription = "SayIntentions assigned gate 52A",
            Taxiways = new[] { "AA", "E", "C" }, DistanceMetres = 3100,
            HoldShorts = new[] { new HoldShortNote("25R", "AA", false) },
            Exit = new ExitChoice(aa, ab, true), VacatingExits = new[] { aa, ab },
            Notes = new[] { "assigned gate matched by position" },
        };
        return new TaxiBriefing(Md11F, taxiOut, taxiIn);
    }

    [Fact]
    public void Full_two_leg_block()
    {
        const string expected =
            "TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)\n" +
            "Aircraft: MD-11F (SimBrief type MD1F), size class D, wingspan 51.7 m, freighter: cargo stands preferred\n" +
            "TAXI OUT at KMEM (scenery navdata): from representative stand C 1 (Ramp Cargo, UPS) to runway 36L\n" +
            "  Taxiways: N, M, A, B (2.4 km)\n" +
            "  Hold short: runway 27 on taxiway M (crossing); runway 36L on taxiway B (before entering)\n" +
            "  Taxiway width note: taxiway K is 15.0 m in the navdata, below the 18.0 m code D minimum\n" +
            "TAXI IN at KLAX (scenery navdata), landing runway 25L\n" +
            "  Expected exit: taxiway AA, high-speed, RIGHT side, 6,200 ft from the threshold. Next exit if missed: AB, right side, 7,100 ft\n" +
            "  Stand: SayIntentions assigned gate 52A\n" +
            "  Taxiways from the exit: AA, E, C (3.1 km)\n" +
            "  Hold short: runway 25R on taxiway AA (crossing)\n" +
            "  Exits on 25L that get clear of the runway: AA (6,200 ft, right, high-speed), AB (7,100 ft, right, normal)\n" +
            "  Note: assigned gate matched by position";

        Assert.Equal(expected, TaxiBriefingRenderer.Render(FullBriefing()));
    }

    [Fact]
    public void Unavailable_legs_and_unknown_aircraft()
    {
        var b = new TaxiBriefing(
            AircraftSizeClass.Resolve("ZZZZ", "", null),
            TaxiLegBriefing.UnavailableLeg("LOWI", "08", BriefingTier.None, "the navigation database has no taxiways for LOWI and OpenStreetMap data is not yet available"),
            TaxiLegBriefing.UnavailableLeg("LOWI", "26", BriefingTier.Navdata, "no exit taxiway is mapped clear of runway 26 in this scenery",
                stand: Cargo(), endpoint: "representative stand C 1 (Ramp Cargo)", exitsSearched: true));

        const string expected =
            "TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)\n" +
            "Aircraft: ZZZZ (SimBrief type ZZZZ), size class unknown, wingspan unknown (aircraft type not recognised), passenger\n" +
            "TAXI OUT at LOWI: taxi route unavailable — the navigation database has no taxiways for LOWI and OpenStreetMap data is not yet available\n" +
            "TAXI IN at LOWI: taxi route unavailable — no exit taxiway is mapped clear of runway 26 in this scenery\n" +
            "  Stand: representative stand C 1 (Ramp Cargo)\n" +
            "  Exits on 26 that get clear of the runway: none found";

        Assert.Equal(expected, TaxiBriefingRenderer.Render(b));
    }

    [Fact]
    public void An_unavailable_taxi_in_lists_exits_only_when_they_were_searched()
    {
        // "Exits on 22L that get clear of the runway: none found" after "no navigation database loaded" (or a timeout, or
        // a runway not in the database) reported a search that was never made. A real search that found nothing still
        // says "none found".
        var noDatabase = TaxiBriefing.Unavailable(B738, "EGLL", "27R", "KJFK", "22L", "no navigation database loaded");
        Assert.DoesNotContain("Exits on", TaxiBriefingRenderer.Render(noDatabase), StringComparison.Ordinal);

        var searched = new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("EGLL", "27R", BriefingTier.Navdata, "x"),
            TaxiLegBriefing.UnavailableLeg("KJFK", "22L", BriefingTier.Navdata,
                "no exit taxiway is mapped clear of runway 22L in this scenery", exitsSearched: true));
        Assert.EndsWith("\n  Exits on 22L that get clear of the runway: none found", TaxiBriefingRenderer.Render(searched));
    }

    [Fact]
    public void An_unnamed_exit_reads_as_unnamed_wherever_it_is_printed()
    {
        // KSDF 35R: "Expected exit: taxiway , normal, …" and " (3,082 ft, …)" with nothing before it.
        var unnamed = Exit("", 3082, "Normal", "Right");
        var later = Exit("", 7388, "Normal", "Right");
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KSDF", Runway = "35R", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand Gate 57",
            Taxiways = new[] { "E" }, DistanceMetres = 1900,
            Exit = new ExitChoice(unnamed, later, true), VacatingExits = new[] { unnamed, later },
        };
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("KSDF", "17R", BriefingTier.Navdata, "x"), taxiIn));

        Assert.Contains("  Expected exit: taxiway (unnamed), normal, RIGHT side, 3,082 ft from the threshold. " +
                        "Next exit if missed: (unnamed), right side, 7,388 ft\n", text);
        Assert.Contains("  Exits on 35R that get clear of the runway: (unnamed) (3,082 ft, right, normal), (unnamed) (7,388 ft, right, normal)", text);
    }

    [Fact]
    public void An_end_exit_reads_as_end_of_runway_or_sharp_angle()
    {
        // ExitType "End" is the last 15 % of the runway OR a turn of more than 110°; a bare "end" read as the former only.
        var m3 = Exit("M3", 2106, "End", "Right");
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "OMDB", Runway = "12L", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand F 17",
            Taxiways = new[] { "M3" }, DistanceMetres = 900, Exit = new ExitChoice(m3, null, true), VacatingExits = new[] { m3 },
        };
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("OMDB", "12L", BriefingTier.Navdata, "x"), taxiIn));

        Assert.Contains("  Expected exit: taxiway M3, end-of-runway or sharp-angle, RIGHT side, 2,106 ft from the threshold.", text);
        Assert.Contains("M3 (2,106 ft, right, end-of-runway or sharp-angle)", text);
    }

    [Fact]
    public void OpenStreetMap_tier_is_labelled_and_no_crossings_are_said()
    {
        var leg = new TaxiLegBriefing
        {
            Icao = "LOWI", Runway = "26", Tier = BriefingTier.OpenStreetMap,
            EndpointDescription = "representative stand 12", Taxiways = new[] { "A" }, DistanceMetres = 640,
            HoldShorts = new[] { new HoldShortNote("26", "A", true) }, Notes = new[] { "stand types unknown (OpenStreetMap)" },
        };
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "LOWI", Runway = "26", Tier = BriefingTier.OpenStreetMap, EndpointDescription = "representative stand 12",
            Taxiways = new[] { "A" }, DistanceMetres = 640,
            Exit = new ExitChoice(Exit("B", 3000, "Normal", ""), null, false), VacatingExits = new[] { Exit("B", 3000, "Normal", "") },
        };
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, leg, taxiIn));

        Assert.Contains("TAXI OUT at LOWI (OpenStreetMap, planning only — taxi guidance cannot use this): from representative stand 12 to runway 26\n", text);
        Assert.Contains("  Taxiways: A (640 m)\n", text);
        Assert.Contains("  Note: stand types unknown (OpenStreetMap)\n", text);
        Assert.Contains("  Expected exit: taxiway B, normal, side unknown, 3,000 ft from the threshold. No later exit is mapped at least 500 ft further along. This runway is short for this aircraft: no exit is comfortably reachable at 130 kt; the last exit is briefed.\n", text);
        Assert.Contains("  No runway crossings on this route.\n", text);
        Assert.Contains("wingspan 35.8 m, passenger", text);
    }

    [Fact]
    public void With_no_exit_to_fall_back_on_the_line_says_none_is_mapped_on_the_same_side_500ft_on()
    {
        // "No later exit is mapped." was untrue beside a list of later exits. The exit to take if the briefed one is
        // missed is the next one on the SAME side at least 500 ft on, so "none" means none of those.
        var h6 = Exit("H6", 5334, "High-speed", "Right");
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KLAX", Runway = "25L", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand Gate 40",
            Taxiways = new[] { "H6", "H" }, DistanceMetres = 1100,
            Exit = new ExitChoice(h6, null, true), VacatingExits = new[] { h6, Exit("H8", 7110, "High-speed", "Left") },
        };
        var taxiOut = TaxiLegBriefing.UnavailableLeg("KSEA", "16L", BriefingTier.Navdata, "x");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn));

        Assert.Contains("  Expected exit: taxiway H6, high-speed, RIGHT side, 5,334 ft from the threshold. " +
                        "No later exit on the same side is mapped at least 500 ft further along.\n", text);
    }

    [Fact]
    public void An_unreachable_exit_line_does_not_call_the_runway_short_when_reachable_exits_were_set_aside()
    {
        // KPHL 17: S is comfortably reachable, but its mapped route leaves the runway on the other side from the one
        // it turns toward, so it cannot be briefed — and "this runway is short for this aircraft" would be untrue.
        var e = Exit("E", 4428, "High-speed", "Right");
        var s = Exit("S", 6025, "End", "Right");
        var k = Exit("K", 4939, "Normal", "Left");

        string Line(params LandingExit[] setAside)
        {
            var taxiIn = new TaxiLegBriefing
            {
                Icao = "KPHL", Runway = "17", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand C 22",
                Taxiways = new[] { "K" }, DistanceMetres = 1300,
                Exit = new ExitChoice(e, null, false) { ReachableExitsSetAside = setAside }, VacatingExits = new[] { e, k, s },
            };
            string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("KPHL", "17", BriefingTier.Navdata, "x"), taxiIn));
            return text.Split('\n').Single(l => l.StartsWith("  Expected exit:", StringComparison.Ordinal));
        }

        Assert.EndsWith(" No exit whose mapped route leaves the runway on the side it turns toward is comfortably reachable at 130 kt, " +
                        "so the last one that does is briefed; S is comfortably reachable, but its mapped route leaves the runway on the other side.",
                        Line(s));
        Assert.EndsWith("; S and K are comfortably reachable, but their mapped routes leave the runway on the other side.", Line(s, k));
        Assert.EndsWith(" This runway is short for this aircraft: no exit is comfortably reachable at 130 kt; the last exit is briefed.", Line());
    }

    [Fact]
    public void A_hold_with_no_named_taxiway_names_the_runway_alone()
    {
        var taxiOut = new TaxiLegBriefing
        {
            Icao = "KMEM", Runway = "36L", Tier = BriefingTier.Navdata, EndpointDescription = "current position",
            Taxiways = new[] { "M" }, DistanceMetres = 900,
            HoldShorts = new[]
            {
                new HoldShortNote("27", "", false), new HoldShortNote("18R", " ", false),
                new HoldShortNote("04", "M", false), new HoldShortNote("36L", "", true),
            },
        };
        var taxiIn = TaxiLegBriefing.UnavailableLeg("KMEM", "36L", BriefingTier.Navdata, "x");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn));

        Assert.Contains("  Hold short: runway 27 (crossing); runway 18R (crossing); runway 04 on taxiway M (crossing); runway 36L (before entering)\n", text);
    }

    [Fact]
    public void Exits_list_is_capped_at_twelve()
    {
        var exits = Enumerable.Range(1, 15).Select(i => Exit($"X{i}", 2000 + i * 500, "Normal", "Left")).ToList();
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "EGLL", Runway = "27R", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand 501",
            Taxiways = new[] { "A" }, DistanceMetres = 1000, Exit = new ExitChoice(exits[5], exits[6], true), VacatingExits = exits,
        };
        var taxiOut = TaxiLegBriefing.UnavailableLeg("EGLL", "27R", BriefingTier.Navdata, "x");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn));

        Assert.Contains("X12 (8,000 ft, left, normal), … and 3 more", text);
        Assert.DoesNotContain("X13", text);
    }

    [Fact]
    public void Distances_format_invariantly()
    {
        Assert.Equal("640 m", TaxiBriefingRenderer.FormatDistance(640.4));
        Assert.Equal("1.0 km", TaxiBriefingRenderer.FormatDistance(1000));
        Assert.Equal("2.4 km", TaxiBriefingRenderer.FormatDistance(2449));
    }

    [Fact]
    public void A_distance_that_rounds_to_a_thousand_metres_reads_in_kilometres()
    {
        Assert.Equal("999 m", TaxiBriefingRenderer.FormatDistance(999.4));
        Assert.Equal("1.0 km", TaxiBriefingRenderer.FormatDistance(999.6));     // not "1000 m"
    }
}
