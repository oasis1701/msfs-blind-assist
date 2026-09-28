using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Settings;

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

    [Fact]
    public void Each_online_tier_names_its_own_source()
    {
        Assert.Equal(TaxiBriefingRenderer.OsmLabel, TaxiBriefingRenderer.TierLabel(BriefingTier.OpenStreetMap));
        Assert.Equal(TaxiBriefingRenderer.XPlaneLabel, TaxiBriefingRenderer.TierLabel(BriefingTier.XPlane));
        Assert.Equal("X-Plane airport data, planning only — taxi guidance cannot use this", TaxiBriefingRenderer.XPlaneLabel);
    }

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
            Taxiways = new[] { "N", "M", "A", "B" }, TaxiwayTurns = new string?[] { null, "left", "right", "slight right" },
            DistanceMetres = 2400,
            HoldShorts = new[] { new HoldShortNote("27", "M", false), new HoldShortNote("36L", "B", true) },
            NarrowTaxiways = new[] { new NarrowTaxiwayNote("K", 15.0, 18.0) },
        };
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KLAX", Runway = "25L", Tier = BriefingTier.Navdata,
            EndpointDescription = "SayIntentions assigned gate 52A",
            Taxiways = new[] { "AA", "E", "C" }, TaxiwayTurns = new string?[] { null, "left", "right" }, StandTurn = "right",
            DistanceMetres = 3100,
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
            "Distance unit: metres (the pilot's setting); every distance below is in metres\n" +
            "Aircraft: MD-11F (SimBrief type MD1F), size class D, wingspan 51.7 m, freighter: cargo stands preferred\n" +
            "TAXI OUT at KMEM (scenery navdata): from representative stand C 1 (Ramp Cargo, UPS) to runway 36L\n" +
            "  Taxiways: N, left onto M, right onto A, slight right onto B (2.4 km)\n" +
            "  Hold short: runway 27 on taxiway M (crossing); runway 36L on taxiway B (before entering)\n" +
            "  Taxiway width note: taxiway K is 15.0 m in the navdata, below the 18.0 m code D minimum\n" +
            "TAXI IN at KLAX (scenery navdata), landing runway 25L\n" +
            "  Expected exit: taxiway AA, high-speed, RIGHT side, 1,890 m from the threshold. Next exit if missed: AB, right side, 2,164 m\n" +
            "  Stand: SayIntentions assigned gate 52A\n" +
            "  Taxiways from the exit: AA, left onto E, right onto C, then right into the stand (3.1 km)\n" +
            "  Hold short: runway 25R on taxiway AA (crossing)\n" +
            "  Exits on 25L that get clear of the runway: AA (1,890 m, right, high-speed), AB (2,164 m, right, normal)\n" +
            "  Note: assigned gate matched by position";

        Assert.Equal(expected, TaxiBriefingRenderer.Render(FullBriefing(), DistanceUnit.Metres));
    }

    // ── Units: the pilot's distance setting, for EVERY distance in the block ──────────────────
    // Live KMEM→KATL (2026-09-26): the block gave taxi distances in km and exit distances in feet, and the AI read
    // "2.2 kilometers" beside "6,025 feet" in one section. The whole block now follows GroundDistanceUnit.

    [Fact]
    public void Feet_setting_gives_every_distance_in_feet()
    {
        const string expected =
            "TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)\n" +
            "Distance unit: feet (the pilot's setting); every distance below is in feet\n" +
            "Aircraft: MD-11F (SimBrief type MD1F), size class D, wingspan 170 ft, freighter: cargo stands preferred\n" +
            "TAXI OUT at KMEM (scenery navdata): from representative stand C 1 (Ramp Cargo, UPS) to runway 36L\n" +
            "  Taxiways: N, left onto M, right onto A, slight right onto B (7,874 ft)\n" +
            "  Hold short: runway 27 on taxiway M (crossing); runway 36L on taxiway B (before entering)\n" +
            "  Taxiway width note: taxiway K is 49 ft in the navdata, below the 59 ft code D minimum\n" +
            "TAXI IN at KLAX (scenery navdata), landing runway 25L\n" +
            "  Expected exit: taxiway AA, high-speed, RIGHT side, 6,200 ft from the threshold. Next exit if missed: AB, right side, 7,100 ft\n" +
            "  Stand: SayIntentions assigned gate 52A\n" +
            "  Taxiways from the exit: AA, left onto E, right onto C, then right into the stand (10,171 ft)\n" +
            "  Hold short: runway 25R on taxiway AA (crossing)\n" +
            "  Exits on 25L that get clear of the runway: AA (6,200 ft, right, high-speed), AB (7,100 ft, right, normal)\n" +
            "  Note: assigned gate matched by position";

        Assert.Equal(expected, TaxiBriefingRenderer.Render(FullBriefing(), DistanceUnit.Feet));
    }

    [Fact]
    public void Metres_setting_gives_every_distance_in_metres_exits_included()
    {
        string text = TaxiBriefingRenderer.Render(FullBriefing(), DistanceUnit.Metres);

        Assert.Contains("Distance unit: metres (the pilot's setting); every distance below is in metres\n", text);
        Assert.Contains("wingspan 51.7 m,", text);
        Assert.Contains("  Taxiways: N, left onto M, right onto A, slight right onto B (2.4 km)\n", text);
        Assert.Contains("taxiway K is 15.0 m in the navdata, below the 18.0 m code D minimum", text);
        Assert.Contains("  Expected exit: taxiway AA, high-speed, RIGHT side, 1,890 m from the threshold. Next exit if missed: AB, right side, 2,164 m\n", text);
        Assert.Contains("AA (1,890 m, right, high-speed), AB (2,164 m, right, normal)", text);
        Assert.DoesNotContain(" ft", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exit_that_leads_straight_to_the_stand_says_so_rather_than_naming_no_taxiways()
    {
        var z = Exit("Z", 7218, "Normal", "Right");
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "N16", Runway = "09", Tier = BriefingTier.Navdata, EndpointDescription = "SayIntentions assigned Z 1",
            Taxiways = Array.Empty<string>(), TaxiwayTurns = Array.Empty<string?>(), DistanceMetres = 0,
            HoldShorts = Array.Empty<HoldShortNote>(),
            Exit = new ExitChoice(z, null, true), VacatingExits = new[] { z },
        };
        string text = TaxiBriefingRenderer.Render(FullBriefing() with { TaxiIn = taxiIn }, DistanceUnit.Metres);
        Assert.Contains("\n  Taxiways from the exit: none (the exit leads straight to the stand)\n", text);
    }

    [Fact]
    public void The_missed_exit_separation_follows_the_setting()
    {
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KSEA", Runway = "34L", Tier = BriefingTier.Navdata, EndpointDescription = "current position",
            Taxiways = new[] { "A" }, DistanceMetres = 640,
            Exit = new ExitChoice(Exit("H6", 5334, "High-speed", "Right"), null, true),
        };
        var b = FullBriefing() with { TaxiIn = taxiIn };
        Assert.Contains("mapped at least 152 m further along.", TaxiBriefingRenderer.Render(b, DistanceUnit.Metres));
        Assert.Contains("mapped at least 500 ft further along.", TaxiBriefingRenderer.Render(b, DistanceUnit.Feet));
    }

    [Fact]
    public void Distances_in_feet_are_whole_feet_with_separators()
    {
        Assert.Equal("2,101 ft", TaxiBriefingRenderer.FormatDistance(640.4, DistanceUnit.Feet));
        Assert.Equal("7,218 ft", TaxiBriefingRenderer.FormatDistance(2200, DistanceUnit.Feet));
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
            "Distance unit: metres (the pilot's setting); every distance below is in metres\n" +
            "Aircraft: ZZZZ (SimBrief type ZZZZ), size class unknown, wingspan unknown (aircraft type not recognised), passenger\n" +
            "TAXI OUT at LOWI to runway 08: taxi route unavailable — the navigation database has no taxiways for LOWI and OpenStreetMap data is not yet available\n" +
            "TAXI IN at LOWI, landing runway 26: taxi route unavailable — no exit taxiway is mapped clear of runway 26 in this scenery\n" +
            "  Stand: representative stand C 1 (Ramp Cargo)\n" +
            "  Exits on 26 that get clear of the runway: none found";

        Assert.Equal(expected, TaxiBriefingRenderer.Render(b, DistanceUnit.Metres));
    }

    [Fact]
    public void An_unavailable_taxi_in_lists_exits_only_when_they_were_searched()
    {
        // "Exits on 22L that get clear of the runway: none found" after "no navigation database loaded" (or a timeout, or
        // a runway not in the database) reported a search that was never made. A real search that found nothing still
        // says "none found".
        var noDatabase = TaxiBriefing.Unavailable(B738, "EGLL", "27R", "KJFK", "22L", "no navigation database loaded");
        Assert.DoesNotContain("Exits on", TaxiBriefingRenderer.Render(noDatabase, DistanceUnit.Metres), StringComparison.Ordinal);

        var searched = new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("EGLL", "27R", BriefingTier.Navdata, "x"),
            TaxiLegBriefing.UnavailableLeg("KJFK", "22L", BriefingTier.Navdata,
                "no exit taxiway is mapped clear of runway 22L in this scenery", exitsSearched: true));
        Assert.EndsWith("\n  Exits on 22L that get clear of the runway: none found", TaxiBriefingRenderer.Render(searched, DistanceUnit.Metres));
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
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("KSDF", "17R", BriefingTier.Navdata, "x"), taxiIn), DistanceUnit.Feet);

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
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("OMDB", "12L", BriefingTier.Navdata, "x"), taxiIn), DistanceUnit.Feet);

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
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, leg, taxiIn), DistanceUnit.Feet);

        Assert.Contains("TAXI OUT at LOWI (OpenStreetMap, planning only — taxi guidance cannot use this): from representative stand 12 to runway 26\n", text);
        Assert.Contains("  Taxiways: A (2,100 ft)\n", text);
        Assert.Contains("  Note: stand types unknown (OpenStreetMap)\n", text);
        Assert.Contains("  Expected exit: taxiway B, normal, side unknown, 3,000 ft from the threshold. No later usable exit is mapped at least 500 ft further along. This runway is short for this aircraft: no exit is comfortably reachable at 130 kt; the briefed exit is the last one with a mapped route.\n", text);
        Assert.Contains("  No runway crossings on this route.\n", text);
        Assert.Contains("wingspan 117 ft, passenger", text);
    }

    [Fact]
    public void An_unheld_crossing_is_never_reported_as_no_crossings()
    {
        // A crossing the automatic hold-short pass could not hold is still a crossing: the block must never say
        // "No runway crossings" over one, or the AI hears a contradiction against the leg's own note.
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KATL", Runway = "08R", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand C 1",
            Taxiways = new[] { "A" }, DistanceMetres = 500,
            UnheldRunways = new[] { "08R" },
            Notes = new[] { "no hold short point could be placed for runway 08R; cross with care" },
        };
        var taxiOut = TaxiLegBriefing.UnavailableLeg("KATL", "08R", BriefingTier.Navdata, "x");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn), DistanceUnit.Feet);

        Assert.Contains("  Hold short: none could be placed; see the notes.\n", text);
        Assert.DoesNotContain("No runway crossings", text, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_exit_to_fall_back_on_the_line_says_none_is_mapped_on_the_same_side_500ft_on()
    {
        // "No later exit is mapped." was untrue beside a list of later exits. The exit to take if the briefed one is
        // missed is the next USABLE one on the SAME side at least 500 ft on, so "none" means none of those — the list
        // can still show a later same-side exit that turns more than 90° (A8 here) or was set aside.
        var h6 = Exit("H6", 5334, "High-speed", "Right");
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KLAX", Runway = "25L", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand Gate 40",
            Taxiways = new[] { "H6", "H" }, DistanceMetres = 1100,
            Exit = new ExitChoice(h6, null, true),
            VacatingExits = new[] { h6, Exit("H8", 7110, "High-speed", "Left"), Exit("A8", 7150, "End", "Right") },
        };
        var taxiOut = TaxiLegBriefing.UnavailableLeg("KSEA", "16L", BriefingTier.Navdata, "x");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn), DistanceUnit.Feet);

        Assert.Contains("  Expected exit: taxiway H6, high-speed, RIGHT side, 5,334 ft from the threshold. " +
                        "No later usable exit on the same side is mapped at least 500 ft further along.\n", text);
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
            string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, TaxiLegBriefing.UnavailableLeg("KPHL", "17", BriefingTier.Navdata, "x"), taxiIn), DistanceUnit.Feet);
            return text.Split('\n').Single(l => l.StartsWith("  Expected exit:", StringComparison.Ordinal));
        }

        // Re-review N-3: the briefed exit is the last one WITH A MAPPED ROUTE (on its own side), never simply "the last exit".
        // S also turns right, 1,597 ft further on: the line must not say there is no later exit on that side and then
        // name S as reachable — S is not usable, and the line says so in both halves.
        Assert.Equal("  Expected exit: taxiway E, high-speed, RIGHT side, 4,428 ft from the threshold. " +
                     "No later usable exit on the same side is mapped at least 500 ft further along. " +
                     "No exit whose mapped route leaves the runway on the side it turns toward is comfortably reachable at 130 kt, " +
                     "so the briefed exit is the last one whose mapped route leaves on that side; S is comfortably reachable, but its mapped route leaves the runway on the other side.",
                     Line(s));
        Assert.EndsWith("; S and K are comfortably reachable, but their mapped routes leave the runway on the other side.", Line(s, k));
        Assert.EndsWith(" This runway is short for this aircraft: no exit is comfortably reachable at 130 kt; the briefed exit is the last one with a mapped route.", Line());

        // Named like the exits list: the first twelve, then how many more.
        var many = Enumerable.Range(1, 15).Select(i => Exit($"X{i}", 5000 + i * 100, "Normal", "Right")).ToArray();
        string capped = Line(many);
        Assert.EndsWith("; X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, … and 3 more are comfortably reachable, " +
                        "but their mapped routes leave the runway on the other side.", capped);
        Assert.DoesNotContain("X13", capped, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_exit_line_says_whether_the_runway_is_short_long_enough_to_backtrack_or_of_unknown_length()
    {
        // "Short" only when true: a runway long enough to stop on, whose exits all lie behind the touchdown, is a
        // backtrack (CYYQ 15); one of unknown length is neither.
        var unknown = AircraftSizeClass.Resolve("ZZZZ", "", null);
        var e = Exit("A", 600, "Normal", "Right");

        string Line(AircraftProfile aircraft, UnreachableRunway verdict, params LandingExit[] setAside)
        {
            var taxiIn = new TaxiLegBriefing
            {
                Icao = "CYYQ", Runway = "15", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand 1",
                Taxiways = new[] { "A" }, DistanceMetres = 300,
                Exit = new ExitChoice(e, null, false) { RunwayLength = verdict, ReachableExitsSetAside = setAside },
                VacatingExits = new[] { e },
            };
            string text = TaxiBriefingRenderer.Render(new TaxiBriefing(aircraft, TaxiLegBriefing.UnavailableLeg("CYYQ", "15", BriefingTier.Navdata, "x"), taxiIn), DistanceUnit.Feet);
            return text.Split('\n').Single(l => l.StartsWith("  Expected exit:", StringComparison.Ordinal));
        }

        // Re-review N-3: "the last exit is briefed" was false — the pick is the last candidate with a mapped route.
        Assert.EndsWith(" This runway is short for this aircraft: no exit is comfortably reachable at 130 kt; the briefed exit is the last one with a mapped route.",
                        Line(B738, UnreachableRunway.Short));
        // "to the briefed exit", not "to the last exit": the briefed one is the last that ROUTES to the stand
        // (BriefingExitPicker), and a later exit with no route may exist (review M-5).
        Assert.EndsWith(" No mapped exit is comfortably reachable at 130 kt, but the runway is long enough to stop on: " +
                        "expect to backtrack on the runway to the briefed exit.",
                        Line(B738, UnreachableRunway.LongEnoughToBacktrack));
        Assert.EndsWith(" No exit is comfortably reachable at 130 kt; the briefed exit is the last one with a mapped route.",
                        Line(B738, UnreachableRunway.LengthUnknown));

        // An unrecognised type: the same shapes with the assumed speed, and never "short".
        const string assumed = "an assumed 130 kt (the aircraft type is not recognised)";
        Assert.EndsWith($" No exit is comfortably reachable at {assumed}; the briefed exit is the last one with a mapped route.", Line(unknown, UnreachableRunway.Short));
        Assert.EndsWith($" No mapped exit is comfortably reachable at {assumed}, but the runway is long enough to stop on: " +
                        "expect to backtrack on the runway to the briefed exit.",
                        Line(unknown, UnreachableRunway.LongEnoughToBacktrack));
        Assert.EndsWith($" No exit is comfortably reachable at {assumed}; the briefed exit is the last one with a mapped route.", Line(unknown, UnreachableRunway.LengthUnknown));
        Assert.DoesNotContain("short for this aircraft", Line(unknown, UnreachableRunway.Short), StringComparison.Ordinal);

        // Exits set aside for leading off the other side still win over all three.
        var s = Exit("S", 6025, "End", "Right");
        foreach (var verdict in new[] { UnreachableRunway.Short, UnreachableRunway.LongEnoughToBacktrack, UnreachableRunway.LengthUnknown })
            Assert.EndsWith("; S is comfortably reachable, but its mapped route leaves the runway on the other side.", Line(B738, verdict, s));
    }

    private static string ExitLineFor(AircraftProfile aircraft, ExitChoice choice)
    {
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "TEST", Runway = "09", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand K 1",
            Taxiways = new[] { "P" }, DistanceMetres = 300, Exit = choice, VacatingExits = new[] { choice.Exit },
        };
        string text = TaxiBriefingRenderer.Render(
            new TaxiBriefing(aircraft, TaxiLegBriefing.UnavailableLeg("TEST", "09", BriefingTier.Navdata, "x"), taxiIn), DistanceUnit.Feet);
        return text.Split('\n').Single(l => l.StartsWith("  Expected exit:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_briefed_exit_behind_the_comfortable_ones_is_a_backtrack_only_when_the_runway_is_long_enough_to_stop_on()
    {
        // Fix wave 3: "expect to backtrack" was said whatever the runway — false on a short one, and false for an exit
        // AHEAD of the comfortable ones (below).
        var p = Exit("P", 984, "Normal", "Right");
        ExitChoice Behind(UnreachableRunway verdict) =>
            new(p, null, false) { ReachableExitsHaveNoRoute = true, BriefedExitBehindReachable = true, RunwayLength = verdict };

        Assert.EndsWith(" No exit comfortably reachable at 130 kt has a mapped route to the stand: expect to stop on the runway " +
                        "and backtrack to the briefed exit, the last one before them with a mapped route.",
                        ExitLineFor(B738, Behind(UnreachableRunway.LongEnoughToBacktrack)));
        foreach (var verdict in new[] { UnreachableRunway.Short, UnreachableRunway.LengthUnknown })
            Assert.EndsWith(" No exit comfortably reachable at 130 kt has a mapped route to the stand; the briefed exit is the " +
                            "last one before them with a mapped route.", ExitLineFor(B738, Behind(verdict)));
        var c172 = AircraftSizeClass.Resolve("C172", "Cessna 172", null);
        Assert.EndsWith(" No exit comfortably reachable at 70 kt has a mapped route to the stand; the briefed exit is the " +
                        "last one before them with a mapped route.", ExitLineFor(c172, Behind(UnreachableRunway.Short)));
    }

    [Fact]
    public void A_briefed_exit_after_the_comfortable_ones_needs_firmer_braking_and_is_never_a_backtrack()
    {
        // A comfortable rapid exit with no route, and a steep routed one 300 ft after it: the aircraft reaches the
        // briefed exit rolling, it does not stop and backtrack to it.
        var s = Exit("S", 4900, "Normal", "Right");
        foreach (var verdict in new[] { UnreachableRunway.LongEnoughToBacktrack, UnreachableRunway.Short, UnreachableRunway.LengthUnknown })
        {
            string line = ExitLineFor(B738, new ExitChoice(s, null, false)
                { ReachableExitsHaveNoRoute = true, BriefedExitBehindReachable = false, RunwayLength = verdict });
            Assert.EndsWith(" No exit comfortably reachable at 130 kt has a mapped route to the stand; the briefed exit, the first " +
                            "one after them with a mapped route, needs firmer than comfortable braking.", line);
            Assert.DoesNotContain("backtrack", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void With_no_stand_known_the_unreachable_sentence_says_the_last_exit_is_briefed()
    {
        // Minor 2: with no stand nothing was routed — the picker briefs the last exit, so "the last one with a mapped
        // route" was untrue.
        var e = Exit("A", 600, "Normal", "Right");
        ExitChoice NoStand(UnreachableRunway verdict) => new(e, null, false) { RunwayLength = verdict, StandKnown = false };

        Assert.EndsWith(" This runway is short for this aircraft: no exit is comfortably reachable at 130 kt; the last exit is briefed.",
                        ExitLineFor(B738, NoStand(UnreachableRunway.Short)));
        Assert.EndsWith(" No exit is comfortably reachable at 130 kt; the last exit is briefed.",
                        ExitLineFor(B738, NoStand(UnreachableRunway.LengthUnknown)));
        Assert.EndsWith(" No mapped exit is comfortably reachable at 130 kt, but the runway is long enough to stop on: " +
                        "expect to backtrack on the runway to the briefed exit.",
                        ExitLineFor(B738, NoStand(UnreachableRunway.LongEnoughToBacktrack)));
        Assert.DoesNotContain("mapped route", ExitLineFor(B738, NoStand(UnreachableRunway.Short)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C172", "Cessna 172", IcaoCodeLetter.A, "70")]
    [InlineData("C56X", "Citation Excel", IcaoCodeLetter.B, "115")]
    public void A_code_A_or_B_aircraft_is_never_told_the_runway_is_short(string type, string name, IcaoCodeLetter letter, string kt)
    {
        // The "short" verdict is the jet touchdown re-plan's rule (2 s, then 2.0 m/s² from the touchdown speed): a C172
        // on a 1,500 ft strip was told "This runway is short for this aircraft" (review I-2). Code A and B get the neutral
        // sentence the unrecognised type gets, with their own speed.
        var aircraft = AircraftSizeClass.Resolve(type, name, null);
        Assert.Equal(letter, aircraft.CodeLetter);   // precondition
        var e = Exit("W", 656, "Normal", "Left");
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "TEST", Runway = "05", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand W 1",
            Taxiways = new[] { "W" }, DistanceMetres = 150,
            Exit = new ExitChoice(e, null, false) { RunwayLength = UnreachableRunway.Short }, VacatingExits = new[] { e },
        };
        string text = TaxiBriefingRenderer.Render(
            new TaxiBriefing(aircraft, TaxiLegBriefing.UnavailableLeg("TEST", "05", BriefingTier.Navdata, "x"), taxiIn), DistanceUnit.Feet);

        Assert.Contains($" No exit is comfortably reachable at {kt} kt; the briefed exit is the last one with a mapped route.", text);
        Assert.DoesNotContain("short for this aircraft", text);
    }

    [Fact]
    public void An_unrecognised_type_is_never_told_the_runway_is_short()
    {
        // The review's KSEA sweep briefed nearly every GA field "short for this aircraft ... at 130 kt" for an
        // unrecognised SimBrief type — an assumed speed misreported as a runway-length fact.
        var unknown = AircraftSizeClass.Resolve("ZZZZ", "", null);
        var e = Exit("E", 4428, "High-speed", "Right");
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "KPHL", Runway = "17", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand C 22",
            Taxiways = new[] { "K" }, DistanceMetres = 1300,
            Exit = new ExitChoice(e, null, false), VacatingExits = new[] { e },
        };
        var taxiOut = TaxiLegBriefing.UnavailableLeg("KPHL", "17", BriefingTier.Navdata, "x");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(unknown, taxiOut, taxiIn), DistanceUnit.Feet);

        Assert.Contains("No exit is comfortably reachable at an assumed 130 kt (the aircraft type is not recognised); the briefed exit is the last one with a mapped route.", text);
        Assert.DoesNotContain("short for this aircraft", text);
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
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn), DistanceUnit.Feet);

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
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn), DistanceUnit.Feet);

        Assert.Contains("X12 (8,000 ft, left, normal), … and 3 more", text);
        Assert.DoesNotContain("X13", text);
    }

    [Fact]
    public void Distances_format_invariantly()
    {
        Assert.Equal("640 m", TaxiBriefingRenderer.FormatDistance(640.4, DistanceUnit.Metres));
        Assert.Equal("1.0 km", TaxiBriefingRenderer.FormatDistance(1000, DistanceUnit.Metres));
        Assert.Equal("2.4 km", TaxiBriefingRenderer.FormatDistance(2449, DistanceUnit.Metres));
    }

    [Fact]
    public void A_distance_that_rounds_to_a_thousand_metres_reads_in_kilometres()
    {
        Assert.Equal("999 m", TaxiBriefingRenderer.FormatDistance(999.4, DistanceUnit.Metres));
        Assert.Equal("1.0 km", TaxiBriefingRenderer.FormatDistance(999.6, DistanceUnit.Metres));     // not "1000 m"
    }

    [Fact]
    public void A_leg_without_turns_names_its_taxiways_alone()
    {
        var b = FullBriefing();
        var bare = b with { TaxiOut = new TaxiLegBriefing
        {
            Icao = "KMEM", Runway = "36L", Tier = BriefingTier.Navdata, EndpointDescription = "current position",
            Taxiways = new[] { "N", "M" }, DistanceMetres = 900,
        } };
        Assert.Contains("  Taxiways: N, M (900 m)\n", TaxiBriefingRenderer.Render(bare, DistanceUnit.Metres));
    }

    [Fact]
    public void A_straight_change_reads_straight_ahead_onto()
    {
        var b = FullBriefing();
        var straight = b with { TaxiOut = new TaxiLegBriefing
        {
            Icao = "KMEM", Runway = "36L", Tier = BriefingTier.Navdata, EndpointDescription = "current position",
            Taxiways = new[] { "N", "M" }, TaxiwayTurns = new string?[] { null, "straight ahead" }, DistanceMetres = 900,
        } };
        Assert.Contains("  Taxiways: N, straight ahead onto M (900 m)\n", TaxiBriefingRenderer.Render(straight, DistanceUnit.Metres));
    }

    // ── the airport's taxiway names (owner, 2026-09-26) ──────────────────────────────────────

    [Fact]
    public void Each_leg_lists_its_airport_s_taxiway_names_before_its_notes()
    {
        var b = FullBriefing();
        b.TaxiOut.AirportTaxiways = new[] { "A", "B", "M", "N" };
        b.TaxiIn.AirportTaxiways = new[] { "AA", "AB", "C", "E" };
        string text = TaxiBriefingRenderer.Render(b, DistanceUnit.Metres);

        Assert.Contains("  Taxiway width note: taxiway K is 15.0 m in the navdata, below the 18.0 m code D minimum\n" +
                        "  Taxiway names at KMEM: A, B, M, N\n" +
                        "TAXI IN at KLAX", text);
        Assert.Contains("  Exits on 25L that get clear of the runway: AA (1,890 m, right, high-speed), AB (2,164 m, right, normal)\n" +
                        "  Taxiway names at KLAX: AA, AB, C, E\n" +
                        "  Note: assigned gate matched by position", text);
    }

    [Fact]
    public void Both_legs_at_the_same_airport_with_the_same_taxiway_list_says_so_once()
    {
        // A flight whose origin and destination are the same airport builds one graph — so both legs' taxiway
        // list is identical, and the taxi-in's line must not repeat it verbatim.
        var taxiOut = new TaxiLegBriefing
        {
            Icao = "TEST", Runway = "09", Tier = BriefingTier.Navdata, EndpointDescription = "current position",
            Taxiways = new[] { "N", "M" }, DistanceMetres = 900,
        };
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "TEST", Runway = "09", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand 1",
            Taxiways = new[] { "N" }, DistanceMetres = 100,
        };
        taxiOut.AirportTaxiways = new[] { "A", "B", "M", "N" };
        taxiIn.AirportTaxiways = new[] { "A", "B", "M", "N" };
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn), DistanceUnit.Metres);

        Assert.Contains("  Taxiway names at TEST: A, B, M, N\n", text);
        Assert.Contains("  Taxiway names at TEST: as listed for the taxi out", text);
        Assert.DoesNotContain("  Taxiway names at TEST: A, B, M, N\n  Taxiway names at TEST: A, B, M, N", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_taxiway_list_at_the_same_airport_is_still_listed_in_full()
    {
        // Same ICAO on both legs is not enough by itself — the lists must actually agree, or the taxi-in still
        // names its own.
        var taxiOut = new TaxiLegBriefing
        {
            Icao = "TEST", Runway = "09", Tier = BriefingTier.Navdata, EndpointDescription = "current position",
            Taxiways = new[] { "N" }, DistanceMetres = 900,
        };
        var taxiIn = new TaxiLegBriefing
        {
            Icao = "TEST", Runway = "09", Tier = BriefingTier.Navdata, EndpointDescription = "representative stand 1",
            Taxiways = new[] { "N" }, DistanceMetres = 100,
        };
        taxiOut.AirportTaxiways = new[] { "A", "B" };
        taxiIn.AirportTaxiways = new[] { "A", "B", "M", "N" };
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, taxiOut, taxiIn), DistanceUnit.Metres);

        Assert.Contains("  Taxiway names at TEST: A, B\n", text);
        Assert.EndsWith("  Taxiway names at TEST: A, B, M, N", text);
        Assert.DoesNotContain("as listed for the taxi out", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unavailable_leg_lists_its_names_too_and_a_leg_without_any_lists_none()
    {
        var withNames = TaxiLegBriefing.UnavailableLeg("KMEM", "36L", BriefingTier.Navdata, "no stand at KMEM connects to the taxiway network");
        withNames.AirportTaxiways = new[] { "J", "M2" };
        var without = TaxiLegBriefing.UnavailableLeg("KATL", "08L", BriefingTier.None, "no navigation database loaded");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, withNames, without), DistanceUnit.Feet);

        Assert.Contains("TAXI OUT at KMEM to runway 36L: taxi route unavailable — no stand at KMEM connects to the taxiway network\n" +
                        "  Taxiway names at KMEM: J, M2\n" +
                        "TAXI IN at KATL", text);
        Assert.DoesNotContain("Taxiway names at KATL", text);
    }

    [Fact]
    public void An_unavailable_leg_s_header_names_its_runway_when_it_has_one()
    {
        var withRunway = TaxiLegBriefing.UnavailableLeg("TEST", "09", BriefingTier.Navdata, "no stand at TEST connects to the taxiway network");
        var noRunway = TaxiLegBriefing.UnavailableLeg("TEST", "", BriefingTier.Navdata, "the flight plan names no departure runway");
        var taxiIn = TaxiLegBriefing.UnavailableLeg("TEST", "09", BriefingTier.Navdata, "no exit taxiway is mapped clear of runway 09 in this scenery");

        Assert.Contains("TAXI OUT at TEST to runway 09: taxi route unavailable — no stand at TEST connects",
            TaxiBriefingRenderer.Render(new TaxiBriefing(B738, withRunway, taxiIn), DistanceUnit.Metres));
        Assert.Contains("TAXI OUT at TEST: taxi route unavailable — the flight plan names no departure runway",
            TaxiBriefingRenderer.Render(new TaxiBriefing(B738, noRunway, taxiIn), DistanceUnit.Metres));
        Assert.Contains("TAXI IN at TEST, landing runway 09: taxi route unavailable — no exit taxiway",
            TaxiBriefingRenderer.Render(new TaxiBriefing(B738, withRunway, taxiIn), DistanceUnit.Metres));
    }
}
