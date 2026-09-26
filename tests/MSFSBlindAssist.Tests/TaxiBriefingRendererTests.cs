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
                stand: Cargo(), endpoint: "representative stand C 1 (Ramp Cargo)"));

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
        Assert.Contains("  Expected exit: taxiway B, normal, side unknown, 3,000 ft from the threshold. No later exit is mapped. This runway is short for this aircraft: no exit is comfortably reachable at 130 kt; the last exit is briefed.\n", text);
        Assert.Contains("  No runway crossings on this route.\n", text);
        Assert.Contains("wingspan 35.8 m, passenger", text);
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
}
