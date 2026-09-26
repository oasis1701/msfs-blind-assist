# One Grounded Taxi Section Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring back the scenery/OpenStreetMap-computed taxi routes in the Describe Route briefing, but have the AI tell them as ONE short section in the real-world voice, with a preview phrase, at most one real-world note per leg, and every taxiway it names held to real names.

**Architecture:** Revert the strip commit (`06148c89`) to restore the route planner as it was at `dd1c72ae`, re-apply the two strip-era keepers (the owner's question wording, the compass-point runway test), add each airport's taxiway name list to every leg planned on a graph (model → planner → renderer), and rewrite the prompt's section 7. Everything else in the planner is untouched.

**Tech Stack:** .NET 10 / C# 13, xUnit (`tests/MSFSBlindAssist.Tests`), Windows Forms EFB.

**Spec:** `docs/superpowers/specs/2026-09-26-route-briefing-one-grounded-taxi-section-design.md`

## Global Constraints

- Branch: `claude/route-briefing-real-world-only` (owner: no new branch). Work in the worktree `D:\Claude\oasis1701\msfs-blind-assist\.claude\worktrees\route-briefing-grounded`. Never push (owner: local only).
- Build ONLY with `dotnet build MSFSBlindAssist.sln -c Debug` (never the bare `.csproj`).
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` (add `--filter "FullyQualifiedName~<Class>"` for one class).
- The owner's question, verbatim: `Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. Please include the expected taxiways, hold short points, and any specific restrictions.`
- The online taxiway-name wait stays exactly as it is (up to 8 s, `TaxiBriefingGraphSource.PrefetchWaitMs`) — owner's choice.
- Commit messages end with the committing agent's own `Co-Authored-By:` attribution line (each agent's own harness guidance governs; the controller's commits use `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
- Files keep their existing line endings (the repo stores LF; do not convert).

---

### Task 1: Undo the strip

**Files:** every file `06148c89` touched (the revert restores them): the planner, pickers, turns, OpenStreetMap graph, graph source, renderer, models, `ElectronicFlightBagForm.BuildTaxiRoutesBlockAsync`, `AircraftSizeClass`, `TaxiGraph` doc lines, `AugmentingAirportDataProvider.GetOnlineTaxiDataAsync`, their tests, `CLAUDE.md`, `docs/gemini.md`, `docs/sayintentions.md`, `docs/taxi-guidance.md`; and it deletes `TaxiPlanningBlock.cs`, `SayIntentionsArrivalGate.cs` (back inside `BriefingStandPicker.cs`) and their tests.

**Interfaces:**
- Produces: the `dd1c72ae` API — `TaxiBriefingPlanner.PlanTaxiOut/PlanTaxiIn(TaxiBriefingRequest, GraphBundle)`, `TaxiBriefingPlanner.PlanAsync(...)`, `TaxiLegBriefing`, `TaxiBriefingRenderer.Render(TaxiBriefing, DistanceUnit)`, `GeminiService.RealWorldTaxiQuestion` (old wording).

- [ ] **Step 1: Revert without committing**

```bash
git revert --no-commit 06148c89
git status --short | head -50
```
Expected: no conflicts; the deleted files come back (`A`/`M`), `TaxiPlanningBlock.cs`, `SayIntentionsArrivalGate.cs`, `TaxiPlanningBlockTests.cs`, `SayIntentionsArrivalGateTests.cs` are deleted (`D`).

- [ ] **Step 2: Build and run the whole suite**

```bash
dotnet build MSFSBlindAssist.sln -c Debug 2>&1 | grep -E " error |Build succeeded" | sort -u
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --no-build 2>&1 | grep -E "Passed!|Failed!|\[FAIL\]"
```
Expected: `Build succeeded.` and `Passed!  - Failed: 0, Passed: 8224` (the `dd1c72ae` count).

- [ ] **Step 3: Commit**

```bash
git commit -q -F - <<'EOF'
revert(briefing): bring back the scenery-computed taxi routes

Reverts 06148c89 ("brief the typical real-world taxi flow only"). The
real-world-only briefing invented taxiway names (KMEM V1, KATL V3/V4);
the owner wants the scenery/OpenStreetMap route back as the briefing's
ground truth, told as one short section (next commits).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Carry over the strip-era keepers

**Files:**
- Modify: `MSFSBlindAssist/Services/GeminiService.cs` (`RealWorldTaxiQuestion`, ~line 907)
- Test: `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs`, `tests/MSFSBlindAssist.Tests/RouteBriefingTextTests.cs`, `tests/MSFSBlindAssist.Tests/BriefingRunwayChoiceTests.cs`

**Interfaces:**
- Produces: `GeminiService.RealWorldTaxiQuestion` in the owner's wording (Global Constraints).

- [ ] **Step 1: Write the failing test** — in `RouteDescriptionPromptTests.Prompt_has_the_taxi_section_with_the_owners_template_and_the_wider_word_target`, replace the template assertion with:

```csharp
        Assert.Contains("Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. " +
                        "Please include the expected taxiways, hold short points, and any specific restrictions.", prompt);
```
and in `RouteBriefingTextTests.The_literal_template_is_removed`, change the echoed template line to:

```csharp
                "Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. " +
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ... --filter "FullyQualifiedName~RouteDescriptionPromptTests|FullyQualifiedName~RouteBriefingTextTests"`
Expected: FAIL — `Prompt_has_the_taxi_section_with_the_owners_template_and_the_wider_word_target` (sub-string not found). `The_literal_template_is_removed` still passes (the remover keys on the question's fixed words).

- [ ] **Step 3: Implement** — in `GeminiService.cs`:

```csharp
    internal const string RealWorldTaxiQuestion =
        "Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. " +
        "Please include the expected taxiways, hold short points, and any specific restrictions.";
```

- [ ] **Step 4: Add the compass-point characterization test** — append to `BriefingRunwayChoiceTests` (it pins behaviour the planner's `RunwayIdsMatch` already has; it passes at once):

```csharp
    [Fact]
    public void A_compass_point_runway_is_matched_as_a_runway()
    {
        // 204 fs2024 runway ends are compass points (N/S/E/W/NE/…); the match must read them, not only numbers.
        Assert.Equal(BriefingRunwayChoice.AgreesNote, BriefingRunwayChoice.Choose("N", "N", siIsThisFlight: true).Note);
        var r = BriefingRunwayChoice.Choose("N", "S", siIsThisFlight: true);
        Assert.Equal("S", r.Runway);
        Assert.Equal("runway S is the runway SayIntentions assigned; the flight plan names N", r.Note);
    }
```

- [ ] **Step 5: Run to verify all pass**

Run: `dotnet test ... --filter "FullyQualifiedName~RouteDescriptionPromptTests|FullyQualifiedName~RouteBriefingTextTests|FullyQualifiedName~BriefingRunwayChoiceTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -q -m "feat(briefing): the owner's taxi question in the owner's own words

Also pins compass-point runway ends in BriefingRunwayChoice.

<your own Co-Authored-By attribution line>"
```

---

### Task 3: Every leg carries its airport's taxiway names

**Files:**
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs` (`TaxiLegBriefing`)
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs` (`PlanTaxiOut`, `PlanTaxiIn`)
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs` (`RenderTaxiOut`, `RenderTaxiIn`)
- Test: `tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs`, `tests/MSFSBlindAssist.Tests/TaxiBriefingRendererTests.cs`

**Interfaces:**
- Consumes: `TaxiGraph.Adjacency` (edges with `TaxiwayName`), the `TaxiBriefingFixture` TEST airport (named taxiways A, B, C, D, E1; stand lead-ins unnamed).
- Produces: `TaxiLegBriefing.AirportTaxiways : IReadOnlyList<string>` (`internal set`), `TaxiBriefingPlanner.AirportTaxiwayNames(TaxiGraph) : IReadOnlyList<string>`, and the block line `  Taxiway names at {ICAO}: {names joined by ", "}` — Task 4's prompt refers to it as the "Taxiway names at" list.

- [ ] **Step 1: Write the failing planner tests** — append to `TaxiBriefingPlannerTests`:

```csharp
    // ── the airport's taxiway names (owner, 2026-09-26) ──────────────────────────────────────

    [Fact]
    public void A_planned_leg_carries_every_taxiway_name_of_its_airport_sorted()
    {
        var bundle = Airport();
        Assert.Equal(new[] { "A", "B", "C", "D", "E1" }, TaxiBriefingPlanner.PlanTaxiOut(Request(B738), bundle).AirportTaxiways);
        Assert.Equal(new[] { "A", "B", "C", "D", "E1" }, TaxiBriefingPlanner.PlanTaxiIn(Request(B738), bundle).AirportTaxiways);
    }

    [Fact]
    public void An_unavailable_leg_planned_on_a_graph_still_carries_the_names()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "04"), Airport());
        Assert.NotNull(leg.Unavailable);
        Assert.Equal(new[] { "A", "B", "C", "D", "E1" }, leg.AirportTaxiways);
    }

    [Fact]
    public async Task A_leg_never_planned_on_a_graph_has_no_names()
    {
        var b = await TaxiBriefingPlanner.PlanAsync(Request(B738), provider: null, gateSource: null, TimeSpan.FromSeconds(5));
        Assert.Empty(b.TaxiOut.AirportTaxiways);
        Assert.Empty(b.TaxiIn.AirportTaxiways);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingPlannerTests"`
Expected: build error `'TaxiLegBriefing' does not contain a definition for 'AirportTaxiways'`.

- [ ] **Step 3: Implement the model and planner**

In `TaxiLegBriefing` (after `Notes`):

```csharp
    /// <summary>Every taxiway name on the graph the leg was planned on (<see cref="TaxiBriefingPlanner.AirportTaxiwayNames"/>),
    /// sorted; empty for a leg never planned on a graph (no database, no ground data, a timeout). The prompt holds any
    /// taxiway the AI names to this list and the route, so its own additions use real names (owner, 2026-09-26).</summary>
    public IReadOnlyList<string> AirportTaxiways { get; internal set; } = Array.Empty<string>();
```

In `TaxiBriefingPlanner.cs`, rename the existing `public static TaxiLegBriefing PlanTaxiOut(TaxiBriefingRequest r, GraphBundle g)` to `private static TaxiLegBriefing PlanTaxiOutLeg(TaxiBriefingRequest r, GraphBundle g)` and `PlanTaxiIn` to `private static TaxiLegBriefing PlanTaxiInLeg(...)` (bodies unchanged), and add above them:

```csharp
    public static TaxiLegBriefing PlanTaxiOut(TaxiBriefingRequest r, GraphBundle g) =>
        WithAirportTaxiways(PlanTaxiOutLeg(r, g), g);

    public static TaxiLegBriefing PlanTaxiIn(TaxiBriefingRequest r, GraphBundle g) =>
        WithAirportTaxiways(PlanTaxiInLeg(r, g), g);

    /// <summary>Every taxiway name on the graph a leg is planned on — exactly the names its route could use: the
    /// scenery's, with OpenStreetMap-filled names, or the OpenStreetMap graph's on that tier. Edge names only, never
    /// the graph's online alias labels. Sorted ignoring case.</summary>
    internal static IReadOnlyList<string> AirportTaxiwayNames(TaxiGraph graph) =>
        graph.Adjacency.Values.SelectMany(edges => edges)
            .Select(e => e.TaxiwayName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TaxiLegBriefing WithAirportTaxiways(TaxiLegBriefing leg, GraphBundle g)
    {
        leg.AirportTaxiways = AirportTaxiwayNames(g.Graph);
        return leg;
    }
```

- [ ] **Step 4: Run to verify the planner tests pass**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingPlannerTests"`
Expected: PASS. (If the TEST graph carries a name beyond A, B, C, D, E1, check where it comes from before changing the expectation — the fixture has only those five named paths.)

- [ ] **Step 5: Write the failing renderer tests** — append to `TaxiBriefingRendererTests`:

```csharp
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
    public void An_unavailable_leg_lists_its_names_too_and_a_leg_without_any_lists_none()
    {
        var withNames = TaxiLegBriefing.UnavailableLeg("KMEM", "36L", BriefingTier.Navdata, "no stand at KMEM connects to the taxiway network");
        withNames.AirportTaxiways = new[] { "J", "M2" };
        var without = TaxiLegBriefing.UnavailableLeg("KATL", "08L", BriefingTier.None, "no navigation database loaded");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, withNames, without), DistanceUnit.Feet);

        Assert.Contains("TAXI OUT at KMEM: taxi route unavailable — no stand at KMEM connects to the taxiway network\n" +
                        "  Taxiway names at KMEM: J, M2\n" +
                        "TAXI IN at KATL", text);
        Assert.DoesNotContain("Taxiway names at KATL", text);
    }
```

- [ ] **Step 6: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~TaxiBriefingRendererTests"`
Expected: FAIL — sub-string not found (no names line yet). The existing full-block tests still pass (their legs carry no names).

- [ ] **Step 7: Implement the renderer** — in `TaxiBriefingRenderer`, add:

```csharp
    /// <summary>Every taxiway name at the leg's airport: the prompt holds any taxiway the AI names to this line and the
    /// route (owner, 2026-09-26). Absent for a leg never planned on a graph.</summary>
    private static void AddAirportTaxiways(TaxiLegBriefing leg, List<string> lines)
    {
        if (leg.AirportTaxiways.Count > 0)
            lines.Add($"  Taxiway names at {leg.Icao}: {string.Join(", ", leg.AirportTaxiways)}");
    }
```
and call `AddAirportTaxiways(leg, lines);` in BOTH `RenderTaxiOut` and `RenderTaxiIn`, on the line immediately before `foreach (var note in leg.Notes) lines.Add($"  Note: {note}");`.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: PASS, 0 failed.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -q -m "feat(briefing): each taxi leg lists its airport's taxiway names

So the AI's own additions can be held to real names.

<your own Co-Authored-By attribution line>"
```

---

### Task 4: One short taxi section in the prompt

**Files:**
- Modify: `MSFSBlindAssist/Services/GeminiService.cs` (`GetRouteDescriptionPrompt`, section 7)
- Test: `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs`

**Interfaces:**
- Consumes: the block's `Taxiway names at` line (Task 3), `RealWorldTaxiQuestion` (Task 2).

- [ ] **Step 1: Write the failing tests** — in `RouteDescriptionPromptTests`:

In `Prompt_has_the_taxi_section_with_the_owners_template_and_the_wider_word_target` replace `Assert.Contains("Real-world practice", prompt);` with `Assert.DoesNotContain("Real-world practice", prompt);`. Replace the body of `Prompt_asks_for_every_taxi_distance_in_the_block_s_unit` with:

```csharp
        // Live KMEM→KATL: "2.2 kilometers" and "6,025 feet" in one taxi section. The block now states one unit.
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("Give every distance in this section in the unit the block's \"Distance unit\" line names", prompt);
```
and append:

```csharp
    [Fact]
    public void The_taxi_section_is_one_short_part_built_on_the_block()
    {
        // Owner, 2026-09-26: two sections were too long; the real-world-only form invented KMEM "V1" and KATL "V3"/"V4".
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("ONE short paragraph for the taxi out at the departure airport and ONE short paragraph for the " +
                        "taxi in at the arrival airport, in the voice of real-world operations", prompt);
        Assert.Contains("The route comes from the block only", prompt);
        Assert.Contains("Give the total taxi distance for each leg.", prompt);
        Assert.DoesNotContain("do two things", prompt);
    }

    [Fact]
    public void Each_leg_says_it_is_a_preview_of_the_clearance()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("say in a short phrase that this is the expected route on the pilot's scenery and that " +
                        "SayIntentions or ATC will give the actual taxi clearance", prompt);
    }

    [Fact]
    public void Own_knowledge_is_held_to_real_names_and_one_real_world_note()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("but only where it concerns a taxiway, runway or stand the block names", prompt);
        Assert.Contains("at most one sentence per leg saying that controllers usually route differently there", prompt);
        Assert.Contains("Any taxiway you name must appear in that leg's lines, including its \"Taxiway names at\" list", prompt);
        Assert.Contains("never give a full alternative route", prompt);
    }

    [Fact]
    public void The_taxi_section_stays_short()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("do not list every exit", prompt);
        Assert.Contains("do not describe where the data came from", prompt);
        Assert.Contains("a representative stand (say it is typical, not assigned)", prompt);
        Assert.Contains("When a leg's route comes from OpenStreetMap, say so in a few words", prompt);
    }

    [Fact]
    public void An_unavailable_leg_is_answered_from_general_knowledge_and_says_so()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("saying it is general knowledge and not checked against the scenery", prompt);
        Assert.Contains("where the leg has a \"Taxiway names at\" list, name only taxiways from it", prompt);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~RouteDescriptionPromptTests"`
Expected: FAIL — the new tests and the two edited ones (sub-strings not found / "Real-world practice" still present).

- [ ] **Step 3: Implement** — in `GetRouteDescriptionPrompt`, replace the whole block from `7. TAXI OUT AND TAXI IN` up to (not including) the blank line before `IMPORTANT GUIDELINES:` with:

```text
7. TAXI OUT AND TAXI IN
   The flight plan data ends with a TAXI ROUTES block worked out from the pilot's own simulator scenery.
   Write this section as ONE short paragraph for the taxi out at the departure airport and ONE short paragraph for the taxi in at the arrival airport, in the voice of real-world operations.
   For each leg, answer the question below from that leg's lines of the TAXI ROUTES block, taking the bracketed items (the airport, the runway, the stand or terminal, and the aircraft type) from them (the runway there may be the one SayIntentions assigned rather than the flight plan's):
      ""{RealWorldTaxiQuestion}""
   That question is an instruction to you, not text for the pilot: write only your answer, and never write the question itself into the briefing, as shown here or with the items filled in.
   The route comes from the block only: the stand, the taxiways in order with the turn at each change of taxiway and into the stand wherever the block gives one, every hold-short point and the runway it protects, and for the arrival which side to leave the runway (left or right), the exit taxiway and its distance from the threshold, the next exit if that one is missed, every runway crossed, and the gate.
   Use ONLY the taxiway, exit and stand names given in the block, and repeat distances, sides and turn directions exactly as given; where the block gives no turn for a taxiway, give none.
   Give the total taxi distance for each leg.
   For each leg, say in a short phrase that this is the expected route on the pilot's scenery and that SayIntentions or ATC will give the actual taxi clearance.
   From your own knowledge you may add wingspan or aircraft-type restrictions, current operational information such as a NOTAM closing a taxiway, and usual practice, but only where it concerns a taxiway, runway or stand the block names; and at most one sentence per leg saying that controllers usually route differently there.
   Any taxiway you name must appear in that leg's lines, including its ""Taxiway names at"" list; never give a full alternative route.
   Keep it short: do not list every exit, do not describe where the data came from, and mention a note from the block only when it changes what the pilot does or hears: a runway SayIntentions assigned that differs from the flight plan, a representative stand (say it is typical, not assigned), a SayIntentions gate the scenery lists under another name or at a different position, a taxiway width or stand size note.
   When a leg's route comes from OpenStreetMap, say so in a few words; taxi guidance cannot use it.
   If the block says a leg is unavailable, say so in a few words and give that leg's usual route from your own knowledge, saying it is general knowledge and not checked against the scenery; where the leg has a ""Taxiway names at"" list, name only taxiways from it.
   Give every distance in this section in the unit the block's ""Distance unit"" line names, and never mix units.
   When a leg's note says SayIntentions assigned a different runway from the flight plan, say so here, and also in the DEPARTURE AND SID or ARRIVAL AND STAR section, naming both runways.
```
(The prompt is a C# verbatim interpolated string: keep `""` for each literal quote and `{RealWorldTaxiQuestion}` as is. Keep the file's existing line endings. Each sentence stays on ONE line: the tests assert whole phrases, and a line break inside one - as in "with the turn at each" / "change of taxiway" - fails them.)

- [ ] **Step 4: Run to verify all prompt tests pass**

Run: `dotnet test ... --filter "FullyQualifiedName~RouteDescriptionPromptTests|FullyQualifiedName~RouteBriefingTextTests"`
Expected: PASS (the kept tests — question from the constant, never written out, names only from the block, turn directions, SayIntentions runway, "from that leg's lines of the TAXI ROUTES block" — pass on the new text too).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -q -m "feat(briefing): one short taxi section built on the scenery route

One paragraph per leg in the real-world voice, a preview phrase, at most
one real-world note, and every taxiway held to the leg's names.

<your own Co-Authored-By attribution line>"
```

---

### Task 5: Docs and guardrails

**Files:**
- Modify: `CLAUDE.md` (the Gemini-section bullet beginning `- The briefing's taxi section always shows BOTH`)
- Modify: `docs/gemini.md` (section "Taxi routes in the route briefing (2026-09-25)": its opening paragraph and the `Limits` bullet)
- Modify: `docs/superpowers/specs/2026-09-26-route-briefing-one-grounded-taxi-section-design.md` (the name-line example)

- [ ] **Step 1: CLAUDE.md** — replace the whole bullet that begins `- The briefing's taxi section always shows BOTH` with:

```markdown
- The briefing's taxi section is ONE short part — a paragraph per leg in the voice of real-world operations — built on the scenery-computed route (names, order, turns, sides and distances only from the block), never two versions (owner, 2026-09-26: the two-section form was too long, and the real-world-only form invented taxiway names — KMEM "V1", KATL "V3"/"V4"). Each leg says it is the expected route on the pilot's scenery and that SayIntentions or ATC gives the actual clearance: the planner's route is the SHORTEST one, and controllers route by flows, one-way taxiways and end-around taxiways. The AI's own additions — restrictions, current operational information, usual practice, and at most one sentence per leg that controllers usually route differently — may name only taxiways in that leg's lines, including its "Taxiway names at" list (`TaxiLegBriefing.AirportTaxiways`, every edge name on the graph the leg was planned on); never a full alternative route. Aircraft classification for it is SimBrief-only (never the loaded aircraft or `WING SPAN`), and navdata taxiway widths are advisory notes, never a routing constraint (the commonest navdata width, 82 ft = 24.99 m on 62 % of fs2024 taxiway rows and 65 % of fs2020's, is judged at the block's 0.1 m precision, so it is never "below" code F's 25 m). → [gemini.md](docs/gemini.md)
```

- [ ] **Step 2: docs/gemini.md** — in the section's opening paragraph, replace the sentence that begins `The prompt's section 7 makes the AI narrate the computed route` (through `which is which.`) with:

```markdown
The prompt's section 7 makes the AI tell that route as ONE short paragraph per leg, in the voice of real-world operations, answering the owner's question ("Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. Please include the expected taxiways, hold short points, and any specific restrictions.") from the block: the route's names, order, turns, sides and distances come from the block only; each leg says it is the expected route on the pilot's scenery and that SayIntentions or ATC gives the actual clearance (the route is the shortest one, and controllers do not always route that way — KATL's V goes around the west end of 08R/26L); and the AI's own additions (restrictions such as an A380's wingspan limits, current operational information such as a NOTAM closure, usual practice, and at most one sentence per leg that controllers usually route differently) may name only taxiways in that leg's lines, including its "Taxiway names at" list — every edge name on the graph the leg was planned on (`TaxiLegBriefing.AirportTaxiways`). An unavailable leg is answered from general knowledge and says so. History: until 2026-09-26 the section had two parts (the computed route, then a "Real-world practice" answer), which the owner found too long; a real-world-only version the same day invented taxiway names (KMEM "V1", KATL "V3"/"V4") and was reverted.
```
and in the `- Limits:` bullet, replace `the block adds ~1–2 KB (exits capped at 12)` with `the block adds ~1–2 KB (exits capped at 12), plus each airport's taxiway name list (about 0.5 KB for KATL's 95 names)`.

- [ ] **Step 3: Spec** — replace `("Taxiways at KATL (scenery navdata): A, A11, …")` with `("Taxiway names at KATL: A, A11, …")`.

- [ ] **Step 4: Verify no stale wording is left**

```bash
grep -n "always shows BOTH\|under a \"Real-world practice\" heading\|\[ICAO\] from \[runway\]" CLAUDE.md docs/gemini.md
```
Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs/gemini.md && git add -f docs/superpowers/specs/2026-09-26-route-briefing-one-grounded-taxi-section-design.md && git commit -q -m "docs(briefing): one grounded taxi section

<your own Co-Authored-By attribution line>"
```

---

### Task 6: Verify, refresh the owner's test build, tidy up

**Files:** none in the repo.

- [ ] **Step 1: Full build and suite on the branch**

```bash
dotnet build MSFSBlindAssist.sln -c Debug 2>&1 | grep -E " error |Build succeeded" | sort -u
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --no-build 2>&1 | grep -E "Passed!|Failed!|\[FAIL\]"
```
Expected: `Build succeeded.`; `Failed: 0`.

- [ ] **Step 2: Merge PR #160 into a throwaway copy and build it** (PR #160's head lives on the fork — fetch it first):

```bash
cd /d/Claude/oasis1701/msfs-blind-assist
gh pr view 160 --repo oasis1701/msfs-blind-assist --json headRefOid
git fetch -q fork feature/first-officer
git worktree add -q --detach .claude/worktrees/release-briefing-pr160 claude/route-briefing-real-world-only
cd .claude/worktrees/release-briefing-pr160 && git merge --no-edit -q fork/feature/first-officer
dotnet build MSFSBlindAssist.sln -c Debug 2>&1 | grep -E " error |Build succeeded" | sort -u
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --no-build 2>&1 | grep -E "Passed!|Failed!"
```
Expected: the fetched tip equals `headRefOid`; clean merge; `Build succeeded.`; `Failed: 0`. If the merge conflicts, stop and report.

- [ ] **Step 3: Replace the build in `D:\Claude\oasis1701\builds\MSFSBA-route-briefing-pr160-debug`** (PowerShell). `navdatareader` (the owner's database-build logs are in it) and `SimConnect.dll` (rewritten by the app at startup) are not produced by a worktree build and are kept:

```powershell
if (Get-Process MSFSBlindAssist -ErrorAction SilentlyContinue) { throw 'MSFS Blind Assist is running - ask the owner to close it' }
$bin  = 'D:\Claude\oasis1701\msfs-blind-assist\.claude\worktrees\release-briefing-pr160\MSFSBlindAssist\bin\x64\Debug\net10.0-windows'
$dest = 'D:\Claude\oasis1701\builds\MSFSBA-route-briefing-pr160-debug'
$keep = @('navdatareader','SimConnect.dll')
foreach ($i in (Get-ChildItem -LiteralPath $dest | Where-Object { $keep -notcontains $_.Name })) { Remove-Item -LiteralPath $i.FullName -Recurse -Force -Confirm:$false }
foreach ($i in Get-ChildItem -LiteralPath $bin) { Copy-Item -LiteralPath $i.FullName -Destination $dest -Recurse -Force }
$diffs = 0
$files = Get-ChildItem -LiteralPath $bin -Recurse -File
foreach ($f in $files) {
  $rel = $f.FullName.Substring($bin.Length).TrimStart('\')
  $t = Join-Path $dest $rel
  if (-not (Test-Path -LiteralPath $t) -or (Get-FileHash -LiteralPath $f.FullName).Hash -ne (Get-FileHash -LiteralPath $t).Hash) { $diffs++; Write-Output "MISMATCH $rel" }
}
$u16 = [System.Text.Encoding]::Unicode.GetString([System.IO.File]::ReadAllBytes("$dest\MSFSBlindAssist.dll"))
Write-Output ("files " + $files.Count + ", mismatches " + $diffs + ", names line " + $u16.Contains('Taxiway names at ') + ", owner wording " + $u16.Contains('[ICAO Code] from [Runway] to [Terminal/Gate]'))
```
Expected: `mismatches 0, names line True, owner wording True`.

- [ ] **Step 4: Remove the temporary worktrees** (the branch `claude/route-briefing-real-world-only` stays; the PR #160 merge commit is not kept):

```bash
cd /d/Claude/oasis1701/msfs-blind-assist
dotnet build-server shutdown >/dev/null 2>&1
git worktree remove .claude/worktrees/release-briefing-pr160
git worktree remove .claude/worktrees/route-briefing-grounded
git worktree prune && git worktree list && ls .claude/worktrees/
```
Expected: only the main checkout is listed and `.claude/worktrees/` is empty (if a folder is left behind empty, `rmdir` it).
