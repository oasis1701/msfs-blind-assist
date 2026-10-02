# Route Briefing Taxi Routes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "TAXI OUT AND TAXI IN" section to the EFB's AI route briefing, computed from the pilot's own scenery data (navdata, OpenStreetMap planning-only fallback, AI-only last) and narrated by the AI alongside its real-world knowledge.

**Architecture:** Pure planning code under `MSFSBlindAssist/Navigation/Briefing/` builds each airport's `TaxiGraph` exactly as `LandingExitForm` does, routes with `TaxiRouter`, places crossing hold-shorts with `RouteRunwayCrossings.InsertRunwayHoldShorts`, picks the landing exit with the rollout planner's comfortable-braking rule and picks a stand by SayIntentions gate → category → wingspan fit → airline → central stand. A renderer turns the result into a plain-text block appended to the SimBrief flight data for that AI call; the prompt gains section 7. The EFB form wires it in with three delegates from MainForm.

**Tech Stack:** .NET 10 / C# 13, Windows Forms, xUnit (`tests/MSFSBlindAssist.Tests`), SQLite navdata via `IAirportDataProvider`.

Spec: `docs/superpowers/specs/2026-09-25-route-briefing-taxi-routes-design.md` (read it first; this plan implements it section by section).

## Global Constraints

- Build with `dotnet build MSFSBlindAssist.sln -c Debug` — NEVER the bare `.csproj` (writes to the wrong output folder). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. If the build fails with MSB3021 (exe locked), the app is running: STOP and report it — do not work around it.
- Do NOT push. Commit locally on the current branch `claude/route-describer-taxi-guidance-4c4b51`. Every commit message ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Screen-reader rule: no new announcements anywhere (status-line text only). The existing "Generating route description, please wait" / "Route description ready" announcements stay.
- Taxi guidance, Taxi Assist, the Landing Exit planner and GSX docking must not change behaviour. The only shared-code refactor is `RunwayLineupTarget` (Task 4), byte-identical.
- The OpenStreetMap graph is briefing-only: built inside the planner, never stored on any field a form or `TaxiGuidanceManager` can reach.
- Every graph build passes `ParkingSpotSource.GetNamedSpots(...)` spots, never raw `GetParkingSpots`. Route START node lookups pass `excludeBridgeOnlyStandStubs: true`; destination lookups do not.
- Navdata `ParkingSpot.Radius` is FEET; GSX radius is METRES (`Source` decides). `SimConnectManager.AircraftWingSpan` is never read — aircraft classification is SimBrief-only.
- All regexes: `RegexOptions.CultureInvariant` (+ `IgnoreCase` where needed). All number formatting/parsing: `CultureInfo.InvariantCulture`. String comparisons: `StringComparison.OrdinalIgnoreCase`, never `ToLower()`.
- Diagnostics only via `MSFSBlindAssist.Utils.Logging.Log` (`Log.Info/Warn("taxi_briefing", …)`).
- The changelog fragment needs the PR number, and opening a PR needs a push the owner has not authorised: the final task records this as an open item instead of guessing a number.
- Tests are characterization tests: literal expectations are derived by reasoning and confirmed by running; if a synthetic-airport assertion fails, first check the fixture geometry against the graph's rules (print the graph's nodes/exits in the test), and only then suspect the code under test — never weaken a rule to pass.

## File structure

Create:
- `MSFSBlindAssist/Navigation/Briefing/AircraftSizeClass.cs` — `IcaoCodeLetter`, `AircraftProfile`, `AircraftSizeClass` (type table, freighter detection, class constants).
- `MSFSBlindAssist/Navigation/Briefing/BriefingExitPicker.cs` — `ExitChoice`, `BriefingExitPicker`.
- `MSFSBlindAssist/Navigation/RunwayLineupTarget.cs` — the lineup-entry-node maths shared with `TaxiAssistForm`.
- `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs` — `BriefingTier`, `OwnPosition`, `SayIntentionsGateHint`, `StandChoiceSource`, `StandChoice`, `TaxiBriefingRequest`, `HoldShortNote`, `NarrowTaxiwayNote`, `TaxiLegBriefing`, `TaxiBriefing`, `GraphBundle`, `RouteBriefingDependencies`.
- `MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs` — `BriefingStandPicker`, `SayIntentionsArrivalGate`.
- `MSFSBlindAssist/Navigation/Briefing/OsmPlanningGraph.cs` — tier-2 graph builder.
- `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs` — the text block.
- `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs` — `PlanTaxiOut`/`PlanTaxiIn` (pure) + `PlanAsync`.
- `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingGraphSource.cs` — tier selection per airport.
- Tests: `AircraftSizeClassTests.cs`, `BriefingExitPickerTests.cs`, `RunwayLineupTargetTests.cs`, `BriefingStandPickerTests.cs`, `OsmPlanningGraphTests.cs`, `TaxiBriefingRendererTests.cs`, `TaxiBriefingFixture.cs`, `TaxiBriefingPlannerTests.cs`, `TaxiBriefingGraphSourceTests.cs`, `RouteDescriptionPromptTests.cs`, `GeminiResponseTests.cs`, `SimBriefFlightPlanFieldsTests.cs`, `AugmentingProviderOnlineDataTests.cs` — all under `tests/MSFSBlindAssist.Tests/`.

Modify:
- `MSFSBlindAssist/Navigation/FlightPlan.cs`, `MSFSBlindAssist/Services/SimBriefService.cs` (Task 1).
- `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (Task 4).
- `MSFSBlindAssist/Services/GeminiService.cs` (Task 5).
- `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs` (Task 6).
- `MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs`, `MSFSBlindAssist/MainForm.Dialogs.cs` (Task 13).
- `docs/gemini.md`, `docs/taxi-guidance.md`, `CLAUDE.md` (Task 14).

## Dependency waves (for parallel dispatch)

- Wave 1 (independent): Task 1, Task 2, Task 3, Task 4, Task 5, Task 6.
- Wave 2: Task 7 (needs 2, 3).
- Wave 3 (independent of each other): Task 8 (needs 7), Task 9 (needs 7), Task 11 (needs 7).
- Wave 4: Task 10 (needs 4, 7, 8).
- Wave 5: Task 12 (needs 9, 10), then Task 13 (needs 1, 5, 11, 12), then Task 14.

---

### Task 1: SimBrief aircraft/airline fields on `FlightPlan`

**Files:**
- Modify: `MSFSBlindAssist/Navigation/FlightPlan.cs` (after `ExtractedFlightData`, ~line 29)
- Modify: `MSFSBlindAssist/Services/SimBriefService.cs` (`ParseSimBriefXML`, ~line 94)
- Test: `tests/MSFSBlindAssist.Tests/SimBriefFlightPlanFieldsTests.cs`

**Interfaces:**
- Produces: `FlightPlan.AircraftTypeIcao : string`, `FlightPlan.AircraftName : string`, `FlightPlan.AircraftMaxPassengers : int?`, `FlightPlan.AirlineIcao : string`; `SimBriefService.ParseSimBriefXML(string xmlContent, string username)` becomes `internal`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MSFSBlindAssist.Tests/SimBriefFlightPlanFieldsTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

// The route briefing classifies the aircraft from the SimBrief OFP only (owner's choice), so the
// four fields it needs must survive the parse. ParseSimBriefXML is internal for this test.
public class SimBriefFlightPlanFieldsTests
{
    private const string Ofp = """
        <OFP>
          <general><icao_airline>UPS</icao_airline><route>DCT</route></general>
          <aircraft><icaocode>MD1F</icaocode><name>MD-11F</name><max_passengers>0</max_passengers></aircraft>
          <origin><icao_code>KSDF</icao_code><plan_rwy>17R</plan_rwy></origin>
          <destination><icao_code>KLAX</icao_code><plan_rwy>25L</plan_rwy></destination>
          <navlog></navlog>
        </OFP>
        """;

    [Fact]
    public void Aircraft_and_airline_fields_are_read_from_the_ofp()
    {
        var plan = new SimBriefService().ParseSimBriefXML(Ofp, "user");

        Assert.Equal("MD1F", plan.AircraftTypeIcao);
        Assert.Equal("MD-11F", plan.AircraftName);
        Assert.Equal(0, plan.AircraftMaxPassengers);
        Assert.Equal("UPS", plan.AirlineIcao);
        Assert.Equal("KSDF", plan.DepartureICAO);
        Assert.Equal("25L", plan.ArrivalRunway);
    }

    [Fact]
    public void Missing_max_passengers_reads_as_null_and_missing_aircraft_as_empty()
    {
        const string ofp = """
            <OFP>
              <general></general>
              <origin><icao_code>EGLL</icao_code></origin>
              <destination><icao_code>KJFK</icao_code></destination>
            </OFP>
            """;
        var plan = new SimBriefService().ParseSimBriefXML(ofp, "user");

        Assert.Null(plan.AircraftMaxPassengers);
        Assert.Equal("", plan.AircraftTypeIcao);
        Assert.Equal("", plan.AircraftName);
        Assert.Equal("", plan.AirlineIcao);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SimBriefFlightPlanFieldsTests"`
Expected: build error — `ParseSimBriefXML` inaccessible / `AircraftTypeIcao` not defined.

- [ ] **Step 3: Add the fields and parse them**

In `FlightPlan.cs`, after `public string ExtractedFlightData { get; set; } = "";`:

```csharp
    // Aircraft and airline exactly as SimBrief filed them (aircraft/icaocode, aircraft/name,
    // aircraft/max_passengers, general/icao_airline). The route briefing's taxi section classifies
    // the aircraft from these ALONE (owner's choice: SimBrief-only, never the loaded aircraft).
    // Empty / null when the OFP did not carry them.
    public string AircraftTypeIcao { get; set; } = "";
    public string AircraftName { get; set; } = "";
    public int? AircraftMaxPassengers { get; set; }
    public string AirlineIcao { get; set; } = "";
```

In `SimBriefService.cs`, change `private FlightPlan ParseSimBriefXML(` to `internal FlightPlan ParseSimBriefXML(` and, right after the `FlightPlan flightPlan = new FlightPlan { ... };` initializer, add:

```csharp
        // Aircraft + airline for the route briefing's taxi section (see FlightPlan for why these
        // four and no others). max_passengers is a freighter signal: SimBrief publishes 0 for one.
        XmlNode? aircraftNode = doc.SelectSingleNode("//aircraft");
        XmlNode? generalNode = doc.SelectSingleNode("//general");
        flightPlan.AircraftTypeIcao = GetNodeValue(aircraftNode, "icaocode") ?? "";
        flightPlan.AircraftName = GetNodeValue(aircraftNode, "name") ?? "";
        flightPlan.AircraftMaxPassengers = int.TryParse(GetNodeValue(aircraftNode, "max_passengers"),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
            out int maxPax) ? maxPax : null;
        flightPlan.AirlineIcao = GetNodeValue(generalNode, "icao_airline") ?? "";
```

(`GetNodeValue(XmlNode? parentNode, string childNodeName)` already accepts a null parent — check line ~412.)

- [ ] **Step 4: Run the test to verify it passes**

Run the same command. Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/FlightPlan.cs MSFSBlindAssist/Services/SimBriefService.cs tests/MSFSBlindAssist.Tests/SimBriefFlightPlanFieldsTests.cs
git commit -m "feat(briefing): keep SimBrief aircraft type, name, max passengers and airline on the flight plan"
```

---

### Task 2: `AircraftSizeClass`

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/AircraftSizeClass.cs`
- Test: `tests/MSFSBlindAssist.Tests/AircraftSizeClassTests.cs`

**Interfaces:**
- Produces (namespace `MSFSBlindAssist.Navigation.Briefing`):
  - `public enum IcaoCodeLetter { Unknown, A, B, C, D, E, F }`
  - `public sealed record AircraftProfile(string TypeCode, string DisplayName, double? WingspanMetres, IcaoCodeLetter CodeLetter, bool IsFreighter, double TouchdownSpeedKts)`
  - `public static class AircraftSizeClass` with `Resolve(string? typeCode, string? name, int? maxPassengers) : AircraftProfile`, `LetterForWingspan(double metres) : IcaoCodeLetter`, `TouchdownSpeedKts(IcaoCodeLetter) : double`, `MinTaxiwayWidthMetres(IcaoCodeLetter) : double`, `LooksLikeFreighter(string? typeCode, string? name, int? maxPassengers) : bool`, `TryGetWingspanMetres(string? typeCode, out double metres) : bool`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/AircraftSizeClassTests.cs
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

public class AircraftSizeClassTests
{
    [Fact]
    public void A388_is_code_F_with_its_wingspan_and_a_heavy_touchdown_speed()
    {
        var p = AircraftSizeClass.Resolve("A388", "Airbus A380-800", 500);

        Assert.Equal(IcaoCodeLetter.F, p.CodeLetter);
        Assert.Equal(79.75, p.WingspanMetres!.Value, 2);
        Assert.Equal(140.0, p.TouchdownSpeedKts);
        Assert.False(p.IsFreighter);
        Assert.Equal("Airbus A380-800", p.DisplayName);
        Assert.Equal(25.0, AircraftSizeClass.MinTaxiwayWidthMetres(p.CodeLetter));
    }

    [Fact]
    public void B738_is_code_C()
    {
        var p = AircraftSizeClass.Resolve("b738", "Boeing 737-800", 189);
        Assert.Equal(IcaoCodeLetter.C, p.CodeLetter);
        Assert.Equal("B738", p.TypeCode);
        Assert.Equal(130.0, p.TouchdownSpeedKts);
        Assert.Equal(15.0, AircraftSizeClass.MinTaxiwayWidthMetres(IcaoCodeLetter.C));
    }

    [Theory]
    [InlineData("MD1F", "MD-11F", 0)]
    [InlineData("B77L", "Boeing 777F", 0)]
    [InlineData("B748", "Boeing 747-8F", null)]
    [InlineData("MD11", "McDonnell Douglas MD-11F", null)]
    [InlineData("B738", "Boeing 737-800BCF", null)]
    [InlineData("B752", "Boeing 757-200PF", null)]
    [InlineData("A332", "Airbus A330-200F", null)]
    [InlineData("B763", "Boeing 767-300 Freighter", null)]
    [InlineData("A320", "Airbus A320", 0)]
    public void Freighters_are_recognised(string code, string name, int? maxPax)
        => Assert.True(AircraftSizeClass.LooksLikeFreighter(code, name, maxPax));

    [Theory]
    [InlineData("B77L", "Boeing 777-200LR", 301)]
    [InlineData("A320", "Airbus A320-200", 180)]
    [InlineData("F100", "Fokker 100", 100)]
    [InlineData("B738", "Boeing 737-800", null)]
    public void Passenger_aircraft_are_not_freighters(string code, string name, int? maxPax)
        => Assert.False(AircraftSizeClass.LooksLikeFreighter(code, name, maxPax));

    [Fact]
    public void Unknown_type_has_no_wingspan_and_the_unknown_letter()
    {
        var p = AircraftSizeClass.Resolve("ZZZZ", "", null);
        Assert.Null(p.WingspanMetres);
        Assert.Equal(IcaoCodeLetter.Unknown, p.CodeLetter);
        Assert.Equal(130.0, p.TouchdownSpeedKts);
        Assert.Equal(0.0, AircraftSizeClass.MinTaxiwayWidthMetres(IcaoCodeLetter.Unknown));
        Assert.Equal("ZZZZ", p.DisplayName);
    }

    [Fact]
    public void Blank_type_and_name_still_yield_a_profile()
    {
        var p = AircraftSizeClass.Resolve(null, null, null);
        Assert.Equal("", p.TypeCode);
        Assert.Equal("unknown aircraft", p.DisplayName);
        Assert.Equal(IcaoCodeLetter.Unknown, p.CodeLetter);
    }

    [Theory]
    [InlineData(14.99, IcaoCodeLetter.A)]
    [InlineData(15.0, IcaoCodeLetter.B)]
    [InlineData(35.99, IcaoCodeLetter.C)]
    [InlineData(36.0, IcaoCodeLetter.D)]
    [InlineData(51.99, IcaoCodeLetter.D)]
    [InlineData(52.0, IcaoCodeLetter.E)]
    [InlineData(64.99, IcaoCodeLetter.E)]
    [InlineData(65.0, IcaoCodeLetter.F)]
    [InlineData(88.4, IcaoCodeLetter.F)]
    public void Letter_boundaries_follow_annex_14(double metres, IcaoCodeLetter expected)
        => Assert.Equal(expected, AircraftSizeClass.LetterForWingspan(metres));

    [Theory]
    [InlineData(IcaoCodeLetter.A, 70.0)]
    [InlineData(IcaoCodeLetter.B, 115.0)]
    [InlineData(IcaoCodeLetter.C, 130.0)]
    [InlineData(IcaoCodeLetter.D, 135.0)]
    [InlineData(IcaoCodeLetter.E, 140.0)]
    [InlineData(IcaoCodeLetter.F, 140.0)]
    public void Touchdown_speed_per_letter(IcaoCodeLetter letter, double kts)
        => Assert.Equal(kts, AircraftSizeClass.TouchdownSpeedKts(letter));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AircraftSizeClassTests"`
Expected: build error — namespace `MSFSBlindAssist.Navigation.Briefing` not found.

- [ ] **Step 3: Implement**

```csharp
// MSFSBlindAssist/Navigation/Briefing/AircraftSizeClass.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>ICAO Annex 14 aerodrome reference code letter, by wingspan.</summary>
public enum IcaoCodeLetter { Unknown, A, B, C, D, E, F }

/// <summary>
/// What the route briefing knows about the aircraft, from the SimBrief OFP ALONE (owner's
/// choice — never the loaded aircraft definition, never the sim's WING SPAN). A type the table
/// does not know keeps a null wingspan and the Unknown letter; nothing is guessed.
/// </summary>
public sealed record AircraftProfile(
    string TypeCode, string DisplayName, double? WingspanMetres,
    IcaoCodeLetter CodeLetter, bool IsFreighter, double TouchdownSpeedKts);

public static class AircraftSizeClass
{
    /// <summary>Wingspan in metres by ICAO/SimBrief type code. A missing type is a one-line addition.</summary>
    private static readonly Dictionary<string, double> WingspanByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A388"] = 79.75, ["A35K"] = 64.75, ["A359"] = 64.75, ["A346"] = 63.45, ["A345"] = 63.45,
        ["A343"] = 60.3, ["A342"] = 60.3, ["A339"] = 64.0, ["A338"] = 64.0, ["A333"] = 60.3, ["A332"] = 60.3,
        ["A33F"] = 60.3, ["A306"] = 44.84, ["A3ST"] = 44.84, ["A310"] = 43.9,
        ["A21N"] = 35.8, ["A20N"] = 35.8, ["A19N"] = 35.8, ["A321"] = 34.1, ["A320"] = 34.1, ["A319"] = 34.1, ["A318"] = 34.1,
        ["BCS1"] = 35.1, ["BCS3"] = 35.1, ["C919"] = 35.8,
        ["B748"] = 68.4, ["B74F"] = 68.4, ["B744"] = 64.44, ["B741"] = 59.64, ["B742"] = 59.64, ["B743"] = 59.64,
        ["B77W"] = 64.8, ["B77L"] = 64.8, ["B77F"] = 64.8, ["B773"] = 60.93, ["B772"] = 60.93,
        ["B78X"] = 60.12, ["B789"] = 60.12, ["B788"] = 60.12,
        ["B764"] = 51.92, ["B763"] = 47.57, ["B762"] = 47.57, ["B76F"] = 47.57, ["B753"] = 38.05, ["B752"] = 38.05,
        ["B3XM"] = 35.92, ["B39M"] = 35.92, ["B38M"] = 35.92, ["B37M"] = 35.92,
        ["B739"] = 35.79, ["B738"] = 35.79, ["B737"] = 35.79, ["B736"] = 35.79,
        ["B735"] = 28.88, ["B734"] = 28.88, ["B733"] = 28.88, ["B732"] = 28.35, ["B722"] = 32.92, ["B712"] = 28.45,
        ["MD11"] = 51.7, ["MD1F"] = 51.7, ["DC10"] = 50.4, ["DC1F"] = 50.4,
        ["MD90"] = 32.87, ["MD88"] = 32.87, ["MD83"] = 32.87, ["MD82"] = 32.87, ["L101"] = 47.34,
        ["E295"] = 35.12, ["E290"] = 33.72, ["E195"] = 28.72, ["E190"] = 28.72, ["E75L"] = 28.65,
        ["E175"] = 26.0, ["E75S"] = 26.0, ["E170"] = 26.0,
        ["CRJX"] = 26.18, ["CRJ9"] = 24.85, ["CRJ7"] = 23.25, ["CRJ2"] = 21.21, ["DH8D"] = 28.42,
        ["AT76"] = 27.05, ["AT75"] = 27.05, ["AT72"] = 27.05, ["AT46"] = 24.57, ["AT45"] = 24.57, ["AT43"] = 24.57,
        ["B463"] = 26.34, ["B462"] = 26.34, ["B461"] = 26.34, ["F100"] = 28.08, ["F70"] = 28.08, ["SU95"] = 27.8,
        ["A124"] = 73.3, ["A225"] = 88.4, ["IL96"] = 60.1, ["C130"] = 40.4, ["C30J"] = 40.4, ["A400"] = 42.4, ["C17"] = 51.75,
        ["GL7T"] = 31.7, ["GLF6"] = 30.36, ["F900"] = 19.33, ["B350"] = 17.65, ["C56X"] = 17.17, ["C25C"] = 16.26,
        ["PC12"] = 16.28, ["C208"] = 15.88, ["TBM9"] = 12.68, ["C172"] = 11.0,
    };

    /// <summary>SimBrief-style codes that are freighters by definition (belt-and-braces beside the name test).</summary>
    private static readonly HashSet<string> FreighterTypes = new(StringComparer.OrdinalIgnoreCase)
        { "MD1F", "B74F", "B76F", "B77F", "A33F", "DC1F" };

    // "Boeing 777F", "747-8F", "MD-11F", "737-800BCF", "757-200PF", "A330-200F", "767-300 Freighter".
    // The letter group must follow a digit, dash or space so "Fokker 100" and "F100" never match.
    private static readonly Regex FreighterName = new(
        @"(?:\d|-|\s)(?:F|BCF|BDSF|SF|PF|PCF|ERF|LRF)\b|\bfreighter\b|\bcargo\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static AircraftProfile Resolve(string? typeCode, string? name, int? maxPassengers)
    {
        string code = (typeCode ?? "").Trim().ToUpperInvariant();
        string display = !string.IsNullOrWhiteSpace(name) ? name.Trim()
                       : code.Length > 0 ? code : "unknown aircraft";
        double? span = TryGetWingspanMetres(code, out double metres) ? metres : null;
        var letter = span is double m ? LetterForWingspan(m) : IcaoCodeLetter.Unknown;
        return new AircraftProfile(code, display, span, letter,
            LooksLikeFreighter(code, name, maxPassengers), TouchdownSpeedKts(letter));
    }

    public static bool TryGetWingspanMetres(string? typeCode, out double metres)
    {
        metres = 0;
        return !string.IsNullOrWhiteSpace(typeCode) && WingspanByType.TryGetValue(typeCode.Trim(), out metres);
    }

    /// <summary>Annex 14 code letter: A &lt; 15 m, B &lt; 24, C &lt; 36, D &lt; 52, E &lt; 65, F otherwise.</summary>
    public static IcaoCodeLetter LetterForWingspan(double metres) => metres switch
    {
        < 15.0 => IcaoCodeLetter.A,
        < 24.0 => IcaoCodeLetter.B,
        < 36.0 => IcaoCodeLetter.C,
        < 52.0 => IcaoCodeLetter.D,
        < 65.0 => IcaoCodeLetter.E,
        _ => IcaoCodeLetter.F,
    };

    /// <summary>Typical touchdown ground speed used to judge which landing exit is comfortably reachable.</summary>
    public static double TouchdownSpeedKts(IcaoCodeLetter letter) => letter switch
    {
        IcaoCodeLetter.A => 70.0,
        IcaoCodeLetter.B => 115.0,
        IcaoCodeLetter.C => 130.0,
        IcaoCodeLetter.D => 135.0,
        IcaoCodeLetter.E => 140.0,
        IcaoCodeLetter.F => 140.0,
        _ => 130.0,
    };

    /// <summary>Annex 14 minimum straight taxiway width per code letter; 0 = no advisory for an unknown type.</summary>
    public static double MinTaxiwayWidthMetres(IcaoCodeLetter letter) => letter switch
    {
        IcaoCodeLetter.A => 7.5,
        IcaoCodeLetter.B => 10.5,
        IcaoCodeLetter.C => 15.0,
        IcaoCodeLetter.D => 18.0,
        IcaoCodeLetter.E => 23.0,
        IcaoCodeLetter.F => 25.0,
        _ => 0.0,
    };

    public static bool LooksLikeFreighter(string? typeCode, string? name, int? maxPassengers)
    {
        if (maxPassengers == 0) return true;
        if (!string.IsNullOrWhiteSpace(typeCode) && FreighterTypes.Contains(typeCode.Trim())) return true;
        return !string.IsNullOrWhiteSpace(name) && FreighterName.IsMatch(name);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Same command. Expected: all pass (the `Theory` rows count individually).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/AircraftSizeClass.cs tests/MSFSBlindAssist.Tests/AircraftSizeClassTests.cs
git commit -m "feat(briefing): classify the SimBrief aircraft by wingspan, code letter and freighter"
```

---

### Task 3: `BriefingExitPicker`

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/BriefingExitPicker.cs`
- Test: `tests/MSFSBlindAssist.Tests/BriefingExitPickerTests.cs`

**Interfaces:**
- Consumes: `LandingExit` (`MSFSBlindAssist.Navigation`: `TaxiwayName`, `ExitAngleDegrees`, `ExitType` "High-speed"/"Normal"/"End", `ExitSide`, `DistanceFromThresholdFeet`, `DistanceFromTouchdownFeet`, `VacatesRunway`), `RolloutExitGate.ComfortableExitLeadFeet(double gsKts, double exitAngleDeg)`, `RolloutExitGate.MaxUsableExitTurnDeg` (90).
- Produces: `public sealed record ExitChoice(LandingExit Exit, LandingExit? NextExit, bool ComfortablyReachable)`; `public static class BriefingExitPicker { public const double HighSpeedPreferenceFeet = 1500.0; public static ExitChoice? Pick(IReadOnlyList<LandingExit> exitsSortedByThreshold, double touchdownSpeedKts); }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/BriefingExitPickerTests.cs
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

// Exit distances: ComfortableExitLeadFeet(140 kt, 90°) ≈ 4,641 ft and (140, 30°) ≈ 4,185 ft;
// (130, 90°) ≈ 4,021 ft; (115, 90°) ≈ 3,173 ft. DistanceFromTouchdownFeet = threshold − 1,000.
public class BriefingExitPickerTests
{
    private static LandingExit Exit(string name, double thresholdFt, double angle = 90, bool vacates = true, string side = "Left") => new()
    {
        TaxiwayName = name,
        DistanceFromThresholdFeet = thresholdFt,
        DistanceFromTouchdownFeet = thresholdFt - 1000.0,
        ExitAngleDegrees = angle,
        ExitType = angle <= 50 ? "High-speed" : angle > 110 ? "End" : "Normal",
        ExitSide = side,
        VacatesRunway = vacates,
    };

    [Fact]
    public void A_heavy_skips_an_exit_it_cannot_slow_down_for()
    {
        var a = Exit("A", 3500);   // 2,500 ft from touchdown — needs 4,641 at 140 kt
        var b = Exit("B", 6500);   // 5,500 ft — reachable
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);
        Assert.True(choice.ComfortablyReachable);
    }

    [Fact]
    public void A_lighter_aircraft_takes_the_earlier_exit()
    {
        var a = Exit("A", 4500);   // 3,500 ft from touchdown — enough at 115 kt (3,173), not at 140 (4,641)
        var b = Exit("B", 6500);
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, b }, 115.0)!.Exit);
        Assert.Same(b, BriefingExitPicker.Pick(new[] { a, b }, 140.0)!.Exit);
    }

    [Fact]
    public void A_high_speed_exit_within_1500ft_beyond_the_first_reachable_wins()
    {
        var a = Exit("A", 6000);            // normal, reachable
        var h = Exit("H", 7200, angle: 30); // high-speed, 1,200 ft further
        var c = Exit("C", 9000);
        var choice = BriefingExitPicker.Pick(new[] { a, h, c }, 140.0)!;

        Assert.Same(h, choice.Exit);
        Assert.Same(c, choice.NextExit);
    }

    [Fact]
    public void A_high_speed_exit_further_than_1500ft_does_not_win()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7800, angle: 30); // 1,800 ft further
        var choice = BriefingExitPicker.Pick(new[] { a, h }, 140.0)!;

        Assert.Same(a, choice.Exit);
        Assert.Same(h, choice.NextExit);
    }

    [Fact]
    public void Non_vacating_and_end_exits_are_not_candidates_when_a_usable_one_exists()
    {
        var a = Exit("A", 6000, vacates: false);
        var b = Exit("B", 7000);
        var e = Exit("E", 8000, angle: 130);
        var choice = BriefingExitPicker.Pick(new[] { a, b, e }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);   // E is not a candidate, so nothing follows B
    }

    [Fact]
    public void Only_end_exits_vacating_falls_back_to_them()
    {
        var e = Exit("E", 8000, angle: 130);
        Assert.Same(e, BriefingExitPicker.Pick(new[] { e }, 140.0)!.Exit);
    }

    [Fact]
    public void Nothing_reachable_picks_the_furthest_and_flags_it()
    {
        var a = Exit("A", 2000);
        var b = Exit("B", 3000);
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.False(choice.ComfortablyReachable);
        Assert.Null(choice.NextExit);
    }

    [Fact]
    public void No_vacating_exit_returns_null()
    {
        Assert.Null(BriefingExitPicker.Pick(Array.Empty<LandingExit>(), 140.0));
        Assert.Null(BriefingExitPicker.Pick(new[] { Exit("A", 6000, vacates: false) }, 140.0));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BriefingExitPickerTests"`
Expected: build error — `BriefingExitPicker` not found.

- [ ] **Step 3: Implement**

```csharp
// MSFSBlindAssist/Navigation/Briefing/BriefingExitPicker.cs
namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>The exit the briefing expects the aircraft to take, and the next one if it is missed.</summary>
public sealed record ExitChoice(LandingExit Exit, LandingExit? NextExit, bool ComfortablyReachable);

/// <summary>
/// Which landing exit to brief. Candidates vacate the runway and turn no more than
/// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>; the first one comfortably reachable at the
/// aircraft's typical touchdown speed (<see cref="RolloutExitGate.ComfortableExitLeadFeet"/>, the
/// touchdown re-plan's own rule) is chosen, except that a High-speed exit no more than
/// <see cref="HighSpeedPreferenceFeet"/> further along wins over a Normal one. With nothing
/// reachable the furthest candidate is briefed and flagged.
/// </summary>
public static class BriefingExitPicker
{
    public const double HighSpeedPreferenceFeet = 1500.0;

    public static ExitChoice? Pick(IReadOnlyList<LandingExit> exitsSortedByThreshold, double touchdownSpeedKts)
    {
        if (exitsSortedByThreshold == null || exitsSortedByThreshold.Count == 0) return null;

        var candidates = exitsSortedByThreshold
            .Where(e => e.VacatesRunway && e.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg)
            .OrderBy(e => e.DistanceFromThresholdFeet)
            .ToList();
        if (candidates.Count == 0)
            candidates = exitsSortedByThreshold.Where(e => e.VacatesRunway)
                .OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        if (candidates.Count == 0) return null;

        var reachable = candidates
            .Where(e => e.DistanceFromTouchdownFeet >=
                        RolloutExitGate.ComfortableExitLeadFeet(touchdownSpeedKts, e.ExitAngleDegrees))
            .ToList();
        if (reachable.Count == 0)
            return new ExitChoice(candidates[^1], null, ComfortablyReachable: false);

        var chosen = reachable[0];
        if (!IsHighSpeed(chosen))
        {
            var rapid = reachable.FirstOrDefault(e => IsHighSpeed(e) &&
                e.DistanceFromThresholdFeet - chosen.DistanceFromThresholdFeet <= HighSpeedPreferenceFeet);
            if (rapid != null) chosen = rapid;
        }

        int index = candidates.IndexOf(chosen);
        var next = index >= 0 && index + 1 < candidates.Count ? candidates[index + 1] : null;
        return new ExitChoice(chosen, next, ComfortablyReachable: true);
    }

    private static bool IsHighSpeed(LandingExit e) =>
        string.Equals(e.ExitType, "High-speed", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run to verify it passes**

Same command. Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/BriefingExitPicker.cs tests/MSFSBlindAssist.Tests/BriefingExitPickerTests.cs
git commit -m "feat(briefing): pick the landing exit a given aircraft can comfortably make"
```

---
### Task 4: `RunwayLineupTarget` (shared with `TaxiAssistForm`)

**Files:**
- Create: `MSFSBlindAssist/Navigation/RunwayLineupTarget.cs`
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (`PopulateDestinations`, the block from `double lineupLat;` through `var nearNode = _graph.FindRunwayLineupEntryNode(...)`, ~lines 2310–2358)
- Test: `tests/MSFSBlindAssist.Tests/RunwayLineupTargetTests.cs`

**Interfaces:**
- Consumes: `TaxiGraph.PickFullLengthStart(IEnumerable<StartPosition>, thrLat, thrLon, farLat, farLon)`, `TaxiGraph.SnapStartToRunwayCenterline(startLat, startLon, thrLat, thrLon, farLat, farLon)`, `TaxiGraph.FindRunwayLineupEntryNode(lineupLat, lineupLon, thrLat, thrLon, farLat, farLon, halfWidthMeters, maxAcceptableCrossM, double? aircraftLat, double? aircraftLon)`, `Services.TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M` (internal const 120).
- Produces: `public static class RunwayLineupTarget { public sealed record Result(double LineupLat, double LineupLon, TaxiNode? EntryNode); public const double DefaultRunwayWidthFeet = 150.0; public static Result Resolve(TaxiGraph graph, Runway rwy, IEnumerable<StartPosition>? startsForRunway, double? anchorLat, double? anchorLon); }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MSFSBlindAssist.Tests/RunwayLineupTargetTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

// Characterization of the lineup-entry maths lifted out of TaxiAssistForm.PopulateDestinations:
// start row → snapped onto the centerline → FindRunwayLineupEntryNode. Runway 09 runs east along
// north=0 from east 0 to 1,000 m; taxiway A1 meets it at east 200 and leaves north.
public class RunwayLineupTargetTests
{
    private static double Lat(double n) => RunwayFixture.Lat(n);
    private static double Lon(double e) => RunwayFixture.Lon(e);

    private static TaxiGraph Graph() => TaxiGraph.Build(
        new List<TaxiPath>
        {
            new() { Name = "A1", Type = "T", Width = 98, StartType = "HS", EndType = "N",
                    StartLat = Lat(0), StartLon = Lon(200), EndLat = Lat(100), EndLon = Lon(200) },
            new() { Name = "A", Type = "T", Width = 98, StartType = "N", EndType = "N",
                    StartLat = Lat(100), StartLon = Lon(0), EndLat = Lat(100), EndLon = Lon(200) },
        },
        new List<ParkingSpot>(), new List<StartPosition>());

    private static Runway Runway09() => new()
    {
        RunwayID = "09", StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(1000),
        Heading = 90, Length = 1000 / 0.3048, Width = 150,
    };

    [Fact]
    public void Lineup_point_is_the_start_row_snapped_onto_the_centerline()
    {
        // The row sits 8 m north of the axis at east 60; the snap pulls it back onto north = 0.
        var rows = new[] { new StartPosition { RunwayName = "09", Latitude = Lat(8), Longitude = Lon(60), Heading = 90 } };

        var r = RunwayLineupTarget.Resolve(Graph(), Runway09(), rows, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(60), r.LineupLon, 9);
        Assert.NotNull(r.EntryNode);
        Assert.Equal(Lon(200), r.EntryNode!.Longitude, 9);   // A1's runway-side node
    }

    [Fact]
    public void Without_a_start_row_the_pavement_start_is_the_lineup_point()
    {
        var r = RunwayLineupTarget.Resolve(Graph(), Runway09(), null, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(0), r.LineupLon, 9);
        Assert.NotNull(r.EntryNode);
        Assert.Equal(Lon(200), r.EntryNode!.Longitude, 9);
    }

    [Fact]
    public void Half_width_falls_back_to_150ft_when_the_runway_has_no_width()
    {
        var rwy = Runway09();
        rwy.Width = 0;
        var r = RunwayLineupTarget.Resolve(Graph(), rwy, null, null, null);
        Assert.NotNull(r.EntryNode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RunwayLineupTargetTests"`
Expected: build error — `RunwayLineupTarget` not found.

- [ ] **Step 3: Create the helper (move the form's comments with it)**

```csharp
// MSFSBlindAssist/Navigation/RunwayLineupTarget.cs
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Where a departure on a runway BEGINS and which graph node a route to it must end on — the
/// ONE owner of that maths, shared by TaxiAssistForm's destination list and the route briefing's
/// taxi-out plan so the two can never disagree about where a runway starts.
///
/// <para>The `start` table is navdatareader's curated "where MSFS spawns an aircraft if you
/// select runway X" value, which correctly accounts for displaced thresholds. It is ALSO the
/// source TaxiGraph builds RunwayCenterlines from, and TakeoffAssist's cross-track math reads
/// those centerlines; anchoring the lineup target here keeps taxi-lineup and TakeoffAssist on
/// the same physical position. The row is trusted for WHERE ALONG the runway the departure
/// begins, but pulled back onto the runway_end centerline first (EGKK's rows sit ~110 m to the
/// SIDE of their own runway). All rows per runway end are offered: TaxiGraph.PickFullLengthStart
/// picks the full-length (furthest-back) one and REJECTS a row past 40 % of the runway. When no
/// usable row exists the physical pavement start is used.</para>
///
/// <para>The entry node is NOT a bare FindNearestNode: nothing guarantees a taxiway MEETS the
/// runway at the lineup point (LPPT 20's start row sits on a 1955 ft displaced threshold with the
/// nearest node 201 m OFF TO THE SIDE). FindRunwayLineupEntryNode resolves to a real runway
/// ENTRANCE instead, preferring one at or behind the lineup point, and is identical to
/// FindNearestNode whenever that node is within RUNWAY_REACH_MAX_CROSS_M of the centerline.</para>
/// </summary>
public static class RunwayLineupTarget
{
    public sealed record Result(double LineupLat, double LineupLon, TaxiNode? EntryNode);

    /// <summary>Half-width fallback when the runway row carries no width (feet).</summary>
    public const double DefaultRunwayWidthFeet = 150.0;

    /// <param name="startsForRunway">Every start row for this runway end (may be null/empty).</param>
    /// <param name="anchorLat">The aircraft's position when known — the reachability anchor;
    /// null when SimConnect has not reported one (never (0,0)).</param>
    public static Result Resolve(TaxiGraph graph, Runway rwy, IEnumerable<StartPosition>? startsForRunway,
                                 double? anchorLat, double? anchorLon)
    {
        StartPosition? start = startsForRunway == null ? null
            : TaxiGraph.PickFullLengthStart(startsForRunway, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon);

        double lineupLat, lineupLon;
        if (start != null)
        {
            (lineupLat, lineupLon) = TaxiGraph.SnapStartToRunwayCenterline(
                start.Latitude, start.Longitude, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon);
        }
        else
        {
            lineupLat = rwy.StartLat;
            lineupLon = rwy.StartLon;
        }

        double halfWidthM = (rwy.Width > 0 ? rwy.Width : DefaultRunwayWidthFeet) * 0.3048 / 2.0;
        var entry = graph.FindRunwayLineupEntryNode(
            lineupLat, lineupLon,
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
            halfWidthM, Services.TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M,
            anchorLat, anchorLon);
        return new Result(lineupLat, lineupLon, entry);
    }
}
```

- [ ] **Step 4: Make `TaxiAssistForm` call it**

In `PopulateDestinations`, replace the block that starts at `double lineupLat;` / `double lineupLon;` / `StartPosition? start = null;` and ends with the `var nearNode = _graph.FindRunwayLineupEntryNode(...)` call (keep the `if (rwy.IsClosed) continue;` above it and the `if (nearNode != null)` below it untouched) with:

```csharp
                // One owner for the lineup point and the runway-entry node — see RunwayLineupTarget
                // (the displaced-threshold and LPPT-20 reasoning moved there with the code). The
                // aircraft position anchors the reachability filter; (0,0) means SimConnect has not
                // reported one yet, the same "haveOwnPosition" test the gate list uses.
                bool haveOwnPos = _aircraftLat != 0 || _aircraftLon != 0;
                startsByRunway.TryGetValue(rwy.RunwayID, out var rwyStartRows);
                var lineup = RunwayLineupTarget.Resolve(_graph, rwy, rwyStartRows,
                    haveOwnPos ? _aircraftLat : null, haveOwnPos ? _aircraftLon : null);
                double lineupLat = lineup.LineupLat;
                double lineupLon = lineup.LineupLon;
                var nearNode = lineup.EntryNode;
```

The long comment blocks that were inline (start table / EGKK / LPPT 20) are now on the helper — delete them from the form rather than leaving a duplicate. `_graph` may be nullable there: if the surrounding code already guarantees non-null, pass `_graph!`. Do not change anything else in the form.

- [ ] **Step 5: Build the solution and run the tests**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` then the Task-4 test filter.
Expected: build succeeded (0 warnings introduced), 3 passed. Then run the whole suite once (`dotnet test ... -p:Platform=x64`) — Taxi Assist has no direct tests, but the full run guards the shared helper.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Navigation/RunwayLineupTarget.cs MSFSBlindAssist/Forms/TaxiAssistForm.cs tests/MSFSBlindAssist.Tests/RunwayLineupTargetTests.cs
git commit -m "refactor(taxi): one owner for the runway lineup point and entry node (RunwayLineupTarget)"
```

---

### Task 5: Prompt section 7 and Gemini truncation note

**Files:**
- Modify: `MSFSBlindAssist/Services/GeminiService.cs` (`GetRouteDescriptionPrompt` ~line 885; response parsing at the tail of `SendRequestAsync` ~line 855; `Candidate` model ~line 975)
- Test: `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs`, `tests/MSFSBlindAssist.Tests/GeminiResponseTests.cs`

**Interfaces:**
- Produces: `GeminiService.IncompleteNote` (`internal const string`), `GeminiService.ParseResponse(string responseJson) : string` (`internal static`), prompt section 7 text (below), word target "600 to 900 words".

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteDescriptionPromptTests
{
    [Fact]
    public void Prompt_has_the_taxi_section_with_the_owners_template_and_the_wider_word_target()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("FLIGHT DATA HERE");

        Assert.Contains("7. TAXI OUT AND TAXI IN", prompt);
        Assert.Contains("Provide the step-by-step taxi route at [ICAO] from [runway] to [terminal/gate] in a [aircraft type]. " +
                        "Please include the expected taxiways, hold short points, and any specific restrictions.", prompt);
        Assert.Contains("Real-world practice", prompt);
        Assert.Contains("Aim for 600 to 900 words", prompt);
        Assert.DoesNotContain("Aim for 300 to 500 words", prompt);
        Assert.EndsWith("FLIGHT DATA HERE", prompt);
    }

    [Fact]
    public void Prompt_forbids_inventing_taxiways_for_the_computed_route()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("Use ONLY the taxiway, exit and stand names given in the block", prompt);
    }
}
```

```csharp
// tests/MSFSBlindAssist.Tests/GeminiResponseTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

// ParseResponse is the tail of SendRequestAsync, made internal so the finishReason handling is
// pinned: a MAX_TOKENS response gets the same "may be incomplete" note ClaudeService already adds.
public class GeminiResponseTests
{
    [Fact]
    public void Text_parts_are_joined()
        => Assert.Equal("Hello world", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Hello "},{"text":"world"}]},"finishReason":"STOP"}]}"""));

    [Fact]
    public void Max_tokens_appends_the_incomplete_note()
    {
        string text = GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Cut off"}]},"finishReason":"MAX_TOKENS"}]}""");
        Assert.Equal("Cut off" + GeminiService.IncompleteNote, text);
    }

    [Fact]
    public void Stop_does_not_append_the_note()
        => Assert.Equal("Done", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Done"}]},"finishReason":"STOP"}]}"""));

    [Fact]
    public void Missing_finish_reason_is_treated_as_complete()
        => Assert.Equal("Done", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Done"}]}}]}"""));

    [Fact]
    public void Blank_text_reads_as_no_description()
        => Assert.Equal("No description available.", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"  "}]},"finishReason":"STOP"}]}"""));

    [Fact]
    public void No_candidates_throws()
        => Assert.Throws<InvalidOperationException>(() => GeminiService.ParseResponse("""{"candidates":[]}"""));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~RouteDescriptionPromptTests|FullyQualifiedName~GeminiResponseTests"`
Expected: build error (`ParseResponse`/`IncompleteNote` missing), prompt assertions would fail.

- [ ] **Step 3: Change the prompt**

In `GetRouteDescriptionPrompt`, after the `6. NOTAMS` block (after the "Skip routine or minor NOTAMs…" line) and before `IMPORTANT GUIDELINES:`, insert:

```
7. TAXI OUT AND TAXI IN
   The flight plan data ends with a TAXI ROUTES block computed by the pilot's own simulator
   scenery. For each of the two legs (taxi out at the departure airport, taxi in at the arrival
   airport) do two things, in this order:
   a) Describe the computed route in prose: the stand it starts from (say plainly when the block
      calls it a representative stand rather than an assignment), the taxiways in order, every
      hold-short point and which runway it protects, and for the arrival which side to leave the
      runway (left or right), the exit taxiway and its distance from the threshold, and the
      fallback exit if that one is missed. Use ONLY the taxiway, exit and stand names given in the block
      for this part, and repeat distances and sides exactly as given. If the block says a leg is
      unavailable, say so in one sentence.
   b) Then, under the heading "Real-world practice", answer this from your own knowledge of the
      airport: "Provide the step-by-step taxi route at [ICAO] from [runway] to [terminal/gate] in a [aircraft type]. Please include the expected taxiways, hold short points, and any specific restrictions."
      Substitute the airport, runway, stand or terminal and aircraft type from the data. Do it for
      the departure (from the stand to the runway) and for the arrival (from the runway, via the
      expected exit, to the terminal or gate). Mention wingspan or aircraft-type restrictions on
      taxiways and stands where you know of them. Where your route differs from the computed one,
      say so and say which is which; never present your own route as the computed one. When the
      block says no ground data exists for an airport, this real-world route is the answer for
      that leg and should be given in full.
```

Change `- Aim for 300 to 500 words` to `- Aim for 600 to 900 words`.

- [ ] **Step 4: Extract `ParseResponse` and read `finishReason`**

Add to the `Candidate` model: `[JsonProperty("finishReason")] public string? FinishReason { get; set; }`.

Replace the tail of `SendRequestAsync` — from `var result = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);` to the final `return` — with `return ParseResponse(responseJson);`, and add:

```csharp
    /// <summary>Spoken/read suffix when Gemini stopped at its output cap — the blind pilot cannot
    /// see that a briefing just stops. Mirrors ClaudeService's max_tokens note.</summary>
    internal const string IncompleteNote = "\n\n(Response may be incomplete — Gemini stopped before finishing.)";

    /// <summary>The response parsing formerly inline in SendRequestAsync; internal so GeminiResponseTests can pin it.</summary>
    internal static string ParseResponse(string responseJson)
    {
        var result = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);
        if (result?.Candidates == null || result.Candidates.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no candidates in response.");
        }

        var candidate = result.Candidates[0];
        var candidateContent = candidate.Content;
        if (candidateContent?.Parts == null || candidateContent.Parts.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no content in response.");
        }

        // Join EVERY non-empty text part — thinking-capable models can return multiple parts.
        string combined = string.Concat(candidateContent.Parts
            .Where(p => !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text));
        bool truncated = string.Equals(candidate.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(combined))
        {
            return truncated
                ? "Gemini stopped before completing a response. Please try again."
                : "No description available.";
        }
        return truncated ? combined + IncompleteNote : combined;
    }
```

- [ ] **Step 5: Run to verify they pass**

Same filter. Expected: 8 passed. Then `dotnet build MSFSBlindAssist.sln -c Debug` — succeeded.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Services/GeminiService.cs tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs tests/MSFSBlindAssist.Tests/GeminiResponseTests.cs
git commit -m "feat(briefing): taxi-out/taxi-in prompt section; note a Gemini briefing cut at the output cap"
```

---

### Task 6: `AugmentingAirportDataProvider.GetOnlineTaxiDataAsync`

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs` (after `PrefetchAsync`, ~line 256)
- Test: `tests/MSFSBlindAssist.Tests/AugmentingProviderOnlineDataTests.cs`

**Interfaces:**
- Consumes: `TaxiDataCache.TryLoad(icao, out IReadOnlyList<AirportTaxiData>?)`, `PrefetchAsync(icao)`, `Enabled`.
- Produces: `public Task<IReadOnlyList<AirportTaxiData>?> GetOnlineTaxiDataAsync(string icao, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/AugmentingProviderOnlineDataTests.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

// The route briefing's OpenStreetMap tier reads the decorator's already-cached online data through
// this one accessor (read-only; nothing here builds geometry into the navdata path).
public class AugmentingProviderOnlineDataTests
{
    private sealed class NoAirportProvider : IAirportDataProvider
    {
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => null;      // a fetch cannot even start
        public List<Runway> GetRunways(string icao) => new();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => new();
        public bool AirportExists(string icao) => false;
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }

    private static AugmentingAirportDataProvider Provider(TaxiDataCache cache) =>
        new(new NoAirportProvider(), cache, Array.Empty<ITaxiDataSource>(), new MergeOptions());

    [Fact]
    public async Task Cached_sources_are_returned_without_a_fetch()
    {
        var cache = new TaxiDataCache(ttlDays: 1);
        var osm = new AirportTaxiData { Source = "osm" };
        osm.Taxiways.Add(new NamedTaxiSegment { Name = "A", Lat1 = 0.01, Lon1 = 0.01, Lat2 = 0.011, Lon2 = 0.01 });
        cache.Save("LOWI", new[] { osm });

        var data = await Provider(cache).GetOnlineTaxiDataAsync("LOWI", CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("osm", Assert.Single(data!).Source);
    }

    [Fact]
    public async Task Disabled_augmentation_returns_null()
    {
        var cache = new TaxiDataCache(ttlDays: 1);
        cache.Save("LOWI", new[] { new AirportTaxiData { Source = "osm" } });
        var provider = Provider(cache);
        provider.Enabled = false;

        Assert.Null(await provider.GetOnlineTaxiDataAsync("LOWI", CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_cached_and_nothing_fetchable_returns_null()
    {
        var provider = Provider(new TaxiDataCache(ttlDays: 1));
        Assert.Null(await provider.GetOnlineTaxiDataAsync("LOWI", CancellationToken.None));
    }

    [Fact]
    public async Task Blank_icao_returns_null()
        => Assert.Null(await Provider(new TaxiDataCache(ttlDays: 1)).GetOnlineTaxiDataAsync(" ", CancellationToken.None));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~AugmentingProviderOnlineDataTests"`
Expected: build error — `GetOnlineTaxiDataAsync` missing.

- [ ] **Step 3: Implement**

After `PrefetchAsync` in `AugmentingAirportDataProvider.cs`:

```csharp
    // ── Route-briefing read of the online data ───────────────────────────────
    /// <summary>
    /// The cached online taxi data for an airport (every source that answered), for the route
    /// BRIEFING's planning-only graph and nothing else. Read-only: nothing here merges geometry
    /// into the navdata path, and TaxiDataMerger's rule ("navdata is AUTHORITATIVE; online-only
    /// geometry is IGNORED — we only steer on navdata pavement") is untouched. Awaits the shared
    /// fetch when nothing is cached; returns null when augmentation is disabled, when the fetch
    /// does not complete before <paramref name="ct"/> is cancelled, or when it returned nothing.
    /// </summary>
    public async Task<IReadOnlyList<AirportTaxiData>?> GetOnlineTaxiDataAsync(string icao, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(icao)) return null;
        if (_cache.TryLoad(icao, out var cached) && cached != null) return cached;
        try
        {
            await PrefetchAsync(icao).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        return _cache.TryLoad(icao, out var fetched) && fetched != null ? fetched : null;
    }
```

- [ ] **Step 4: Run to verify they pass**

Same filter. Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs tests/MSFSBlindAssist.Tests/AugmentingProviderOnlineDataTests.cs
git commit -m "feat(briefing): expose the cached online taxi data for the planning-only briefing graph"
```

---

### Task 7: Briefing models

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs`
- No dedicated test (pure data; exercised by every later task). Build must succeed.

**Interfaces (Produces, namespace `MSFSBlindAssist.Navigation.Briefing`):**

```csharp
public enum BriefingTier { Navdata, OpenStreetMap, None }
public sealed record OwnPosition(double Lat, double Lon, bool OnGround);
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position);
public enum StandChoiceSource { SayIntentions, AirlineMatch, Category, Any }
public sealed record StandChoice(ParkingSpot Spot, StandChoiceSource Source, IReadOnlyList<string> Notes);
public sealed record TaxiBriefingRequest(string OriginIcao, string OriginRunway, string DestinationIcao, string DestinationRunway,
    AircraftProfile Aircraft, string? AirlineIcao, OwnPosition? Own, SayIntentionsGateHint? ArrivalGate);
public sealed record HoldShortNote(string Runway, string Taxiway, bool BeforeEntering);
public sealed record NarrowTaxiwayNote(string Taxiway, double WidthMetres, double MinimumMetres);
public sealed class TaxiLegBriefing { ... }   // see code
public sealed record TaxiBriefing(AircraftProfile Aircraft, TaxiLegBriefing TaxiOut, TaxiLegBriefing TaxiIn);
public sealed record GraphBundle(TaxiGraph Graph, BriefingTier Tier, IReadOnlyList<Runway> Runways,
    IReadOnlyList<StartPosition> Starts, IReadOnlyList<ParkingSpot> Spots, string? Note, Airport? Airport);
public sealed record RouteBriefingDependencies(Func<IAirportDataProvider?> Provider, Func<GateDataSource?> GateSource,
    Func<Task<SayIntentionsFlightContext>> SayIntentions);
```

- [ ] **Step 1: Write the file**

```csharp
// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>Where a leg's ground data came from. The block names it on every leg.</summary>
public enum BriefingTier { Navdata, OpenStreetMap, None }

/// <summary>The aircraft's own position when SimConnect reported one for this briefing.</summary>
public sealed record OwnPosition(double Lat, double Lon, bool OnGround);

/// <summary>SayIntentions' ARRIVAL gate — the label it published and, when it did, the stand's position.
/// Only ever built by <see cref="SayIntentionsArrivalGate"/>, which checks the flight matches this OFP.</summary>
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position);

public enum StandChoiceSource { SayIntentions, AirlineMatch, Category, Any }

/// <summary>The stand a leg routes to and how it was chosen; <see cref="Notes"/> are pilot-readable caveats.</summary>
public sealed record StandChoice(ParkingSpot Spot, StandChoiceSource Source, IReadOnlyList<string> Notes);

public sealed record TaxiBriefingRequest(
    string OriginIcao, string OriginRunway, string DestinationIcao, string DestinationRunway,
    AircraftProfile Aircraft, string? AirlineIcao, OwnPosition? Own, SayIntentionsGateHint? ArrivalGate);

/// <summary>A hold-short point on the route: the runway it protects, the taxiway it is on, and whether it is
/// the hold before entering the departure runway (true) or a crossing (false).</summary>
public sealed record HoldShortNote(string Runway, string Taxiway, bool BeforeEntering);

/// <summary>Advisory only: navdata says this taxiway is narrower than the Annex 14 minimum for the aircraft's code letter.</summary>
public sealed record NarrowTaxiwayNote(string Taxiway, double WidthMetres, double MinimumMetres);

public sealed class TaxiLegBriefing
{
    public required string Icao { get; init; }
    public required string Runway { get; init; }
    public BriefingTier Tier { get; init; }
    /// <summary>Non-null when no route could be computed: the pilot-readable reason.</summary>
    public string? Unavailable { get; init; }
    /// <summary>Taxi-out: where the route STARTS ("current position, stand N12 (Ramp Cargo)" / "representative stand …").
    /// Taxi-in: the STAND it ends at ("SayIntentions assigned gate 52A" / "representative stand …").</summary>
    public string EndpointDescription { get; init; } = "";
    public StandChoice? Stand { get; init; }
    public IReadOnlyList<string> Taxiways { get; init; } = Array.Empty<string>();
    public double DistanceMetres { get; init; }
    public IReadOnlyList<HoldShortNote> HoldShorts { get; init; } = Array.Empty<HoldShortNote>();
    public ExitChoice? Exit { get; init; }
    /// <summary>Every exit that gets clear of the landing runway, nearest the threshold first.</summary>
    public IReadOnlyList<LandingExit> VacatingExits { get; init; } = Array.Empty<LandingExit>();
    public IReadOnlyList<NarrowTaxiwayNote> NarrowTaxiways { get; init; } = Array.Empty<NarrowTaxiwayNote>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public static TaxiLegBriefing UnavailableLeg(string icao, string runway, BriefingTier tier, string reason,
        StandChoice? stand = null, string endpoint = "", IReadOnlyList<string>? notes = null,
        IReadOnlyList<LandingExit>? vacatingExits = null, ExitChoice? exit = null) => new()
    {
        Icao = icao, Runway = runway, Tier = tier, Unavailable = reason, Stand = stand,
        EndpointDescription = endpoint, Notes = notes ?? Array.Empty<string>(),
        VacatingExits = vacatingExits ?? Array.Empty<LandingExit>(), Exit = exit,
    };
}

public sealed record TaxiBriefing(AircraftProfile Aircraft, TaxiLegBriefing TaxiOut, TaxiLegBriefing TaxiIn)
{
    /// <summary>Both legs unavailable for one reason (no database, planner failure) — still rendered, never silent.</summary>
    public static TaxiBriefing Unavailable(AircraftProfile aircraft, string originIcao, string originRunway,
        string destinationIcao, string destinationRunway, string reason) => new(aircraft,
        TaxiLegBriefing.UnavailableLeg(originIcao, originRunway, BriefingTier.None, reason),
        TaxiLegBriefing.UnavailableLeg(destinationIcao, destinationRunway, BriefingTier.None, reason));
}

/// <summary>One airport's graph and the data it was built from. <see cref="Note"/> is a caveat the tier
/// carries (OSM: stand types unknown). <see cref="Airport"/> is the reference point for the 5 km own-position test.</summary>
public sealed record GraphBundle(TaxiGraph Graph, BriefingTier Tier, IReadOnlyList<Runway> Runways,
    IReadOnlyList<StartPosition> Starts, IReadOnlyList<ParkingSpot> Spots, string? Note, Airport? Airport);

/// <summary>What the EFB needs from MainForm to compute the taxi section: a provider GETTER (the instance is
/// swapped on a database switch), the gate source and the SayIntentions file reader. Null in tests.</summary>
public sealed record RouteBriefingDependencies(
    Func<IAirportDataProvider?> Provider,
    Func<GateDataSource?> GateSource,
    Func<Task<SayIntentionsFlightContext>> SayIntentions);
```

- [ ] **Step 2: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: succeeded. (If `GeoPoint` does not resolve, its namespace is the one `SayIntentionsTaxiPathSnapper.cs` declares — add that `using`.)

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs
git commit -m "feat(briefing): taxi briefing request/result models"
```

---
### Task 8: `BriefingStandPicker` and `SayIntentionsArrivalGate`

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs`
- Test: `tests/MSFSBlindAssist.Tests/BriefingStandPickerTests.cs`

**Interfaces:**
- Consumes: Task 7 models; `ParkingSpot` (`Type`, `Radius` feet for navdata / metres for GSX by `Source`, `MaxWingspanMeters`, `AirlineCodes`, `Aliases`, `IsDeiceArea`, `Describe()`, `FitsAircraft(double wingspanFeet)`); `SayIntentionsClearanceParser.NormalizeParkingName(string?)`; `TaxiGraph.FastDistanceMeters`; `SayIntentionsFlightContext` (`FlightJsonExists`, `Origin`, `Destination`, `AssignedGate`, `AssignedGatePosition`).
- Produces:
  - `public static class BriefingStandPicker { public const double SiPositionBackstopMetres = 150.0; public const double SiNoseStopRadiusFactor = 2.0; public const double SiPositionUnknownRadiusMetres = 60.0; public static StandChoice? Pick(IReadOnlyList<ParkingSpot> spots, AircraftProfile aircraft, string? airlineIcao, SayIntentionsGateHint? siGate, Func<ParkingSpot, bool> hasGraphNode); public static string IdentityLabel(ParkingSpot spot); }`
  - `public static class SayIntentionsArrivalGate { public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao); }`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/BriefingStandPickerTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class BriefingStandPickerTests
{
    // Spots on a 0.001°-ish grid; e/n are metres east/north of RunwayFixture's base.
    private static ParkingSpot Spot(string name, int number, int type, double e, double n, double radiusFt = 100,
                                    string airlines = "", GateSource source = GateSource.Navdata) => new()
    {
        AirportICAO = "TEST", Name = name, Number = number, Type = type, Radius = radiusFt,
        Latitude = RunwayFixture.Lat(n), Longitude = RunwayFixture.Lon(e), AirlineCodes = airlines, Source = source,
    };

    private static readonly AircraftProfile Md11F = AircraftSizeClass.Resolve("MD1F", "MD-11F", 0);
    private static readonly AircraftProfile B738 = AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189);
    private static readonly AircraftProfile A388 = AircraftSizeClass.Resolve("A388", "Airbus A380-800", 500);
    private static readonly AircraftProfile Unknown = AircraftSizeClass.Resolve("ZZZZ", "", null);

    private static bool Always(ParkingSpot _) => true;

    [Fact]
    public void A_freighter_prefers_a_cargo_ramp()
    {
        var gate = Spot("G", 1, 10, 0, 0);
        var cargo = Spot("C", 1, 6, 500, 0);
        var choice = BriefingStandPicker.Pick(new[] { gate, cargo }, Md11F, null, null, Always)!;

        Assert.Same(cargo, choice.Spot);
        Assert.Equal(StandChoiceSource.Category, choice.Source);
    }

    [Fact]
    public void A_freighter_with_no_cargo_ramps_falls_back_with_a_note()
    {
        var gate = Spot("G", 1, 10, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { gate }, Md11F, null, null, Always)!;

        Assert.Same(gate, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("no cargo stands", StringComparison.Ordinal));
    }

    [Fact]
    public void An_airliner_prefers_gates_over_ramps_and_cargo()
    {
        var cargo = Spot("C", 1, 6, 0, 0);
        var ramp = Spot("R", 1, 4, 100, 0);
        var gate = Spot("G", 1, 11, 200, 0);
        Assert.Same(gate, BriefingStandPicker.Pick(new[] { cargo, ramp, gate }, B738, null, null, Always)!.Spot);
    }

    [Fact]
    public void An_airliner_with_no_gates_uses_a_ramp_and_says_so()
    {
        var ramp = Spot("R", 1, 5, 100, 0);
        var cargo = Spot("C", 1, 6, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { cargo, ramp }, B738, null, null, Always)!;

        Assert.Same(ramp, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("using a ramp", StringComparison.Ordinal));
    }

    [Fact]
    public void Wingspan_drops_stands_too_small_but_keeps_stands_of_unknown_size()
    {
        // A380: 79.75 m = 261.6 ft, navdata needs Radius >= 130.8 ft.
        var small = Spot("G", 1, 13, 0, 0, radiusFt: 100);
        var big = Spot("G", 2, 14, 300, 0, radiusFt: 150);
        var unknown = Spot("G", 3, 14, 600, 0, radiusFt: 0);
        var choice = BriefingStandPicker.Pick(new[] { small, big, unknown }, A388, null, null, Always)!;

        Assert.NotSame(small, choice.Spot);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void Nothing_fits_falls_back_to_all_with_a_note()
    {
        var small = Spot("G", 1, 13, 0, 0, radiusFt: 100);
        var choice = BriefingStandPicker.Pick(new[] { small }, A388, null, null, Always)!;

        Assert.Same(small, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("79.8 m wingspan", StringComparison.Ordinal));
    }

    [Fact]
    public void Gsx_max_wingspan_is_honoured_in_metres()
    {
        var gsxSmall = Spot("G", 1, 14, 0, 0, radiusFt: 30, source: GateSource.Gsx);
        gsxSmall.MaxWingspanMeters = 65.0;
        var gsxBig = Spot("G", 2, 14, 300, 0, radiusFt: 40, source: GateSource.Gsx);
        gsxBig.MaxWingspanMeters = 80.0;
        Assert.Same(gsxBig, BriefingStandPicker.Pick(new[] { gsxSmall, gsxBig }, A388, null, null, Always)!.Spot);
    }

    [Fact]
    public void The_airline_s_own_gates_win()
    {
        var other = Spot("A", 1, 10, 0, 0, airlines: "DAL, AAL");
        var ours = Spot("B", 7, 10, 900, 0, airlines: "UAL;DAL");
        var choice = BriefingStandPicker.Pick(new[] { other, ours }, B738, "ual", null, Always)!;

        Assert.Same(ours, choice.Spot);
        Assert.Equal(StandChoiceSource.AirlineMatch, choice.Source);
    }

    [Fact]
    public void Central_stand_is_the_one_nearest_the_centroid()
    {
        var west = Spot("G", 1, 10, 0, 0);
        var middle = Spot("G", 2, 10, 400, 0);
        var east = Spot("G", 3, 10, 1000, 0);   // centroid at 466 m → G 2
        Assert.Same(middle, BriefingStandPicker.Pick(new[] { west, middle, east }, B738, null, null, Always)!.Spot);
    }

    [Fact]
    public void Excluded_types_and_deice_pads_are_never_chosen()
    {
        var fuel = Spot("F", 1, 16, 0, 0);
        var vehicles = Spot("V", 1, 17, 10, 0);
        var deice = Spot("D", 1, 10, 20, 0);
        deice.IsDeiceArea = true;
        Assert.Null(BriefingStandPicker.Pick(new[] { fuel, vehicles, deice }, B738, null, null, Always));
    }

    [Fact]
    public void Stands_with_no_graph_node_are_skipped()
    {
        var far = Spot("G", 1, 10, 0, 0);
        var near = Spot("G", 2, 10, 100, 0);
        Assert.Same(near, BriefingStandPicker.Pick(new[] { far, near }, B738, null, null, s => s.Number == 2)!.Spot);
        Assert.Null(BriefingStandPicker.Pick(new[] { far }, B738, null, null, _ => false));
    }

    [Fact]
    public void Say_intentions_gate_matches_by_normalised_name()
    {
        var j1 = Spot("J", 1, 10, 0, 0);
        var j2 = Spot("J", 2, 10, 100, 0);
        var hint = new SayIntentionsGateHint("Terminal 3 Gate J1", null);
        var choice = BriefingStandPicker.Pick(new[] { j2, j1 }, B738, "DAL", hint, Always)!;

        Assert.Same(j1, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
    }

    [Fact]
    public void Say_intentions_gate_matches_an_online_alias()
    {
        var a24a = Spot("A", 24, 10, 0, 0);
        a24a.Suffix = "A";
        a24a.Aliases.Add("A24");
        var hint = new SayIntentionsGateHint("Gate A24", null);
        Assert.Same(a24a, BriefingStandPicker.Pick(new[] { a24a }, B738, null, hint, Always)!.Spot);
    }

    [Fact]
    public void Say_intentions_gate_matches_by_position_within_twice_the_radius()
    {
        // Navdata radius 100 ft = 30.5 m → 61 m acceptance; the nose-stop sits 40 m off the datum.
        var b6 = Spot("B", 6, 10, 0, 0, radiusFt: 100);
        var b7 = Spot("B", 7, 10, 200, 0, radiusFt: 100);
        var hint = new SayIntentionsGateHint("Gate 99", new GeoPoint(RunwayFixture.Lat(0), RunwayFixture.Lon(40)));
        var choice = BriefingStandPicker.Pick(new[] { b6, b7 }, B738, null, hint, Always)!;

        Assert.Same(b6, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("matched by position", StringComparison.Ordinal));
    }

    [Fact]
    public void Say_intentions_gate_not_found_falls_back_and_says_so()
    {
        var g1 = Spot("G", 1, 10, 0, 0);
        var hint = new SayIntentionsGateHint("Gate Z9", null);
        var choice = BriefingStandPicker.Pick(new[] { g1 }, B738, null, hint, Always)!;

        Assert.Same(g1, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("Gate Z9", StringComparison.Ordinal) && n.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_aircraft_applies_no_fit_filter()
    {
        var tiny = Spot("G", 1, 9, 0, 0, radiusFt: 10);
        Assert.Same(tiny, BriefingStandPicker.Pick(new[] { tiny }, Unknown, null, null, Always)!.Spot);
    }

    [Fact]
    public void Identity_label_is_the_part_before_the_type()
    {
        Assert.Equal("A 24A", BriefingStandPicker.IdentityLabel(new ParkingSpot { Name = "A", Number = 24, Suffix = "A", Type = 10 }));
        Assert.Equal("Gate 5", BriefingStandPicker.IdentityLabel(new ParkingSpot { Number = 5, Type = 10 }));
    }

    // ── SayIntentionsArrivalGate.From ──────────────────────────────────────────

    private static SayIntentionsFlightContext Ctx(bool exists, string? origin, string? dest, string? gate) => new()
    {
        FlightJsonExists = exists, Origin = origin, Destination = dest, AssignedGate = gate,
    };

    [Fact]
    public void No_flight_json_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(false, "EGLL", "KJFK", "Gate 6"), "EGLL", "KJFK"));

    [Fact]
    public void Null_context_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(null, "EGLL", "KJFK"));

    [Fact]
    public void Another_flight_s_gate_is_ignored()
    {
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KLAX", "Gate 6"), "EGLL", "KJFK"));
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "LMML", "KJFK", "Gate 6"), "EGLL", "KJFK"));
    }

    [Fact]
    public void Matching_flight_yields_the_hint()
    {
        var ctx = Ctx(true, "egll", "kjfk ", "Terminal 1 Gate 6");
        ctx.AssignedGatePosition = new GeoPoint(40.64, -73.78);
        var hint = SayIntentionsArrivalGate.From(ctx, "EGLL", "KJFK")!;

        Assert.Equal("Terminal 1 Gate 6", hint.Label);
        Assert.Equal(40.64, hint.Position!.Value.Latitude);
    }

    [Fact]
    public void Blank_gate_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KJFK", " "), "EGLL", "KJFK"));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~BriefingStandPickerTests"`
Expected: build error — `BriefingStandPicker` not found.

- [ ] **Step 3: Implement**

```csharp
// MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs
using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which stand the briefing routes to when SayIntentions has not assigned one (or its gate is not
/// at this airport): category (freighter → cargo ramps, airliner → gates, ramps as fallback),
/// wingspan fit, the SimBrief airline's own stands, then the stand at the centre of what is left so
/// the route represents the terminal area. Every fallback leaves a pilot-readable note; nothing is
/// silent. Pure: the caller supplies "has a graph node within 100 m" as a predicate.
/// </summary>
public static class BriefingStandPicker
{
    /// <summary>Position backstop for the SayIntentions gate (same role as GateAliasResolver's 150 m).</summary>
    public const double SiPositionBackstopMetres = 150.0;
    /// <summary>The published gate position is the NOSE-STOP, offset from the stand datum by up to
    /// about twice the stand's radius (measured KDTW/EDDB) — TaxiAssistForm's SI step uses the same factor.</summary>
    public const double SiNoseStopRadiusFactor = 2.0;
    /// <summary>Acceptance for a stand whose size is unknown (radius 0: OpenStreetMap, sparse navdata).</summary>
    public const double SiPositionUnknownRadiusMetres = 60.0;

    private static readonly HashSet<int> ExcludedTypes = new() { 1, 8, 16, 17 };   // None, Military Combat, Fuel, Vehicles
    private static readonly HashSet<int> CargoTypes = new() { 6, 7 };
    private static readonly HashSet<int> GateTypes = new() { 9, 10, 11, 13, 14 };
    private static readonly HashSet<int> RampTypes = new() { 2, 3, 4, 5, 12, 15 };

    public static StandChoice? Pick(IReadOnlyList<ParkingSpot> spots, AircraftProfile aircraft, string? airlineIcao,
                                    SayIntentionsGateHint? siGate, Func<ParkingSpot, bool> hasGraphNode)
    {
        var notes = new List<string>();
        if (spots == null || spots.Count == 0) return null;

        if (siGate != null)
        {
            var si = MatchSayIntentionsGate(spots, siGate, hasGraphNode, notes);
            if (si != null) return si;
            notes.Add($"SayIntentions assigned gate \"{siGate.Label}\" was not found at this airport; using a representative stand instead");
        }

        var pool = spots.Where(s => !ExcludedTypes.Contains(s.Type) && !s.IsDeiceArea).ToList();
        if (pool.Count == 0) return null;

        bool categoryApplied = false;
        if (aircraft.IsFreighter)
        {
            var cargo = pool.Where(s => CargoTypes.Contains(s.Type) || s.Type == 0).ToList();
            if (cargo.Any(s => CargoTypes.Contains(s.Type))) { pool = cargo; categoryApplied = true; }
            else notes.Add("no cargo stands at this airport");
        }
        else
        {
            var gates = pool.Where(s => GateTypes.Contains(s.Type) || s.Type == 0).ToList();
            if (gates.Any(s => GateTypes.Contains(s.Type))) { pool = gates; categoryApplied = true; }
            else
            {
                var ramps = pool.Where(s => RampTypes.Contains(s.Type) || s.Type == 0).ToList();
                if (ramps.Any(s => RampTypes.Contains(s.Type)))
                {
                    pool = ramps; categoryApplied = true;
                    notes.Add("no gate stands at this airport; using a ramp");
                }
            }
        }

        if (aircraft.WingspanMetres is double span)
        {
            double spanFeet = span / 0.3048;
            var fitting = pool.Where(s => SizeUnknown(s) || s.FitsAircraft(spanFeet)).ToList();
            if (fitting.Count > 0) pool = fitting;
            else notes.Add($"no stand at this airport is marked as fitting a {span.ToString("0.0", CultureInfo.InvariantCulture)} m wingspan");
        }

        var source = categoryApplied ? StandChoiceSource.Category : StandChoiceSource.Any;
        if (!string.IsNullOrWhiteSpace(airlineIcao))
        {
            var airline = pool.Where(s => ServesAirline(s, airlineIcao)).ToList();
            if (airline.Count > 0) { pool = airline; source = StandChoiceSource.AirlineMatch; }
        }

        pool = pool.Where(hasGraphNode).ToList();
        if (pool.Count == 0) return null;
        return new StandChoice(Central(pool), source, notes);
    }

    /// <summary>"A 24A", "Gate 5" — the part of <see cref="ParkingSpot.Describe"/> before its first spaced dash,
    /// which is the boundary NormalizeParkingName cuts at, so a SayIntentions label compares against it.</summary>
    public static string IdentityLabel(ParkingSpot spot)
    {
        string d = spot.Describe();
        int cut = d.IndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? d[..cut] : d;
    }

    private static StandChoice? MatchSayIntentionsGate(IReadOnlyList<ParkingSpot> spots, SayIntentionsGateHint hint,
                                                       Func<ParkingSpot, bool> hasGraphNode, List<string> notes)
    {
        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(hint.Label);
        if (wanted.Length > 0)
        {
            var byName = spots.FirstOrDefault(s => hasGraphNode(s) &&
                (SayIntentionsClearanceParser.NormalizeParkingName(IdentityLabel(s)) == wanted ||
                 s.Aliases.Any(a => SayIntentionsClearanceParser.NormalizeParkingName(a) == wanted)));
            if (byName != null) return new StandChoice(byName, StandChoiceSource.SayIntentions, notes.ToList());
        }

        if (hint.Position is GeoPoint p)
        {
            ParkingSpot? best = null;
            double bestD = double.MaxValue;
            foreach (var s in spots)
            {
                if (!hasGraphNode(s)) continue;
                double d = TaxiGraph.FastDistanceMeters(p.Latitude, p.Longitude, s.Latitude, s.Longitude);
                double radiusM = s.Source == GateSource.Navdata ? s.Radius * 0.3048 : s.Radius;
                double limit = radiusM > 0
                    ? Math.Min(SiPositionBackstopMetres, radiusM * SiNoseStopRadiusFactor)
                    : SiPositionUnknownRadiusMetres;
                if (d <= limit && d < bestD) { best = s; bestD = d; }
            }
            if (best != null)
            {
                notes.Add("assigned gate matched by position");
                return new StandChoice(best, StandChoiceSource.SayIntentions, notes.ToList());
            }
        }
        return null;
    }

    private static bool SizeUnknown(ParkingSpot s) => s.Radius <= 0 && !s.MaxWingspanMeters.HasValue;

    private static bool ServesAirline(ParkingSpot s, string airlineIcao)
    {
        if (string.IsNullOrWhiteSpace(s.AirlineCodes)) return false;
        return s.AirlineCodes.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(code => string.Equals(code, airlineIcao.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static ParkingSpot Central(List<ParkingSpot> pool)
    {
        double cLat = pool.Average(s => s.Latitude);
        double cLon = pool.Average(s => s.Longitude);
        return pool
            .OrderBy(s => TaxiGraph.FastDistanceMeters(cLat, cLon, s.Latitude, s.Longitude))
            .ThenBy(s => s.Describe(), StringComparer.Ordinal)
            .First();
    }
}

/// <summary>
/// Turns a flight.json snapshot into the briefing's arrival-gate hint, or null. SayIntentions assigns
/// an ARRIVAL gate only, so the hint is offered only when SayIntentions' flight is THIS OFP's flight:
/// the file exists, its origin AND destination match the plan, and a gate is set. SI not running → no
/// file → null; SI on another flight, or a stale file from another city pair → null. No timestamp is
/// consulted (a stale file for the same city pair is accepted and labelled as SayIntentions' assignment).
/// </summary>
public static class SayIntentionsArrivalGate
{
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao)
    {
        if (ctx == null || !ctx.FlightJsonExists) return null;
        if (string.IsNullOrWhiteSpace(ctx.AssignedGate)) return null;
        if (!IcaoEquals(ctx.Origin, departureIcao) || !IcaoEquals(ctx.Destination, arrivalIcao)) return null;
        return new SayIntentionsGateHint(ctx.AssignedGate.Trim(), ctx.AssignedGatePosition);
    }

    private static bool IcaoEquals(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run to verify they pass**

Same filter. Expected: all pass. If `GateSource.Gsx` is not a member name, use whatever non-navdata member `GateSource` declares (`MSFSBlindAssist/Database/Models/GateSource.cs`).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs tests/MSFSBlindAssist.Tests/BriefingStandPickerTests.cs
git commit -m "feat(briefing): choose the briefing stand — SayIntentions gate, category, wingspan, airline, central"
```

---

### Task 9: `OsmPlanningGraph` (tier 2)

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/OsmPlanningGraph.cs`
- Test: `tests/MSFSBlindAssist.Tests/OsmPlanningGraphTests.cs`

**Interfaces:**
- Consumes: `AirportTaxiData` (`Source`, `Taxiways : List<NamedTaxiSegment>{Name,Lat1,Lon1,Lat2,Lon2}`, `Parking : List<(string Name, double Lat, double Lon)>`, `HoldingPoints : List<(string Name, double Lat, double Lon, string Kind)>`), `TaxiGraph.Build(List<TaxiPath>, List<ParkingSpot>, List<StartPosition>, IReadOnlyList<Runway>?)`, `StandId.Parse(string?)` → `(Letter, Number, Suffix, HasNumber)`, `GraphBundle` (Task 7).
- Produces: `public static class OsmPlanningGraph { public const double HoldSnapMetres = 3.0; public const string Note = "stand types unknown (OpenStreetMap)"; public static GraphBundle? Build(IReadOnlyList<AirportTaxiData>? sources, IReadOnlyList<Runway> runways, IReadOnlyList<StartPosition> starts, Airport? airport); internal static AirportTaxiData? PickSource(...); internal static string HoldTypeAt(...); internal static ParkingSpot ToSpot(...); }`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/OsmPlanningGraphTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

// The OpenStreetMap PLANNING-ONLY graph: named ways become "T" paths, holding positions within 3 m of
// a way vertex become hold-short nodes, parking positions become typeless stands. Never fed to guidance.
public class OsmPlanningGraphTests
{
    private static double Lat(double n) => RunwayFixture.Lat(n);
    private static double Lon(double e) => RunwayFixture.Lon(e);

    private static AirportTaxiData Osm(params (string name, double e1, double n1, double e2, double n2)[] segs)
    {
        var d = new AirportTaxiData { Source = "osm" };
        foreach (var s in segs)
            d.Taxiways.Add(new NamedTaxiSegment { Name = s.name, Lat1 = Lat(s.n1), Lon1 = Lon(s.e1), Lat2 = Lat(s.n2), Lon2 = Lon(s.e2) });
        return d;
    }

    [Fact]
    public void Consecutive_way_segments_connect_into_one_taxiway()
    {
        var osm = Osm(("A", 0, 0, 100, 0), ("A", 100, 0, 200, 0));
        var bundle = OsmPlanningGraph.Build(new[] { osm }, new List<Runway>(), new List<StartPosition>(), null)!;

        Assert.Equal(BriefingTier.OpenStreetMap, bundle.Tier);
        Assert.Equal(3, bundle.Graph.Nodes.Count);
        var middle = bundle.Graph.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(100)) < 1e-9);
        Assert.Equal(2, bundle.Graph.Adjacency[middle.NodeId].Count);
        Assert.Equal(OsmPlanningGraph.Note, bundle.Note);
    }

    [Fact]
    public void A_holding_position_on_a_vertex_becomes_a_hold_short_node()
    {
        var osm = Osm(("A", 0, 0, 100, 0), ("A", 100, 0, 200, 0));
        osm.HoldingPoints.Add(("A1", Lat(0), Lon(101.0), "runway"));   // 1 m from the vertex at 100 E
        osm.HoldingPoints.Add(("A2", Lat(0), Lon(200.0), "ILS"));
        var g = OsmPlanningGraph.Build(new[] { osm }, new List<Runway>(), new List<StartPosition>(), null)!.Graph;

        Assert.Equal(TaxiNodeType.HoldShort, g.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(100)) < 1e-9).Type);
        Assert.Equal(TaxiNodeType.ILSHoldShort, g.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(200)) < 1e-9).Type);
        Assert.Equal(TaxiNodeType.Normal, g.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(0)) < 1e-9).Type);
    }

    [Fact]
    public void A_holding_position_further_than_3m_from_any_vertex_is_ignored()
    {
        Assert.Equal("N", OsmPlanningGraph.HoldTypeAt(Lat(0), Lon(0), new List<(string, double, double, string)> { ("X", Lat(0), Lon(5), "runway") }));
        Assert.Equal("HS", OsmPlanningGraph.HoldTypeAt(Lat(0), Lon(0), new List<(string, double, double, string)> { ("X", Lat(0), Lon(2), "") }));
        Assert.Equal("IHS", OsmPlanningGraph.HoldTypeAt(Lat(0), Lon(0), new List<(string, double, double, string)> { ("X", Lat(0), Lon(2), "ils") }));
    }

    [Fact]
    public void Parking_positions_become_typeless_stands_with_parsed_identity()
    {
        var b25 = OsmPlanningGraph.ToSpot(("B25", 1.0, 2.0));
        Assert.Equal("B", b25.Name);
        Assert.Equal(25, b25.Number);
        Assert.Equal(0, b25.Type);
        Assert.Equal(0.0, b25.Radius);
        Assert.Equal(1.0, b25.Latitude);

        var word = OsmPlanningGraph.ToSpot(("HAWKER", 1.0, 2.0));
        Assert.Equal("HAWKER", word.Name);
        Assert.Equal(0, word.Number);
    }

    [Fact]
    public void Unnamed_segments_are_skipped_and_no_taxiways_means_no_graph()
    {
        var empty = new AirportTaxiData { Source = "osm" };
        empty.Taxiways.Add(new NamedTaxiSegment { Name = "", Lat1 = Lat(0), Lon1 = Lon(0), Lat2 = Lat(0), Lon2 = Lon(100) });
        Assert.Null(OsmPlanningGraph.Build(new[] { empty }, new List<Runway>(), new List<StartPosition>(), null));
        Assert.Null(OsmPlanningGraph.Build(null, new List<Runway>(), new List<StartPosition>(), null));
    }

    [Fact]
    public void The_source_with_the_most_taxiways_wins_and_osm_breaks_a_tie()
    {
        var osm = Osm(("A", 0, 0, 100, 0));
        var apt = Osm(("A", 0, 0, 100, 0));
        apt.GetType().GetProperty("Source")!.SetValue(apt, "aptdat");   // init-only: set through reflection for the fixture
        Assert.Same(osm, OsmPlanningGraph.PickSource(new[] { apt, osm }));

        var bigger = Osm(("A", 0, 0, 100, 0), ("B", 0, 0, 0, 100));
        bigger.GetType().GetProperty("Source")!.SetValue(bigger, "aptdat");
        Assert.Same(bigger, OsmPlanningGraph.PickSource(new[] { osm, bigger }));
    }
}
```

(If `Source` cannot be set through reflection on the `init` property, construct the fixture with `new AirportTaxiData { Source = "aptdat" }` and add the segments to it instead — the point is only which source wins.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~OsmPlanningGraphTests"`
Expected: build error — `OsmPlanningGraph` not found.

- [ ] **Step 3: Implement**

```csharp
// MSFSBlindAssist/Navigation/Briefing/OsmPlanningGraph.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// The route briefing's tier-2 graph: built from the online taxi data (OpenStreetMap, or X-Plane
/// apt.dat) when the navigation database has NO taxiways for an airport. PLANNING ONLY — this graph
/// is a local of the planner, is never stored where TaxiGuidanceManager or a form could reach it,
/// and the briefing labels every line built from it. TaxiDataMerger's rule ("never steer on online
/// geometry") is untouched: nothing here feeds the navdata path.
/// <para>Named way segments become "T" paths (unnamed ones are dropped, as the fetcher already
/// drops them); a holding position within <see cref="HoldSnapMetres"/> of a way vertex marks that
/// vertex "HS" ("IHS" for an ILS hold), so hold-short placement and landing-exit discovery run
/// exactly as on navdata; parking positions become stands of unknown type and size (OSM carries
/// neither), so the stand picker keeps them all and applies no fit filter. Runways and runway starts
/// come from the database — the online sources carry no runways.</para>
/// </summary>
public static class OsmPlanningGraph
{
    public const double HoldSnapMetres = 3.0;
    public const string Note = "stand types unknown (OpenStreetMap)";

    public static GraphBundle? Build(IReadOnlyList<AirportTaxiData>? sources, IReadOnlyList<Runway> runways,
                                     IReadOnlyList<StartPosition> starts, Airport? airport)
    {
        var source = PickSource(sources);
        if (source == null) return null;

        var holds = source.HoldingPoints;
        var paths = new List<TaxiPath>(source.Taxiways.Count);
        foreach (var seg in source.Taxiways)
        {
            if (string.IsNullOrWhiteSpace(seg.Name)) continue;
            paths.Add(new TaxiPath
            {
                Type = "T", Name = seg.Name.Trim(), Width = 0,
                StartType = HoldTypeAt(seg.Lat1, seg.Lon1, holds), EndType = HoldTypeAt(seg.Lat2, seg.Lon2, holds),
                StartLat = seg.Lat1, StartLon = seg.Lon1, EndLat = seg.Lat2, EndLon = seg.Lon2,
            });
        }
        if (paths.Count == 0) return null;

        var spots = source.Parking.Select(ToSpot).ToList();
        var graph = TaxiGraph.Build(paths, spots, starts.ToList(), runways);
        return new GraphBundle(graph, BriefingTier.OpenStreetMap, runways, starts, spots, Note, airport);
    }

    /// <summary>The source with the most taxiway segments; OSM wins a tie because only OSM carries holding points.</summary>
    internal static AirportTaxiData? PickSource(IReadOnlyList<AirportTaxiData>? sources) =>
        sources?.Where(s => s != null && s.Taxiways.Count > 0)
                .OrderByDescending(s => s.Taxiways.Count)
                .ThenByDescending(s => string.Equals(s.Source, "osm", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

    internal static string HoldTypeAt(double lat, double lon, IReadOnlyList<(string Name, double Lat, double Lon, string Kind)> holds)
    {
        foreach (var h in holds)
        {
            if (TaxiGraph.FastDistanceMeters(lat, lon, h.Lat, h.Lon) <= HoldSnapMetres)
                return string.Equals(h.Kind, "ILS", StringComparison.OrdinalIgnoreCase) ? "IHS" : "HS";
        }
        return "N";
    }

    internal static ParkingSpot ToSpot((string Name, double Lat, double Lon) p)
    {
        var id = StandId.Parse(p.Name);
        return new ParkingSpot
        {
            Name = id.HasNumber ? id.Letter : (p.Name ?? "").Trim(),
            Number = id.HasNumber ? id.Number : 0,
            Suffix = id.HasNumber ? id.Suffix : "",
            Type = 0, Radius = 0,
            Latitude = p.Lat, Longitude = p.Lon,
            Source = GateSource.Navdata,
        };
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Same filter. Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/OsmPlanningGraph.cs tests/MSFSBlindAssist.Tests/OsmPlanningGraphTests.cs
git commit -m "feat(briefing): planning-only taxi graph from OpenStreetMap when navdata has no taxiways"
```

---
### Task 10: `TaxiBriefingPlanner` — the pure legs (`PlanTaxiOut` / `PlanTaxiIn`)

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs` (pure half only; `PlanAsync` is Task 12)
- Test: `tests/MSFSBlindAssist.Tests/TaxiBriefingFixture.cs`, `tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs`

**Interfaces:**
- Consumes: Task 7 models; `BriefingStandPicker.Pick`, `BriefingStandPicker.IdentityLabel` (Task 8); `BriefingExitPicker.Pick` (Task 3); `RunwayLineupTarget.Resolve` (Task 4); `TaxiRouter(graph).FindShortestPath(int, int)`; `RouteRunwayCrossings.InsertRunwayHoldShorts(TaxiRoute, IReadOnlyList<TaxiGraph.RunwayCenterline>, string destinationName, RouteRunwayCrossings.AircraftPosition? aircraft)`, `RouteRunwayCrossings.ExtractRunwayDesignators(string?)`, `RouteRunwayCrossings.NormalizeDesignator(string)`, `RouteRunwayCrossings.Reciprocal(string)`; `RouteTaxiwaySequence.DistinctConsecutive(IReadOnlyList<TaxiRouteSegment>?, int start = 0)`; `TaxiGraph.GetLandingExits(Runway)`, `LandingExitVacateScreen.Mark(graph, exits, runway)`, `LandingExitDestination.Resolve(graph, exit, allExits, runway, runwayHeadingTrue, out double, out double, out string)`; `TaxiGraph.FindNearestNode(lat, lon, int? requiredComponentId = null, bool excludeBridgeOnlyStandStubs = false)`, `TaxiGraph.FastDistanceMeters`; `SayIntentionsClearanceParser.CleanRunway(string?)` (→ "09L" form or null); `TaxiRouteSegment.PathWidth` (feet, from the edge).
- Produces (`public static partial class TaxiBriefingPlanner`):
  - `public const double OwnPositionMaxAirportDistanceMetres = 5000.0;`, `OwnPositionMaxNodeDistanceMetres = 150.0`, `StandNodeMaxDistanceMetres = 100.0`
  - `public static TaxiLegBriefing PlanTaxiOut(TaxiBriefingRequest r, GraphBundle g)`
  - `public static TaxiLegBriefing PlanTaxiIn(TaxiBriefingRequest r, GraphBundle g)`
  - `internal static List<HoldShortNote> CollectHoldShorts(TaxiRoute route, IReadOnlyList<TaxiRouteRunwayEvent> events, string? departureRunway, string? landedRunway, List<string> notes)`
  - `internal static List<NarrowTaxiwayNote> NarrowTaxiways(TaxiRoute route, AircraftProfile aircraft)`
  - `internal static bool SameRunway(string a, string b)`, `internal static Runway? FindRunway(IReadOnlyList<Runway> runways, string id)`, `internal static TaxiNode? StandNode(TaxiGraph graph, ParkingSpot spot)`, `internal static string DescribeStand(StandChoice stand, string? airlineIcao)`

- [ ] **Step 1: Write the shared fixture**

```csharp
// tests/MSFSBlindAssist.Tests/TaxiBriefingFixture.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The synthetic airport "TEST" the briefing tests plan on. Metres east/north of RunwayFixture's base.
///
///   north 250: stands  G 1 (300 E, gate, DAL, r=150 ft)  G 2 (800 E, gate small, r=40 ft)  C 1 (2500 E, cargo, UPS, r=100 ft)
///   north 100: taxiway A, east 0 → 3000, nodes at every junction; hold bars (HS) at 1150 E and 1250 E
///              either side of runway 18/36, which runs north–south at east 1200 from north 60 to 1000.
///   north   0: runway 09/27, east 0 → 3000 (150 ft wide). Taxiways off it, all going north to A:
///              E1 at 50 E (164 ft — below the 500 ft exit floor, so it is the departure entrance only),
///              B at 600 E (1,969 ft), C at 1800 E (5,906 ft), D at 2700 E (8,858 ft); their runway ends are HS.
///
/// Landing 09 at 130–140 kt: B is 969 ft from touchdown (unreachable), C 4,906 ft (the exit), D next.
/// </summary>
internal static class TaxiBriefingFixture
{
    public static double Lat(double northM) => RunwayFixture.Lat(northM);
    public static double Lon(double eastM) => RunwayFixture.Lon(eastM);

    public static TaxiPath Path(string name, double e1, double n1, double e2, double n2,
                                string type = "T", string startType = "N", string endType = "N", double widthFt = 98.0) => new()
    {
        Name = name, Type = type, Width = widthFt, StartType = startType, EndType = endType,
        StartLat = Lat(n1), StartLon = Lon(e1), EndLat = Lat(n2), EndLon = Lon(e2),
    };

    public static TaxiPath LeadIn(double connE, double connN, double standE, double standN) =>
        Path("", connE, connN, standE, standN, type: "P", endType: "P", widthFt: 60);

    public static Runway Runway(string id, double e1, double n1, double e2, double n2, double headingTrue, double widthFt = 150) => new()
    {
        RunwayID = id, StartLat = Lat(n1), StartLon = Lon(e1), EndLat = Lat(n2), EndLon = Lon(e2),
        Heading = headingTrue, HeadingMag = headingTrue,
        Length = Math.Sqrt((e2 - e1) * (e2 - e1) + (n2 - n1) * (n2 - n1)) / 0.3048, Width = widthFt,
    };

    public static StartPosition Start(string rwy, double e, double n, double heading) =>
        new() { RunwayName = rwy, Type = "R", Heading = heading, Latitude = Lat(n), Longitude = Lon(e) };

    public static ParkingSpot Spot(string name, int number, int type, double e, double n, double radiusFt, string airlines = "") => new()
    {
        AirportICAO = "TEST", Name = name, Number = number, Type = type, Radius = radiusFt,
        Latitude = Lat(n), Longitude = Lon(e), AirlineCodes = airlines, Source = GateSource.Navdata,
    };

    public static readonly double[] TaxiwayANodesEast = { 0, 50, 300, 500, 600, 800, 1000, 1150, 1200, 1250, 1500, 1800, 2000, 2500, 2700, 3000 };

    public static List<TaxiPath> Paths()
    {
        var paths = new List<TaxiPath>();
        var xs = TaxiwayANodesEast;
        for (int i = 1; i < xs.Length; i++)
        {
            string st = xs[i - 1] is 1150.0 or 1250.0 ? "HS" : "N";
            string et = xs[i] is 1150.0 or 1250.0 ? "HS" : "N";
            paths.Add(Path("A", xs[i - 1], 100, xs[i], 100, startType: st, endType: et));
        }
        paths.Add(Path("E1", 50, 0, 50, 100, startType: "HS"));
        paths.Add(Path("B", 600, 0, 600, 100, startType: "HS"));
        paths.Add(Path("C", 1800, 0, 1800, 100, startType: "HS"));
        paths.Add(Path("D", 2700, 0, 2700, 100, startType: "HS"));
        paths.Add(LeadIn(300, 100, 300, 250));
        paths.Add(LeadIn(800, 100, 800, 250));
        paths.Add(LeadIn(2500, 100, 2500, 250));
        return paths;
    }

    public static List<ParkingSpot> Spots() => new()
    {
        Spot("G", 1, 10, 300, 250, 150, "DAL"),
        Spot("G", 2, 9, 800, 250, 40),
        Spot("C", 1, 6, 2500, 250, 100, "UPS"),
    };

    public static List<Runway> Runways() => new()
    {
        Runway("09", 0, 0, 3000, 0, 90),
        Runway("27", 3000, 0, 0, 0, 270),
        Runway("36", 1200, 60, 1200, 1000, 360),
        Runway("18", 1200, 1000, 1200, 60, 180),
    };

    public static List<StartPosition> Starts() => new()
    {
        Start("09", 10, 0, 90), Start("27", 2990, 0, 270), Start("36", 1200, 70, 360), Start("18", 1200, 990, 180),
    };

    public static Airport AirportRef() => new() { ICAO = "TEST", Name = "Test Field", Latitude = Lat(500), Longitude = Lon(1500) };

    public static GraphBundle Airport(BriefingTier tier = BriefingTier.Navdata)
    {
        var runways = Runways();
        var starts = Starts();
        var spots = Spots();
        var graph = TaxiGraph.Build(Paths(), spots, starts, runways);
        return new GraphBundle(graph, tier, runways, starts, spots, null, AirportRef());
    }

    public static TaxiBriefingRequest Request(AircraftProfile aircraft, string? airline = null, OwnPosition? own = null,
                                              SayIntentionsGateHint? gate = null, string originRunway = "09", string destRunway = "09") =>
        new("TEST", originRunway, "TEST", destRunway, aircraft, airline, own, gate);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

public class TaxiBriefingPlannerTests
{
    private static readonly AircraftProfile Md11F = AircraftSizeClass.Resolve("MD1F", "MD-11F", 0);
    private static readonly AircraftProfile B738 = AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189);
    private static readonly AircraftProfile A388 = AircraftSizeClass.Resolve("A388", "Airbus A380-800", 500);

    // ── taxi out ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Freighter_taxis_out_from_the_cargo_stand_via_A_and_E1_with_both_hold_shorts()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(Md11F, airline: "UPS"), Airport());

        Assert.Null(leg.Unavailable);
        Assert.Equal(BriefingTier.Navdata, leg.Tier);
        Assert.StartsWith("representative stand C 1", leg.EndpointDescription);
        Assert.Equal(new[] { "A", "E1" }, leg.Taxiways);
        Assert.InRange(leg.DistanceMetres, 2600, 2800);
        Assert.Contains(leg.HoldShorts, h => (h.Runway == "18" || h.Runway == "36") && h.Taxiway == "A" && !h.BeforeEntering);
        Assert.Contains(leg.HoldShorts, h => h.Runway == "09" && h.Taxiway == "E1" && h.BeforeEntering);
        Assert.Equal(2, leg.HoldShorts.Count);
        Assert.Empty(leg.NarrowTaxiways);
    }

    [Fact]
    public void Parked_at_the_origin_the_taxi_out_starts_from_the_current_position()
    {
        var own = new OwnPosition(Lat(250), Lon(2500), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), Airport());

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Null(leg.Stand);
        Assert.Equal(new[] { "A", "E1" }, leg.Taxiways);
    }

    [Fact]
    public void Airborne_or_far_away_the_taxi_out_uses_a_representative_stand()
    {
        var airborne = new OwnPosition(Lat(250), Lon(2500), OnGround: false);
        Assert.StartsWith("representative stand", TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: airborne), Airport()).EndpointDescription);

        var elsewhere = new OwnPosition(Lat(20000), Lon(2500), OnGround: true);   // 19.5 km north of the field
        Assert.StartsWith("representative stand", TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: elsewhere), Airport()).EndpointDescription);
    }

    [Fact]
    public void Airliner_taxis_out_from_its_airline_s_gate()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "DAL"), Airport());
        Assert.StartsWith("representative stand G 1", leg.EndpointDescription);
        Assert.Equal(StandChoiceSource.AirlineMatch, leg.Stand!.Source);
    }

    [Fact]
    public void Unknown_runway_is_reported()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "04"), Airport());
        Assert.Equal("runway 04 is not in the navigation database for TEST", leg.Unavailable);
    }

    [Fact]
    public void Runway_ids_match_without_leading_zeros()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "9"), Airport());
        Assert.Null(leg.Unavailable);
        Assert.Equal("09", leg.Runway);
    }

    // ── taxi in ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Airliner_lands_on_09_exits_left_at_C_and_taxis_to_the_say_intentions_gate()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", null);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("Left", leg.Exit.Exit.ExitSide);
        Assert.True(leg.Exit.ComfortablyReachable);
        Assert.Equal("D", leg.Exit.NextExit!.TaxiwayName);
        Assert.Equal(new[] { "B", "C", "D" }, leg.VacatingExits.Select(e => e.TaxiwayName));
        Assert.Equal("SayIntentions assigned gate G 1", leg.EndpointDescription);
        Assert.Equal(new[] { "A" }, leg.Taxiways);
        Assert.InRange(leg.DistanceMetres, 1550, 1750);
        var hold = Assert.Single(leg.HoldShorts);
        Assert.True(hold.Runway is "18" or "36");
        Assert.Equal("A", hold.Taxiway);
        Assert.False(hold.BeforeEntering);
    }

    [Fact]
    public void Freighter_lands_and_taxis_to_the_cargo_stand_with_no_crossing()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(Md11F, airline: "UPS"), Airport());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.StartsWith("representative stand C 1", leg.EndpointDescription);
        Assert.Empty(leg.HoldShorts);              // C at 1800 E to C 1 at 2500 E never meets 18/36
        Assert.Equal(new[] { "A" }, leg.Taxiways);
    }

    [Fact]
    public void A380_fits_only_the_large_gate()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(A388), Airport());
        Assert.StartsWith("representative stand G 1", leg.EndpointDescription);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
    }

    [Fact]
    public void Say_intentions_gate_missing_at_the_airport_is_said_and_a_representative_stand_used()
    {
        var gate = new SayIntentionsGateHint("Terminal 4 Gate B99", null);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.Contains(leg.Notes, n => n.Contains("B99", StringComparison.Ordinal) && n.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void No_stand_on_the_network_is_reported_but_the_exit_is_still_briefed()
    {
        var bundle = Airport();
        var noStands = bundle with { Spots = new List<ParkingSpot>() };
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738), noStands);

        Assert.NotNull(leg.Unavailable);
        Assert.Contains("no stand", leg.Unavailable, StringComparison.Ordinal);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.NotEmpty(leg.VacatingExits);
    }

    [Fact]
    public void Landing_runway_with_no_exit_is_reported_with_the_stand()
    {
        // Runway 18 has no turn-offs at all.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "18"), Airport());

        Assert.Equal("no exit taxiway is mapped clear of runway 18 in this scenery", leg.Unavailable);
        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.Empty(leg.VacatingExits);
    }

    // ── hold-short bookkeeping ───────────────────────────────────────────────────────────────

    private static TaxiRoute RouteWithHold(string? holdLabel, string? startHold)
    {
        var n1 = RunwayFixture.Node(1, 0, 0);
        var n2 = RunwayFixture.Node(2, 100, 0, holdShortName: holdLabel);
        var n3 = RunwayFixture.Node(3, 200, 0);
        var route = new TaxiRoute { Segments = RunwayFixture.Route(n1, n2, n3), StartHoldRunway = startHold };
        route.Segments[0].TaxiwayName = "A";
        route.Segments[1].TaxiwayName = "A";
        route.Segments[0].IsHoldShortPoint = holdLabel != null;
        return route;
    }

    [Fact]
    public void A_hold_for_the_runway_just_landed_on_is_discarded_including_its_reciprocal()
    {
        var notes = new List<string>();
        var route = RouteWithHold("runway 27 at A", startHold: null);
        Assert.Empty(TaxiBriefingPlanner.CollectHoldShorts(route, Array.Empty<TaxiRouteRunwayEvent>(), null, landedRunway: "09", notes));
        Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(route, Array.Empty<TaxiRouteRunwayEvent>(), null, landedRunway: "18", notes));
    }

    [Fact]
    public void A_start_hold_is_a_hold_note_on_the_first_taxiway()
    {
        var notes = new List<string>();
        var route = RouteWithHold(null, startHold: "runway 18");
        var hold = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(route, Array.Empty<TaxiRouteRunwayEvent>(), departureRunway: "09", null, notes));
        Assert.Equal("18", hold.Runway);
        Assert.Equal("A", hold.Taxiway);
        Assert.False(hold.BeforeEntering);
    }

    [Fact]
    public void An_unheld_crossing_becomes_a_note()
    {
        var notes = new List<string>();
        var events = new[] { new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "06L", Held = false } };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), events, null, null, notes);
        Assert.Contains(notes, n => n.Contains("06L", StringComparison.Ordinal) && n.Contains("no hold short point", StringComparison.Ordinal));
    }

    [Fact]
    public void Same_runway_is_reciprocal_aware()
    {
        Assert.True(TaxiBriefingPlanner.SameRunway("09", "27"));
        Assert.True(TaxiBriefingPlanner.SameRunway("9", "09"));
        Assert.True(TaxiBriefingPlanner.SameRunway("27L", "09R"));
        Assert.False(TaxiBriefingPlanner.SameRunway("09", "18"));
    }

    [Fact]
    public void Narrow_taxiway_note_only_below_the_code_minimum_and_never_for_unknown_width()
    {
        var route = RouteWithHold(null, null);
        route.Segments[0].TaxiwayName = "K"; route.Segments[0].PathWidth = 49.0;   // 14.9 m
        route.Segments[1].TaxiwayName = "A"; route.Segments[1].PathWidth = 0.0;    // unknown

        var notes = TaxiBriefingPlanner.NarrowTaxiways(route, A388);
        var k = Assert.Single(notes);
        Assert.Equal("K", k.Taxiway);
        Assert.Equal(14.9, k.WidthMetres, 1);
        Assert.Equal(25.0, k.MinimumMetres);

        Assert.Single(TaxiBriefingPlanner.NarrowTaxiways(route, B738));                                   // 14.9 m < code C's 15 m
        Assert.Empty(TaxiBriefingPlanner.NarrowTaxiways(route, AircraftSizeClass.Resolve("ZZZZ", "", null)));   // unknown letter → no minimum
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingPlannerTests"`
Expected: build error — `TaxiBriefingPlanner` not found.

- [ ] **Step 4: Implement the pure half**

```csharp
// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Computes the route briefing's expected taxi-out and taxi-in legs on a graph already built for
/// the airport (<see cref="GraphBundle"/>) — the pure half. Uses the same code taxi guidance flies
/// with (TaxiRouter, RouteRunwayCrossings, GetLandingExits, RunwayLineupTarget) so the taxiways it
/// names are the ones Taxi Assist offers later; it never touches TaxiGuidanceManager's state.
/// </summary>
public static partial class TaxiBriefingPlanner
{
    /// <summary>The aircraft counts as "parked at the origin" within this distance of the airport reference point.</summary>
    public const double OwnPositionMaxAirportDistanceMetres = 5000.0;
    /// <summary>…and only when a graph node lies this close to it.</summary>
    public const double OwnPositionMaxNodeDistanceMetres = 150.0;
    /// <summary>A stand resolves to its nearest graph node within this distance (Taxi Assist's gate rule).</summary>
    public const double StandNodeMaxDistanceMetres = 100.0;

    public static TaxiLegBriefing PlanTaxiOut(TaxiBriefingRequest r, GraphBundle g)
    {
        string icao = r.OriginIcao;
        var notes = new List<string>();
        if (g.Note != null) notes.Add(g.Note);

        var rwy = FindRunway(g.Runways, r.OriginRunway);
        if (rwy == null)
            return TaxiLegBriefing.UnavailableLeg(icao, r.OriginRunway, g.Tier,
                $"runway {r.OriginRunway} is not in the navigation database for {icao}", notes: notes);

        int startNode = -1;
        string endpoint = "";
        StandChoice? stand = null;

        if (r.Own is { OnGround: true } own && g.Airport != null &&
            TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, g.Airport.Latitude, g.Airport.Longitude) <= OwnPositionMaxAirportDistanceMetres)
        {
            // A route START: bridge-only stand stubs excluded, as TaxiGuidanceManager.LoadRoute does.
            var node = g.Graph.FindNearestNode(own.Lat, own.Lon, excludeBridgeOnlyStandStubs: true);
            if (node != null && TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, node.Latitude, node.Longitude) <= OwnPositionMaxNodeDistanceMetres)
            {
                startNode = node.NodeId;
                endpoint = string.IsNullOrEmpty(node.ParkingName) ? "current position" : $"current position, stand {node.ParkingName}";
            }
        }

        if (startNode < 0)
        {
            // SayIntentions never assigns a departure gate — no hint here by design.
            stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, siGate: null, s => StandNode(g.Graph, s) != null);
            if (stand == null)
                return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                    $"no stand at {icao} connects to the taxiway network", notes: notes);
            notes.AddRange(stand.Notes);
            startNode = StandNode(g.Graph, stand.Spot)!.NodeId;
            endpoint = $"representative stand {DescribeStand(stand, r.AirlineIcao)}";
        }

        var startNodeObj = g.Graph.Nodes[startNode];
        var startsForRunway = g.Starts.Where(s => RunwayIdsMatch(s.RunwayName, rwy.RunwayID)).ToList();
        var target = RunwayLineupTarget.Resolve(g.Graph, rwy, startsForRunway, startNodeObj.Latitude, startNodeObj.Longitude);
        if (target.EntryNode == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxiway reaches runway {rwy.RunwayID} in this scenery", stand, endpoint, notes);

        var route = new TaxiRouter(g.Graph).FindShortestPath(startNode, target.EntryNode.NodeId);
        if (route == null || route.Segments.Count == 0)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxi route connects {endpoint} to runway {rwy.RunwayID} in this scenery", stand, endpoint, notes);

        var events = RouteRunwayCrossings.InsertRunwayHoldShorts(route, g.Graph.RunwayCenterlines, $"Runway {rwy.RunwayID}", aircraft: null);
        var holds = CollectHoldShorts(route, events, departureRunway: rwy.RunwayID, landedRunway: null, notes);
        // The route ENDS on the departure runway, so the automatic pass records no event for that entry
        // (it is the destination strip); the hold before entering is always real and always the last leg.
        if (!holds.Any(h => h.BeforeEntering))
            holds.Add(new HoldShortNote(rwy.RunwayID, LastNamedTaxiway(route), BeforeEntering: true));

        return new TaxiLegBriefing
        {
            Icao = icao, Runway = rwy.RunwayID, Tier = g.Tier, EndpointDescription = endpoint, Stand = stand,
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments),
            DistanceMetres = route.TotalDistanceMeters, HoldShorts = holds,
            NarrowTaxiways = NarrowTaxiways(route, r.Aircraft), Notes = notes,
        };
    }

    public static TaxiLegBriefing PlanTaxiIn(TaxiBriefingRequest r, GraphBundle g)
    {
        string icao = r.DestinationIcao;
        var notes = new List<string>();
        if (g.Note != null) notes.Add(g.Note);

        var rwy = FindRunway(g.Runways, r.DestinationRunway);
        if (rwy == null)
            return TaxiLegBriefing.UnavailableLeg(icao, r.DestinationRunway, g.Tier,
                $"runway {r.DestinationRunway} is not in the navigation database for {icao}", notes: notes);

        var stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, r.ArrivalGate, s => StandNode(g.Graph, s) != null);
        if (stand != null) notes.AddRange(stand.Notes);
        string endpoint = stand == null ? "" : DescribeArrivalStand(stand, r.AirlineIcao);

        var exits = g.Graph.GetLandingExits(rwy);
        LandingExitVacateScreen.Mark(g.Graph, exits, rwy);
        var vacating = exits.Where(e => e.VacatesRunway).OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        var choice = BriefingExitPicker.Pick(exits, r.Aircraft.TouchdownSpeedKts);
        if (choice == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no exit taxiway is mapped clear of runway {rwy.RunwayID} in this scenery", stand, endpoint, notes, vacating);
        if (stand == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no stand at {icao} connects to the taxiway network", null, endpoint, notes, vacating, choice);

        int from = LandingExitDestination.Resolve(g.Graph, choice.Exit, exits, rwy, rwy.Heading, out _, out _, out _);
        int to = StandNode(g.Graph, stand.Spot)!.NodeId;
        var route = new TaxiRouter(g.Graph).FindShortestPath(from, to);
        if (route == null || route.Segments.Count == 0)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxi route connects exit {choice.Exit.TaxiwayName} to {DescribeStand(stand, r.AirlineIcao)} in this scenery",
                stand, endpoint, notes, vacating, choice);

        var events = RouteRunwayCrossings.InsertRunwayHoldShorts(route, g.Graph.RunwayCenterlines, "", aircraft: null);
        var holds = CollectHoldShorts(route, events, departureRunway: null, landedRunway: rwy.RunwayID, notes);

        return new TaxiLegBriefing
        {
            Icao = icao, Runway = rwy.RunwayID, Tier = g.Tier, EndpointDescription = endpoint, Stand = stand,
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments),
            DistanceMetres = route.TotalDistanceMeters, HoldShorts = holds, Exit = choice, VacatingExits = vacating,
            NarrowTaxiways = NarrowTaxiways(route, r.Aircraft), Notes = notes,
        };
    }

    /// <summary>
    /// Hold-short notes from the route's flagged segments and its start hold. A hold naming the runway
    /// just landed on (either end) is DISCARDED — the aircraft has just vacated it. An event the pass
    /// could not hold becomes a note, never a silent gap.
    /// </summary>
    internal static List<HoldShortNote> CollectHoldShorts(TaxiRoute route, IReadOnlyList<TaxiRouteRunwayEvent> events,
                                                          string? departureRunway, string? landedRunway, List<string> notes)
    {
        var holds = new List<HoldShortNote>();
        if (route.StartHoldRunway is string startHold)
        {
            foreach (var d in RouteRunwayCrossings.ExtractRunwayDesignators(startHold))
            {
                if (landedRunway != null && SameRunway(d, landedRunway)) continue;
                holds.Add(new HoldShortNote(d, NamedTaxiwayAt(route, 0), departureRunway != null && SameRunway(d, departureRunway)));
            }
        }
        for (int i = 0; i < route.Segments.Count; i++)
        {
            var seg = route.Segments[i];
            if (!seg.IsHoldShortPoint || string.IsNullOrEmpty(seg.HoldShortRunway)) continue;
            foreach (var d in RouteRunwayCrossings.ExtractRunwayDesignators(seg.HoldShortRunway))
            {
                if (landedRunway != null && SameRunway(d, landedRunway)) continue;
                if (holds.Any(h => SameRunway(h.Runway, d))) continue;
                holds.Add(new HoldShortNote(d, NamedTaxiwayAt(route, i), departureRunway != null && SameRunway(d, departureRunway)));
            }
        }
        foreach (var e in events)
        {
            if (e.Held) continue;
            if (landedRunway != null && SameRunway(e.Designator, landedRunway)) continue;
            notes.Add($"no hold short point could be placed for runway {e.Designator}; cross with care");
        }
        return holds;
    }

    /// <summary>Advisory: taxiways on the route whose navdata width is below the code letter's Annex 14
    /// minimum. Width 0 (unknown; every OpenStreetMap edge) never produces a note.</summary>
    internal static List<NarrowTaxiwayNote> NarrowTaxiways(TaxiRoute route, AircraftProfile aircraft)
    {
        var result = new List<NarrowTaxiwayNote>();
        double min = AircraftSizeClass.MinTaxiwayWidthMetres(aircraft.CodeLetter);
        if (min <= 0) return result;
        foreach (var group in route.Segments
                     .Where(s => !string.IsNullOrEmpty(s.TaxiwayName) && s.PathWidth > 0)
                     .GroupBy(s => s.TaxiwayName, StringComparer.OrdinalIgnoreCase))
        {
            double widthM = group.Min(s => s.PathWidth) * 0.3048;
            if (widthM < min) result.Add(new NarrowTaxiwayNote(group.Key, widthM, min));
        }
        return result;
    }

    internal static bool SameRunway(string a, string b)
    {
        string na = Canon(a), nb = Canon(b);
        return na == nb || Canon(RouteRunwayCrossings.Reciprocal(a)) == nb;
    }

    /// <summary>"9" and "09" are one runway: CleanRunway pads the number; NormalizeDesignator is the fallback for a
    /// compass-point designator it cannot parse.</summary>
    private static string Canon(string d) =>
        SayIntentionsClearanceParser.CleanRunway(d) ?? RouteRunwayCrossings.NormalizeDesignator(d);

    internal static bool RunwayIdsMatch(string? a, string? b)
    {
        string? ca = SayIntentionsClearanceParser.CleanRunway(a);
        string? cb = SayIntentionsClearanceParser.CleanRunway(b);
        return ca != null && cb != null && string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase);
    }

    internal static Runway? FindRunway(IReadOnlyList<Runway> runways, string id) =>
        runways.FirstOrDefault(r => RunwayIdsMatch(r.RunwayID, id));

    /// <summary>A DESTINATION lookup: bridge-only stand stubs must stay reachable, so no exclusion here.</summary>
    internal static TaxiNode? StandNode(TaxiGraph graph, ParkingSpot spot)
    {
        var node = graph.FindNearestNode(spot.Latitude, spot.Longitude);
        if (node == null) return null;
        return TaxiGraph.FastDistanceMeters(spot.Latitude, spot.Longitude, node.Latitude, node.Longitude) <= StandNodeMaxDistanceMetres ? node : null;
    }

    /// <summary>"C 1 (Ramp Cargo, UPS)" — identity, then the type when known, then the airline when it decided.</summary>
    internal static string DescribeStand(StandChoice stand, string? airlineIcao)
    {
        var parts = new List<string>();
        if (stand.Spot.Type > 1) parts.Add(stand.Spot.GetParkingType());
        if (stand.Source == StandChoiceSource.AirlineMatch && !string.IsNullOrWhiteSpace(airlineIcao)) parts.Add(airlineIcao.Trim().ToUpperInvariant());
        string label = BriefingStandPicker.IdentityLabel(stand.Spot);
        return parts.Count == 0 ? label : $"{label} ({string.Join(", ", parts)})";
    }

    private static string DescribeArrivalStand(StandChoice stand, string? airlineIcao) =>
        stand.Source == StandChoiceSource.SayIntentions
            ? $"SayIntentions assigned gate {BriefingStandPicker.IdentityLabel(stand.Spot)}"
            : $"representative stand {DescribeStand(stand, airlineIcao)}";

    private static string NamedTaxiwayAt(TaxiRoute route, int index)
    {
        for (int i = Math.Min(index, route.Segments.Count - 1); i >= 0; i--)
            if (!string.IsNullOrEmpty(route.Segments[i].TaxiwayName)) return route.Segments[i].TaxiwayName;
        for (int i = index + 1; i < route.Segments.Count; i++)
            if (!string.IsNullOrEmpty(route.Segments[i].TaxiwayName)) return route.Segments[i].TaxiwayName;
        return "";
    }

    private static string LastNamedTaxiway(TaxiRoute route) => NamedTaxiwayAt(route, route.Segments.Count - 1);
}
```

- [ ] **Step 5: Run the tests; debug the fixture before the code**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingPlannerTests"`
Expected: all pass. If a synthetic-airport assertion fails, add a temporary test that prints `bundle.Graph.GetLandingExits(rwy)` (name, distance, side, VacatesRunway), the route's `Segments` (taxiway, hold flag, label) and `RunwayEvents`, and compare against the fixture comment; the likely fixture faults are the HS markers on the exit junction nodes, the 500 ft exit floor and the runway `Length`/`Width`. The `EndpointDescription` for the current-position case depends on `TaxiGraph.FormatParkingName` — only the `"current position"` prefix is asserted.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs tests/MSFSBlindAssist.Tests/TaxiBriefingFixture.cs tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs
git commit -m "feat(briefing): plan the expected taxi-out and taxi-in legs on the airport graph"
```

---
### Task 11: `TaxiBriefingRenderer`

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs`
- Test: `tests/MSFSBlindAssist.Tests/TaxiBriefingRendererTests.cs`

**Interfaces:**
- Consumes: Task 7 models, `ExitChoice` (Task 3), `LandingExit`, `AircraftProfile`.
- Produces: `public static class TaxiBriefingRenderer { public const string Header = "TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)"; public const int MaxListedExits = 12; public static string Render(TaxiBriefing b); public static string TierLabel(BriefingTier tier); public static string FormatDistance(double metres); }`. Output uses `\n` line ends, no trailing newline.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/TaxiBriefingRendererTests.cs
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
            HoldShorts = new[] { new HoldShortNote("26", "A", true) }, Notes = new[] { OsmPlanningGraph.Note },
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
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingRendererTests"`
Expected: build error — `TaxiBriefingRenderer` not found.

- [ ] **Step 3: Implement**

```csharp
// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs
using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// The plain-text TAXI ROUTES block the AI receives (appended to the SimBrief flight data for one
/// Describe Route call). Every line is data for the prompt's section 7: it names the tier a leg came
/// from, keeps taxiway/exit/stand names exactly as the source spells them, and states every caveat.
/// InvariantCulture throughout; "\n" line ends; no markdown.
/// </summary>
public static class TaxiBriefingRenderer
{
    public const string Header = "TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)";
    public const string OsmLabel = "OpenStreetMap, planning only — taxi guidance cannot use this";
    /// <summary>The exits list names the nearest this many and counts the rest.</summary>
    public const int MaxListedExits = 12;

    public static string Render(TaxiBriefing b)
    {
        var lines = new List<string> { Header, AircraftLine(b.Aircraft) };
        RenderTaxiOut(b.TaxiOut, b.Aircraft, lines);
        RenderTaxiIn(b.TaxiIn, b.Aircraft, lines);
        return string.Join("\n", lines);
    }

    public static string TierLabel(BriefingTier tier) => tier switch
    {
        BriefingTier.Navdata => "scenery navdata",
        BriefingTier.OpenStreetMap => OsmLabel,
        _ => "",
    };

    public static string FormatDistance(double metres) => metres < 1000
        ? $"{Math.Round(metres).ToString("0", CultureInfo.InvariantCulture)} m"
        : $"{(metres / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} km";

    private static string AircraftLine(AircraftProfile a)
    {
        string size = a.CodeLetter == IcaoCodeLetter.Unknown ? "size class unknown" : $"size class {a.CodeLetter}";
        string span = a.WingspanMetres is double m
            ? $"wingspan {m.ToString("0.0", CultureInfo.InvariantCulture)} m"
            : "wingspan unknown (aircraft type not recognised)";
        string role = a.IsFreighter ? "freighter: cargo stands preferred" : "passenger";
        string code = string.IsNullOrEmpty(a.TypeCode) ? "unknown" : a.TypeCode;
        return $"Aircraft: {a.DisplayName} (SimBrief type {code}), {size}, {span}, {role}";
    }

    private static void RenderTaxiOut(TaxiLegBriefing leg, AircraftProfile aircraft, List<string> lines)
    {
        if (leg.Unavailable != null)
        {
            lines.Add($"TAXI OUT at {leg.Icao}: taxi route unavailable — {leg.Unavailable}");
            if (leg.EndpointDescription.Length > 0) lines.Add($"  Start: {leg.EndpointDescription}");
        }
        else
        {
            lines.Add($"TAXI OUT at {leg.Icao} ({TierLabel(leg.Tier)}): from {leg.EndpointDescription} to runway {leg.Runway}");
            lines.Add($"  Taxiways: {JoinNames(leg.Taxiways)} ({FormatDistance(leg.DistanceMetres)})");
            lines.Add(HoldLine(leg.HoldShorts));
            foreach (var n in leg.NarrowTaxiways) lines.Add(NarrowLine(n, aircraft.CodeLetter));
        }
        foreach (var note in leg.Notes) lines.Add($"  Note: {note}");
    }

    private static void RenderTaxiIn(TaxiLegBriefing leg, AircraftProfile aircraft, List<string> lines)
    {
        if (leg.Unavailable != null)
        {
            lines.Add($"TAXI IN at {leg.Icao}: taxi route unavailable — {leg.Unavailable}");
            if (leg.Exit != null) lines.Add(ExitLine(leg.Exit, aircraft));
            if (leg.EndpointDescription.Length > 0) lines.Add($"  Stand: {leg.EndpointDescription}");
            lines.Add(ExitsListLine(leg));
        }
        else
        {
            lines.Add($"TAXI IN at {leg.Icao} ({TierLabel(leg.Tier)}), landing runway {leg.Runway}");
            if (leg.Exit != null) lines.Add(ExitLine(leg.Exit, aircraft));
            lines.Add($"  Stand: {leg.EndpointDescription}");
            lines.Add($"  Taxiways from the exit: {JoinNames(leg.Taxiways)} ({FormatDistance(leg.DistanceMetres)})");
            lines.Add(HoldLine(leg.HoldShorts));
            foreach (var n in leg.NarrowTaxiways) lines.Add(NarrowLine(n, aircraft.CodeLetter));
            lines.Add(ExitsListLine(leg));
        }
        foreach (var note in leg.Notes) lines.Add($"  Note: {note}");
    }

    private static string HoldLine(IReadOnlyList<HoldShortNote> holds)
    {
        if (holds.Count == 0) return "  No runway crossings on this route.";
        var parts = holds.Select(h =>
            $"runway {h.Runway} on taxiway {h.Taxiway} ({(h.BeforeEntering ? "before entering" : "crossing")})");
        return "  Hold short: " + string.Join("; ", parts);
    }

    private static string NarrowLine(NarrowTaxiwayNote n, IcaoCodeLetter letter) =>
        $"  Taxiway width note: taxiway {n.Taxiway} is {n.WidthMetres.ToString("0.0", CultureInfo.InvariantCulture)} m in the navdata, " +
        $"below the {n.MinimumMetres.ToString("0.0", CultureInfo.InvariantCulture)} m code {letter} minimum";

    private static string ExitLine(ExitChoice choice, AircraftProfile aircraft)
    {
        var e = choice.Exit;
        var sb = new StringBuilder();
        sb.Append($"  Expected exit: taxiway {e.TaxiwayName}, {e.ExitType.ToLowerInvariant()}, {SideUpper(e.ExitSide)}, {Feet(e.DistanceFromThresholdFeet)} ft from the threshold.");
        sb.Append(choice.NextExit is { } n
            ? $" Next exit if missed: {n.TaxiwayName}, {SideLower(n.ExitSide)}, {Feet(n.DistanceFromThresholdFeet)} ft"
            : " No later exit is mapped.");
        if (!choice.ComfortablyReachable)
            sb.Append($" This runway is short for this aircraft: no exit is comfortably reachable at {aircraft.TouchdownSpeedKts.ToString("0", CultureInfo.InvariantCulture)} kt; the last exit is briefed.");
        return sb.ToString();
    }

    private static string ExitsListLine(TaxiLegBriefing leg)
    {
        if (leg.VacatingExits.Count == 0) return $"  Exits on {leg.Runway} that get clear of the runway: none found";
        var shown = leg.VacatingExits.Take(MaxListedExits)
            .Select(e => $"{e.TaxiwayName} ({Feet(e.DistanceFromThresholdFeet)} ft, {SideLowerBare(e.ExitSide)}, {e.ExitType.ToLowerInvariant()})");
        string list = string.Join(", ", shown);
        int more = leg.VacatingExits.Count - MaxListedExits;
        if (more > 0) list += $", … and {more} more";
        return $"  Exits on {leg.Runway} that get clear of the runway: {list}";
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count == 0 ? "(unnamed)" : string.Join(", ", names);
    private static string Feet(double ft) => Math.Round(ft).ToString("N0", CultureInfo.InvariantCulture);
    private static string SideUpper(string side) => string.IsNullOrEmpty(side) ? "side unknown" : $"{side.ToUpperInvariant()} side";
    private static string SideLower(string side) => string.IsNullOrEmpty(side) ? "side unknown" : $"{side.ToLowerInvariant()} side";
    private static string SideLowerBare(string side) => string.IsNullOrEmpty(side) ? "side unknown" : side.ToLowerInvariant();
}
```

- [ ] **Step 4: Run to verify they pass**

Same filter. Expected: 5 passed. (`"N0"` with InvariantCulture renders `6,200`; check the em-dash and ellipsis characters are the ones in the expected strings — copy them from the test.)

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs tests/MSFSBlindAssist.Tests/TaxiBriefingRendererTests.cs
git commit -m "feat(briefing): render the TAXI ROUTES block the AI narrates"
```

---
### Task 12: `TaxiBriefingGraphSource` and `TaxiBriefingPlanner.PlanAsync`

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingGraphSource.cs`
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs` (add the async half — the class is `partial`; put it in a new file `TaxiBriefingPlanner.Async.cs` beside it)
- Test: `tests/MSFSBlindAssist.Tests/TaxiBriefingGraphSourceTests.cs`

**Interfaces:**
- Consumes: `IAirportDataProvider` (`DatabaseExists`, `GetAirport`, `GetRunways`, `GetRunwayStarts`, `GetTaxiPaths`), `ParkingSpotSource.GetNamedSpots(IAirportDataProvider, GateDataSource?, string)`, `AugmentingAirportDataProvider.Enabled` / `.GetOnlineTaxiDataAsync(icao, ct)` (Task 6), `OsmPlanningGraph.Build` (Task 9), Task 10's `PlanTaxiOut`/`PlanTaxiIn`, `Log.Info/Warn`.
- Produces:
  - `public static class TaxiBriefingGraphSource { public static Task<(GraphBundle? Bundle, string? Reason)> BuildAsync(IAirportDataProvider provider, GateDataSource? gateSource, string icao, CancellationToken ct); }`
  - `TaxiBriefingPlanner.DefaultBudget : TimeSpan` (20 s); `public static Task<TaxiBriefing> PlanAsync(TaxiBriefingRequest request, IAirportDataProvider? provider, GateDataSource? gateSource, TimeSpan budget, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/TaxiBriefingGraphSourceTests.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.TaxiAugment;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

public class TaxiBriefingGraphSourceTests
{
    /// <summary>A navdata provider serving the TEST fixture airport, with or without taxi paths.</summary>
    private sealed class FakeProvider : IAirportDataProvider
    {
        public bool HasTaxiPaths = true;
        public bool HasAirport = true;
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => HasAirport ? AirportRef() : null;
        public List<Runway> GetRunways(string icao) => Runways();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => Spots();
        public bool AirportExists(string icao) => HasAirport;
        public int GetAirportCount() => 1;
        public int GetRunwayCount() => 4;
        public int GetParkingSpotCount() => 3;
        public HashSet<string> GetAllAirportICAOs() => new() { "TEST" };
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => HasTaxiPaths ? Paths() : new List<TaxiPath>();
        public List<StartPosition> GetRunwayStarts(string icao) => Starts();
    }

    [Fact]
    public async Task Navdata_with_taxiways_is_tier_one()
    {
        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(new FakeProvider(), null, "TEST", CancellationToken.None);

        Assert.Null(reason);
        Assert.Equal(BriefingTier.Navdata, bundle!.Tier);
        Assert.Equal(3, bundle.Spots.Count);
        Assert.Equal("TEST", bundle.Airport!.ICAO);
    }

    [Fact]
    public async Task Unknown_airport_is_a_reason()
    {
        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(new FakeProvider { HasAirport = false }, null, "ZZZZ", CancellationToken.None);
        Assert.Null(bundle);
        Assert.Equal("ZZZZ is not in the navigation database", reason);
    }

    [Fact]
    public async Task No_taxiways_and_a_plain_provider_means_no_online_fallback()
    {
        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(new FakeProvider { HasTaxiPaths = false }, null, "TEST", CancellationToken.None);
        Assert.Null(bundle);
        Assert.Equal("the navigation database has no taxiways for TEST and OpenStreetMap data is not available", reason);
    }

    [Fact]
    public async Task No_taxiways_with_cached_osm_data_is_tier_two()
    {
        var cache = new TaxiDataCache(ttlDays: 1);
        var osm = new AirportTaxiData { Source = "osm" };
        osm.Taxiways.Add(new NamedTaxiSegment { Name = "A", Lat1 = Lat(100), Lon1 = Lon(0), Lat2 = Lat(100), Lon2 = Lon(3000) });
        cache.Save("TEST", new[] { osm });
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { HasTaxiPaths = false }, cache, Array.Empty<ITaxiDataSource>(), new MergeOptions());

        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);

        Assert.Null(reason);
        Assert.Equal(BriefingTier.OpenStreetMap, bundle!.Tier);
        Assert.Equal(OsmPlanningGraph.Note, bundle.Note);
    }

    [Fact]
    public async Task No_taxiways_with_augmentation_disabled_says_so()
    {
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { HasTaxiPaths = false }, new TaxiDataCache(1), Array.Empty<ITaxiDataSource>(), new MergeOptions()) { Enabled = false };
        var (_, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);
        Assert.Equal("the navigation database has no taxiways for TEST and online taxi data is disabled in settings", reason);
    }

    [Fact]
    public async Task Plan_async_answers_both_legs_and_survives_a_null_provider()
    {
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL");

        var full = await TaxiBriefingPlanner.PlanAsync(request, new FakeProvider(), null, TaxiBriefingPlanner.DefaultBudget);
        Assert.Null(full.TaxiOut.Unavailable);
        Assert.Null(full.TaxiIn.Unavailable);
        Assert.Equal("C", full.TaxiIn.Exit!.Exit.TaxiwayName);

        var none = await TaxiBriefingPlanner.PlanAsync(request, null, null, TaxiBriefingPlanner.DefaultBudget);
        Assert.Equal("no navigation database loaded", none.TaxiOut.Unavailable);
        Assert.Equal("no navigation database loaded", none.TaxiIn.Unavailable);
    }

    [Fact]
    public async Task Plan_async_reports_a_leg_that_throws_instead_of_failing_the_briefing()
    {
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189));
        var throwing = new ThrowingProvider();
        var b = await TaxiBriefingPlanner.PlanAsync(request, throwing, null, TaxiBriefingPlanner.DefaultBudget);
        Assert.StartsWith("taxi route could not be computed (", b.TaxiOut.Unavailable);
        Assert.StartsWith("taxi route could not be computed (", b.TaxiIn.Unavailable);
    }

    private sealed class ThrowingProvider : IAirportDataProvider
    {
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => throw new InvalidOperationException("boom");
        public List<Runway> GetRunways(string icao) => throw new InvalidOperationException("boom");
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => new();
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingGraphSourceTests"`
Expected: build error.

- [ ] **Step 3: Implement the graph source**

```csharp
// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingGraphSource.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which ground data a briefing leg is planned on: tier 1 the navigation database (the graph built
/// exactly as LandingExitForm builds it — GetNamedSpots, never raw GetParkingSpots), tier 2 the
/// OpenStreetMap planning-only graph when the database has NO taxiways for the airport, otherwise
/// nothing with a pilot-readable reason. Runs on the caller's thread (PlanAsync puts it on a
/// background one): every call is a database read or a graph build.
/// </summary>
public static class TaxiBriefingGraphSource
{
    public static async Task<(GraphBundle? Bundle, string? Reason)> BuildAsync(
        IAirportDataProvider provider, GateDataSource? gateSource, string icao, CancellationToken ct)
    {
        var airport = provider.GetAirport(icao);
        if (airport == null) return (null, $"{icao} is not in the navigation database");

        var runways = provider.GetRunways(icao);
        var starts = provider.GetRunwayStarts(icao);
        var paths = provider.GetTaxiPaths(icao);
        if (paths.Count > 0)
        {
            var spots = ParkingSpotSource.GetNamedSpots(provider, gateSource, icao);
            var graph = TaxiGraph.Build(paths, spots, starts, runways);
            return (new GraphBundle(graph, BriefingTier.Navdata, runways, starts, spots, null, airport), null);
        }

        string noNav = $"the navigation database has no taxiways for {icao}";
        if (provider is not AugmentingAirportDataProvider augmenting)
            return (null, noNav + " and OpenStreetMap data is not available");
        if (!augmenting.Enabled)
            return (null, noNav + " and online taxi data is disabled in settings");

        var online = await augmenting.GetOnlineTaxiDataAsync(icao, ct).ConfigureAwait(false);
        if (online == null) return (null, noNav + " and OpenStreetMap data is not yet available");

        var osm = OsmPlanningGraph.Build(online, runways, starts, airport);
        return osm == null ? (null, noNav + " and OpenStreetMap has no named taxiways for it") : (osm, null);
    }
}
```

- [ ] **Step 4: Implement `PlanAsync`**

```csharp
// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.Async.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Navigation.Briefing;

public static partial class TaxiBriefingPlanner
{
    /// <summary>Whole-briefing budget for both airports' database reads, graph builds and routing.
    /// A leg that overruns reports "timed out"; the AI briefing never waits longer than this.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(20);

    private const string LogCategory = "taxi_briefing";

    /// <summary>
    /// Both legs, each computed on a background thread inside its own try/catch, so a failure or a
    /// timeout on one leg becomes that leg's <see cref="TaxiLegBriefing.Unavailable"/> and never an
    /// exception to the caller. A null or absent database makes both legs unavailable with one reason.
    /// </summary>
    public static async Task<TaxiBriefing> PlanAsync(TaxiBriefingRequest request, IAirportDataProvider? provider,
                                                     GateDataSource? gateSource, TimeSpan budget, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        var token = cts.Token;

        var taxiOut = await PlanLegSafelyAsync(request.OriginIcao, request.OriginRunway, provider, gateSource, token,
            g => PlanTaxiOut(request, g)).ConfigureAwait(false);
        var taxiIn = await PlanLegSafelyAsync(request.DestinationIcao, request.DestinationRunway, provider, gateSource, token,
            g => PlanTaxiIn(request, g)).ConfigureAwait(false);
        return new TaxiBriefing(request.Aircraft, taxiOut, taxiIn);
    }

    private static async Task<TaxiLegBriefing> PlanLegSafelyAsync(string icao, string runway, IAirportDataProvider? provider,
        GateDataSource? gateSource, CancellationToken ct, Func<GraphBundle, TaxiLegBriefing> plan)
    {
        if (provider == null || !provider.DatabaseExists)
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "no navigation database loaded");
        if (string.IsNullOrWhiteSpace(icao))
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "no airport in the flight plan");

        try
        {
            // WaitAsync bounds the wait even when the work inside ignores the token (a graph build
            // cannot be cancelled); an abandoned build finishes in the background and is discarded.
            return await Task.Run(async () =>
            {
                var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(provider, gateSource, icao, ct).ConfigureAwait(false);
                if (bundle == null)
                {
                    Log.Info(LogCategory, $"{icao} {runway}: no graph — {reason}");
                    return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, reason ?? "no ground data for this airport");
                }
                ct.ThrowIfCancellationRequested();
                var leg = plan(bundle);
                Log.Info(LogCategory, Summarise(leg));
                return leg;
            }, ct).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Warn(LogCategory, $"{icao} {runway}: taxi route computation timed out");
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "taxi route computation timed out");
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"{icao} {runway}: {ex}");
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, $"taxi route could not be computed ({ex.Message})");
        }
    }

    private static string Summarise(TaxiLegBriefing leg) => leg.Unavailable != null
        ? $"{leg.Icao} {leg.Runway} tier={leg.Tier} unavailable=\"{leg.Unavailable}\""
        : $"{leg.Icao} {leg.Runway} tier={leg.Tier} endpoint=\"{leg.EndpointDescription}\" taxiways=[{string.Join(",", leg.Taxiways)}] " +
          $"holds=[{string.Join(";", leg.HoldShorts.Select(h => $"{h.Runway}@{h.Taxiway}{(h.BeforeEntering ? "(entry)" : "")}"))}] " +
          $"exit={leg.Exit?.Exit.TaxiwayName ?? "-"} next={leg.Exit?.NextExit?.TaxiwayName ?? "-"} distM={leg.DistanceMetres:0}";
}
```

- [ ] **Step 5: Run to verify they pass; build the solution**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingGraphSourceTests"` then `dotnet build MSFSBlindAssist.sln -c Debug`.
Expected: 7 passed; build succeeded.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/TaxiBriefingGraphSource.cs MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.Async.cs tests/MSFSBlindAssist.Tests/TaxiBriefingGraphSourceTests.cs
git commit -m "feat(briefing): tiered graph source (navdata, OpenStreetMap planning-only) and the bounded PlanAsync"
```

---

### Task 13: Wire the EFB and MainForm

**Files:**
- Modify: `MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs` (fields ~line 24–28, constructor ~line 77, `DescribeRouteAsync` ~line 857)
- Modify: `MSFSBlindAssist/MainForm.Dialogs.cs` (~line 623, the `new ElectronicFlightBagForm(...)` call)
- No unit test (UI + SimConnect); build must succeed and the in-sim plan in the final report covers it.

**Interfaces:**
- Consumes: `RouteBriefingDependencies` (Task 7), `SayIntentionsArrivalGate.From` (Task 8), `AircraftSizeClass.Resolve` (Task 2), `TaxiBriefingPlanner.PlanAsync`/`DefaultBudget` (Task 12), `TaxiBriefingRenderer.Render` (Task 11), `TaxiBriefing.Unavailable` (Task 7), `SimConnectManager.RequestAircraftPositionAsync(Action<AircraftPosition>)` with `AircraftPosition { Latitude, Longitude, SimOnGround }`, `SimConnectManager.IsConnected`, `FlightPlan` fields (Task 1), MainForm's `airportDataProvider` (`IAirportDataProvider?`), `BuildGateDataSource()` (`GateDataSource?`), `sayIntentionsService.ReadFlightContextAsync()`.

- [ ] **Step 1: EFB constructor and field**

Add the using directives `using MSFSBlindAssist.Navigation.Briefing;` and `using MSFSBlindAssist.Utils.Logging;` (if not already present). Add a field beside the others:

```csharp
    // What the taxi section of the route briefing needs from MainForm; null when the form is built
    // without it (tests), in which case the block says no navigation database is loaded.
    private readonly RouteBriefingDependencies? _briefingDependencies;
```

Change the constructor signature to add a trailing optional parameter and store it:

```csharp
    public ElectronicFlightBagForm(FlightPlanManager flightPlanManager, SimConnectManager simConnectManager,
                                   ScreenReaderAnnouncer announcer, WaypointTracker waypointTracker, string simbriefUsername,
                                   RouteBriefingDependencies? briefingDependencies = null)
    {
        _flightPlanManager = flightPlanManager;
        _simConnectManager = simConnectManager;
        _announcer = announcer;
        _waypointTracker = waypointTracker;
        _simbriefUsername = simbriefUsername;
        _briefingDependencies = briefingDependencies;
```

- [ ] **Step 2: Compute the block in `DescribeRouteAsync`**

Replace the two lines

```csharp
            var aiProvider = AiProviderFactory.Create();
            string description = await aiProvider.DescribeRouteAsync(
                _flightPlanManager.CurrentFlightPlan.ExtractedFlightData);
```

with (keep the comment block above them about resolving the provider per call):

```csharp
            // The taxi section: computed from the pilot's own scenery for THIS press and appended to the
            // flight data for this AI call only — the stored ExtractedFlightData stays pure SimBrief,
            // since the facts change when a runway is edited or the aircraft moves. Never blocks the
            // briefing: every failure renders as an "unavailable" line inside the block.
            UpdateStatus("Computing taxi routes...");
            string taxiBlock = await BuildTaxiRoutesBlockAsync();
            UpdateStatus("Generating route description...");
            string flightData = _flightPlanManager.CurrentFlightPlan.ExtractedFlightData + "\n\n" + taxiBlock;

            var aiProvider = AiProviderFactory.Create();
            string description = await aiProvider.DescribeRouteAsync(flightData);
```

Add these methods to the form:

```csharp
    private const int OwnPositionTimeoutMs = 1500;

    private async Task<string> BuildTaxiRoutesBlockAsync()
    {
        var plan = _flightPlanManager.CurrentFlightPlan;
        var aircraft = AircraftSizeClass.Resolve(plan.AircraftTypeIcao, plan.AircraftName, plan.AircraftMaxPassengers);
        try
        {
            var provider = _briefingDependencies?.Provider();
            var gateSource = _briefingDependencies?.GateSource();
            var own = await ReadOwnPositionAsync();
            var siContext = _briefingDependencies == null ? null : await _briefingDependencies.SayIntentions();
            var siGate = SayIntentionsArrivalGate.From(siContext, plan.DepartureICAO, plan.ArrivalICAO);

            var request = new TaxiBriefingRequest(plan.DepartureICAO, plan.DepartureRunway, plan.ArrivalICAO, plan.ArrivalRunway,
                                                  aircraft, plan.AirlineIcao, own, siGate);
            var briefing = await TaxiBriefingPlanner.PlanAsync(request, provider, gateSource, TaxiBriefingPlanner.DefaultBudget);
            return TaxiBriefingRenderer.Render(briefing);
        }
        catch (Exception ex)
        {
            Log.Warn("taxi_briefing", $"taxi routes block failed: {ex}");
            return TaxiBriefingRenderer.Render(TaxiBriefing.Unavailable(aircraft, plan.DepartureICAO, plan.DepartureRunway,
                plan.ArrivalICAO, plan.ArrivalRunway, $"taxi route could not be computed ({ex.Message})"));
        }
    }

    /// <summary>The aircraft's position for the "parked at the origin" test — null when not connected or
    /// when SimConnect does not answer within <see cref="OwnPositionTimeoutMs"/> (the callback lands on the
    /// UI message pump, which this await yields to).</summary>
    private async Task<OwnPosition?> ReadOwnPositionAsync()
    {
        if (_simConnectManager?.IsConnected != true) return null;
        var tcs = new TaskCompletionSource<OwnPosition?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _simConnectManager.RequestAircraftPositionAsync(p =>
            tcs.TrySetResult(new OwnPosition(p.Latitude, p.Longitude, p.SimOnGround > 0.5)));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(OwnPositionTimeoutMs));
        return completed == tcs.Task ? tcs.Task.Result : null;
    }
```

- [ ] **Step 3: Pass the dependencies from MainForm**

In `MainForm.Dialogs.cs`, change the construction to:

```csharp
            electronicFlightBagForm = new ElectronicFlightBagForm(flightPlanManager, simConnectManager, announcer, waypointTracker,
                settings.SimbriefUsername ?? "",
                new MSFSBlindAssist.Navigation.Briefing.RouteBriefingDependencies(
                    () => airportDataProvider,            // a getter: RefreshDatabaseProvider swaps the instance
                    BuildGateDataSource,
                    () => sayIntentionsService.ReadFlightContextAsync()));
```

- [ ] **Step 4: Build and run the whole suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` then `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
Expected: build succeeded; every test passes. Confirm `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` has a fresh LastWriteTime.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs MSFSBlindAssist/MainForm.Dialogs.cs
git commit -m "feat(efb): add the computed taxi-out/taxi-in routes to the AI route briefing"
```

---

### Task 14: Docs, invariants, final verification

**Files:**
- Modify: `docs/gemini.md` (append a section), `docs/taxi-guidance.md` (append a paragraph under "## Integration Points"), `CLAUDE.md` (two bullets in the "### Gemini AI" invariants group)
- Changelog fragment: NOT yet — see step 4.

- [ ] **Step 1: `docs/gemini.md` — append**

```markdown
## Taxi routes in the route briefing (2026-09-25)

The EFB's Describe Route briefing ends with a **TAXI OUT AND TAXI IN** section. The app computes
the expected taxi-out (stand → departure runway) and taxi-in (landing exit → stand) routes from the
pilot's own scenery data and appends a plain-text `TAXI ROUTES` block to the SimBrief flight data
for that AI call only (`ElectronicFlightBagForm.BuildTaxiRoutesBlockAsync`; the stored
`ExtractedFlightData` stays pure SimBrief). The prompt's section 7 makes the AI narrate the computed
route using only the names in the block, then answer the owner's real-world template ("Provide the
step-by-step taxi route at [ICAO] from [runway] to [terminal/gate] in a [aircraft type]…") from its
own knowledge under a "Real-world practice" heading, saying where the two differ and which is which.

Everything lives in `Navigation/Briefing/`:

- **Tiers per airport** (`TaxiBriefingGraphSource`): navdata (the graph built exactly as
  `LandingExitForm` builds it), then `OsmPlanningGraph` — a throw-away graph from the cached
  OpenStreetMap/apt.dat data when the database has NO taxiways for that airport (a local of the
  planner, labelled "planning only — taxi guidance cannot use this", never stored anywhere
  guidance can reach; `TaxiDataMerger`'s never-steer-on-online-geometry rule is untouched) — then
  nothing, with a reason, in which case the AI's real-world route is the leg's answer.
- **Aircraft** (`AircraftSizeClass`): SimBrief type/name/`max_passengers` ONLY (owner's choice):
  a wingspan table → Annex 14 code letter → typical touchdown speed; freighter when
  `max_passengers` is 0, the code is a known freighter code, or the name says F/BCF/BDSF/SF/PF/
  Freighter/Cargo. Unknown type → no fit filter, no width notes, said so in the block.
- **Exit** (`BriefingExitPicker`): first vacating exit ≤ 90° comfortably reachable at the touchdown
  speed (`RolloutExitGate.ComfortableExitLeadFeet`, the touchdown re-plan's rule); a high-speed
  exit ≤ 1,500 ft further wins over a normal one; nothing reachable → the last exit, flagged.
- **Stand** (`BriefingStandPicker`): the SayIntentions gate (name, alias, then the published
  nose-stop position within min(150 m, 2 × radius)) only when SI's flight matches THIS OFP on
  origin AND destination (`SayIntentionsArrivalGate.From`; SI closed → no file → inferred; another
  flight or a stale file from another city pair → ignored); otherwise category (freighter → cargo
  ramps, airliner → gates, ramps as fallback), wingspan fit (`ParkingSpot.FitsAircraft`; unknown
  size kept), the SimBrief airline's stands, then the stand nearest the group's centroid. Every
  fallback is a note in the block.
- **Hold-shorts**: `RouteRunwayCrossings.InsertRunwayHoldShorts` with a null aircraft; the
  departure runway's own entry is always added as the "before entering" hold; a hold naming the
  runway just landed on (either end) is discarded.
- **Taxiway widths are advisory only**: navdata `taxi_path.width` is the scenery default for
  ~78 % of rows worldwide (30 m; every KMEM taxiway reads 30 m) so it never changes a route; a
  taxiway below the code letter's Annex 14 minimum is named in a "Taxiway width note".
- Limits: the block adds ~1–2 KB (exits capped at 12); Claude's `max_tokens` is 16000 and Gemini
  now reports `finishReason == MAX_TOKENS` with the same "may be incomplete" note; the word target
  is 600–900; the planner is bounded at 20 s (`TaxiBriefingPlanner.DefaultBudget`).

Diagnostics: `debug.log`, category `taxi_briefing` (one summary line per leg, warnings on failure).
```

- [ ] **Step 2: `docs/taxi-guidance.md` — append under "## Integration Points"**

```markdown
**Route briefing (Shift+E → Describe Route).** `Navigation/Briefing/TaxiBriefingPlanner` plans the
briefing's expected taxi-out/taxi-in legs on a graph built exactly as `LandingExitForm` builds it,
through the same pure code guidance uses (`TaxiRouter`, `RouteRunwayCrossings`, `GetLandingExits`,
`RunwayLineupTarget` — the lineup-entry maths shared with `TaxiAssistForm.PopulateDestinations`).
It never touches `TaxiGuidanceManager`. When the database has no taxiways for an airport it builds
a PLANNING-ONLY graph from the cached OpenStreetMap data (`OsmPlanningGraph`); that graph is a
local of the planner and must never be handed to guidance or a form — the "never steer on online
geometry" rule stands. See docs/gemini.md, "Taxi routes in the route briefing".
```

- [ ] **Step 3: `CLAUDE.md` — two bullets at the end of the "### Gemini AI" invariants group**

```markdown
- The route briefing's OpenStreetMap graph (`Navigation/Briefing/OsmPlanningGraph`) is PLANNING-ONLY: built inside `TaxiBriefingPlanner`, labelled "planning only — taxi guidance cannot use this", never stored on a field a form or `TaxiGuidanceManager` can reach. It exists only because a briefing does not steer; the augmentation rule "never steer on online geometry" is unchanged. → [gemini.md](docs/gemini.md)
- The briefing's taxi section always shows BOTH the scenery-computed route (names only from the block) and the AI's own real-world route (the owner's step-by-step template under "Real-world practice"), and never merges them — where they differ the AI says so and which is which. Aircraft classification for it is SimBrief-only (never the loaded aircraft or `WING SPAN`), and navdata taxiway widths are advisory notes, never a routing constraint (30 m scenery default on ~78 % of rows). → [gemini.md](docs/gemini.md)
```

- [ ] **Step 4: Changelog fragment — deferred**

The fragment is `changelog.d/<pr>-route-briefing-taxi-routes.feature.md` with this content, but `<pr>` is READ from `gh pr create`'s output and the owner has not authorised a push. Do NOT create the file with a guessed number. Record in the final report: "Open item: after the owner pushes and opens the PR, add the fragment with the real number." Prepared content:

```markdown
The EFB route briefing (Shift+E, Describe Route) now ends with a "Taxi out and taxi in" section. It tells you the taxiways to expect from where you are parked (or from a typical stand for your aircraft) to the departure runway with every hold-short point, and after landing whether to leave the runway left or right, which exit and how far down the runway, the next exit if you miss it, and the taxiways and runway crossings to your gate — your SayIntentions-assigned gate when SayIntentions is running for this flight, otherwise a sensible stand for the aircraft (a cargo stand for a freighter, a stand that fits an A380). Routes come from your own scenery's taxiway data, from OpenStreetMap for planning when the scenery has none, and the AI adds what it knows of real-world practice at each airport, saying where that differs.
```

- [ ] **Step 5: Final verification**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` and the full test suite. Expected: build succeeded, all tests pass. Then `git status` must be clean after the commit below, and `git log --oneline origin/main..HEAD` lists every task commit.

- [ ] **Step 6: Commit**

```bash
git add docs/gemini.md docs/taxi-guidance.md CLAUDE.md
git commit -m "docs: route briefing taxi routes — tiers, exit/stand rules, OSM planning-only invariant"
```

**In-sim test plan (for the PR description; the owner runs it):**
1. Parked at the origin, SayIntentions running with a gate assigned for this flight, SimBrief plan loaded: Describe Route → the section starts "from current position…", taxi-in names the SI gate, both routes use taxiways Taxi Assist also offers.
2. From the main menu (or airborne) with SayIntentions closed: both legs say "representative stand …", freighter plans (MD-11F/777F) land on a cargo stand, an A380 on a stand that fits.
3. An airport whose scenery has no taxiways in the database: the leg is labelled OpenStreetMap planning-only, or says why no ground data exists and the AI's route stands alone.
4. Taxi Assist and the Landing Exit dialog behave exactly as before (the runway list, the destination list, the default exit).
