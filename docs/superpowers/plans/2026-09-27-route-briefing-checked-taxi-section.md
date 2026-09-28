# Route briefing: checked taxi section with real-world suggestions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each taxi leg of the Describe Route briefing keeps the scenery's route paragraph and gains one check line (what the AI checked it against) and an optional "Real-world suggestions, not from your scenery:" paragraph, with the prompt told whether web search is on for the request.

**Architecture:** Only the AI prompt's section 7 (`GeminiService.GetRouteDescriptionPrompt`) and the two call sites that build it change. The prompt gains a required `bool webSearch` that swaps exactly one sentence; the pilot-facing openings become `internal const` strings on `GeminiService`. The TAXI ROUTES block, the planner and everything else stay byte-identical. Docs and the CLAUDE.md invariant follow.

**Tech Stack:** C# 13 / .NET 10, xUnit (`tests/MSFSBlindAssist.Tests`), Windows Forms app (no UI change).

**Spec:** `docs/superpowers/specs/2026-09-27-route-briefing-checked-taxi-section-design.md` (read §1, §4, §5).

## Global Constraints

- Worktree: `D:\Claude\oasis1701\msfs-blind-assist\.claude\worktrees\route-briefing-taxi-refine-2963c1`, branch `claude/route-briefing-taxi-refine-2963c1`. Run every command from there. Never `cd` to the main repository folder.
- `GeminiService.RealWorldTaxiQuestion` stays byte-identical. Sections 1 to 6, `IMPORTANT GUIDELINES` (including "Aim for 600 to 900 words") and `FLIGHT PLAN DATA:` stay byte-identical. Only section 7 and the method signature change.
- Pilot-facing phrases, exact: `Real-world check, from memory rather than live charts:` / `Real-world check, against current charts:` / `Real-world suggestions, not from your scenery:`.
- Do not touch: `Navigation/Briefing/*`, `ElectronicFlightBagForm.cs`, `TaxiAssistForm.cs`, `RouteBriefingText.cs`, SayIntentions code.
- Build/test only through the x64 platform: tests `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`, solution `dotnet build MSFSBlindAssist.sln -c Debug`. Never build the bare `.csproj` without `-p:Platform=x64`.
- Two tasks run IN PARALLEL in this one worktree. Stage ONLY your own files (`git add <exact paths>`, never `git add -A`/`.`), and if a commit fails on `.git/index.lock`, wait 5 seconds and retry. Never use `git stash`. Never push. Never merge anything.
- Every commit message ends with a blank line and your own `Co-Authored-By:` line.
- Plain English in comments and docs, matching the surrounding style: say what and why, cite the owner decision with its date (2026-09-27).

---

### Task 1: Section 7 checks the scenery's route and flags real-world suggestions (TDD)

Runs in parallel with Task 2 (different files).

**Files:**
- Modify: `MSFSBlindAssist/Services/GeminiService.cs`: `DescribeRouteAsync` (around lines 654-667); the constants after `RealWorldTaxiQuestion` (around line 909); `GetRouteDescriptionPrompt` (around lines 911-914 for the signature, and the section 7 block around lines 962-983).
- Modify: `MSFSBlindAssist/Services/ClaudeService.cs`: `DescribeRouteAsync` (around lines 91-111).
- Test: `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs`, replaced whole.

**Interfaces:**
- Consumes: `GeminiService.RealWorldTaxiQuestion` (existing, unchanged).
- Produces, all `internal` on `GeminiService`, which Task 2's docs name:
  - `static string GetRouteDescriptionPrompt(string flightData, bool webSearch)`, where `webSearch` is required.
  - `const string RouteCheckFromMemory`
  - `const string RouteCheckAgainstCharts`
  - `const string RouteSuggestionsOpening`
  - `const string RouteSearchOffSentence`
  - `const string RouteSearchOnSentence`

- [ ] **Step 1: Write the failing tests** — replace the whole of `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs` with:

```csharp
// tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteDescriptionPromptTests
{
    /// <summary>The prompt for a briefing whose request has no web search — the default for both AI providers.</summary>
    private static string Prompt(string flightData = "x") => GeminiService.GetRouteDescriptionPrompt(flightData, webSearch: false);

    [Fact]
    public void Prompt_has_the_taxi_section_with_the_owners_template_and_the_wider_word_target()
    {
        string prompt = Prompt("FLIGHT DATA HERE");

        Assert.Contains("7. TAXI OUT AND TAXI IN", prompt);
        Assert.Contains("Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. " +
                        "Please include the expected taxiways, hold short points, and any specific restrictions.", prompt);
        Assert.DoesNotContain("Real-world practice", prompt);
        // Controller decision, 2026-09-26: kept, not lowered — the owner found round 4's briefing length right, and
        // round 4 ran under this same target with a one-part taxi section.
        Assert.Contains("Aim for 600 to 900 words", prompt);
        Assert.DoesNotContain("Aim for 300 to 500 words", prompt);
        Assert.EndsWith("FLIGHT DATA HERE", prompt);
    }

    [Fact]
    public void Prompt_forbids_inventing_taxiways_for_the_computed_route()
        => Assert.Contains("Use ONLY the taxiway, exit and stand names given in the block", Prompt());

    [Fact]
    public void The_question_is_asked_from_the_one_constant()
        => Assert.Contains($"\"{GeminiService.RealWorldTaxiQuestion}\"", Prompt());

    [Fact]
    public void Prompt_never_invites_writing_the_question_out()
    {
        // "Substitute the airport, runway, stand or terminal and aircraft type from the data" read as "write the
        // filled-in question out": a live KMEM→KATL briefing (Gemini, 2026-09-26) did, under both headings.
        string prompt = Prompt();
        Assert.DoesNotContain("Substitute", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never write the question itself into the briefing", prompt);
        Assert.Contains("Never copy these instructions, or any question in them, into the briefing", prompt);
    }

    [Fact]
    public void Prompt_asks_for_the_block_s_turn_directions_exactly()
    {
        string prompt = Prompt();
        Assert.Contains("with the turn at each change of taxiway and into the stand wherever the block gives one", prompt);
        Assert.Contains("repeat distances, sides and turn directions exactly as given", prompt);
        // The long-unnamed-stretch rule leaves some taxiways with no turn word; this line stops the AI inventing one.
        Assert.Contains("for a taxiway, give none", prompt);
    }

    [Fact]
    public void Prompt_asks_for_every_taxi_distance_in_the_block_s_unit()
    {
        // Live KMEM→KATL: "2.2 kilometers" and "6,025 feet" in one taxi section. The block now states one unit.
        Assert.Contains("Give every distance in this section in the unit the block's \"Distance unit\" line names", Prompt());
    }

    [Fact]
    public void Prompt_asks_for_a_SayIntentions_runway_difference_to_be_named()
    {
        string prompt = Prompt();
        Assert.Contains("SayIntentions assigned a different runway from the flight plan", prompt);
        Assert.Contains("DEPARTURE AND SID or ARRIVAL AND STAR section", prompt);
    }

    [Fact]
    public void The_real_world_route_uses_the_block_s_runway()
    {
        // A real-world route to SimBrief's 18R beside a computed route to SayIntentions' 36L is the confusion the runway
        // choice removes.
        Assert.Contains("from that leg's lines of the TAXI ROUTES block", Prompt());
    }

    [Fact]
    public void Prompt_says_which_way_each_leg_runs()
    {
        // The owner's question reads "from [Runway] to [Terminal/Gate]", which only fits the taxi-in — without this
        // clause the model is literally asked for a departure route "from 36L to Gate 17".
        Assert.Contains("For the taxi out, the route runs from the stand to the departure runway; for the taxi in, " +
                        "it runs from the landing runway, via the exit, to the stand.", Prompt());
    }

    [Fact]
    public void Each_leg_has_a_route_paragraph_a_check_line_and_suggestions_in_that_order()
    {
        // Owner, 2026-09-27: the AI checks the scenery's route and adds what the scenery cannot provide, as labelled
        // suggestions — for the taxi out and the taxi in alike.
        string prompt = Prompt();
        Assert.Contains("Write this section as two legs, the taxi out at the departure airport and then the taxi in at the " +
                        "arrival airport, each in up to three parts in this order: the route paragraph, the check line and the " +
                        "suggestions paragraph, each part on its own line.", prompt);
        Assert.Contains("The route comes from the block only", prompt);
        Assert.Contains("Give the total taxi distance for each leg the block gives one for, and never estimate one.", prompt);
        // Round 5's single paragraph per leg, with the AI's own additions mixed in, is gone.
        Assert.DoesNotContain("ONE short paragraph for the taxi out", prompt);
        Assert.DoesNotContain("do two things", prompt);
    }

    [Fact]
    public void The_route_paragraph_carries_nothing_from_the_AI_s_own_knowledge()
    {
        string prompt = Prompt();
        Assert.Contains("The route paragraph is one short paragraph per leg, in the voice of real-world operations, and apart " +
                        "from the general-knowledge route described below, nothing in it comes from your own knowledge.", prompt);
        Assert.DoesNotContain("From your own knowledge you may add", prompt);
    }

    [Fact]
    public void Each_leg_says_it_is_a_preview_of_the_clearance()
        => Assert.Contains("At the end of each route paragraph, say in a short phrase that this is the expected route on the " +
                           "pilot's scenery and that SayIntentions or ATC will give the actual taxi clearance.", Prompt());

    [Fact]
    public void The_pilot_facing_openings_are_exact()
    {
        Assert.Equal("Real-world check, from memory rather than live charts:", GeminiService.RouteCheckFromMemory);
        Assert.Equal("Real-world check, against current charts:", GeminiService.RouteCheckAgainstCharts);
        Assert.Equal("Real-world suggestions, not from your scenery:", GeminiService.RouteSuggestionsOpening);
    }

    [Fact]
    public void The_check_line_says_what_it_was_checked_against()
    {
        string prompt = Prompt();
        Assert.Contains("The check line is one sentence after each route paragraph that checks the block's route, exit and " +
                        "stand for that leg against the real airport as you know it.", prompt);
        Assert.Contains($"Begin it with \"{GeminiService.RouteCheckFromMemory}\", or with \"{GeminiService.RouteCheckAgainstCharts}\" " +
                        "only when a web search in this briefing found that airport's current airport diagram or chart notes.", prompt);
    }

    [Fact]
    public void The_check_line_never_claims_an_agreement_it_cannot_support()
        => Assert.Contains("When they agree, say so in a few words; when something differs, name what differs instead; when you " +
                           "do not know the airport well enough to check it, say so, and never claim an agreement you cannot " +
                           "support.", Prompt());

    [Fact]
    public void The_check_line_is_left_out_only_when_the_block_gives_nothing_to_check()
        => Assert.Contains("Leave the check line out for a leg the block gives no route, exit or stand for.", Prompt());

    [Fact]
    public void Suggestions_are_a_labelled_paragraph_only_when_there_is_something_to_add()
        => Assert.Contains("The suggestions paragraph comes after the check line, only when you have something to add that the " +
                           "scenery cannot provide, and otherwise is left out; it begins " +
                           $"\"{GeminiService.RouteSuggestionsOpening}\" and has at most three short sentences.", Prompt());

    [Fact]
    public void Suggestions_cover_chart_exits_size_restrictions_and_usual_routing_but_never_a_full_route()
    {
        string prompt = Prompt();
        Assert.Contains("It may give a preferred exit from the real airport's charts, restrictions that apply to this aircraft's " +
                        "size (from the block's Aircraft line, such as a wide-body kept off a taxiway, a wingspan limit or a " +
                        "full-length departure requirement), current operational information such as a NOTAM closing a taxiway on " +
                        "the route, and at most one sentence saying that controllers usually route differently there; never give a " +
                        "full alternative route.", prompt);
        Assert.Contains("A suggested exit takes its side and distance from the block's exits list, and gets none when the list " +
                        "does not give them.", prompt);
        Assert.Contains("When the block gives no size class for the aircraft, say which aircraft a size restriction applies to.", prompt);
        Assert.DoesNotContain("at most one sentence per leg saying", prompt);
    }

    [Fact]
    public void The_check_line_and_suggestions_name_only_what_the_scenery_has()
    {
        // Owner's choice, 2026-09-27: a point that needs a name the scenery lacks is left out, never flagged — the guard
        // that stopped round 4's invented names (KMEM "V1", KATL "V3"/"V4").
        string prompt = Prompt();
        Assert.Contains("Any taxiway, exit or stand you name in the check line or the suggestions must appear in that leg's " +
                        "lines, including its \"Taxiway names at\" list; when a point could only be made with a name that is not " +
                        "there, leave the point out.", prompt);
        Assert.DoesNotContain("Any taxiway you name must appear", prompt);
    }

    [Fact]
    public void The_search_sentences_are_exact()
    {
        Assert.Equal("Web search is off for this briefing, so every check line begins " +
                     "\"Real-world check, from memory rather than live charts:\".", GeminiService.RouteSearchOffSentence);
        Assert.Equal("Web search is on for this briefing: you may look up each airport's current airport diagram and chart " +
                     "notes, and a check line says current charts only when that search found them.",
                     GeminiService.RouteSearchOnSentence);
    }

    [Fact]
    public void The_prompt_says_whether_this_briefing_has_web_search()
    {
        // A check line must never claim current charts the AI could not have looked at: web search is off by default for
        // both providers, and Claude's retry without the web_search tool has none either.
        string off = GeminiService.GetRouteDescriptionPrompt("DATA", webSearch: false);
        string on = GeminiService.GetRouteDescriptionPrompt("DATA", webSearch: true);
        Assert.Contains(GeminiService.RouteSearchOffSentence, off);
        Assert.DoesNotContain(GeminiService.RouteSearchOnSentence, off);
        Assert.Contains(GeminiService.RouteSearchOnSentence, on);
        Assert.DoesNotContain(GeminiService.RouteSearchOffSentence, on);
    }

    [Fact]
    public void The_search_flag_changes_nothing_else()
    {
        string off = GeminiService.GetRouteDescriptionPrompt("DATA", webSearch: false);
        string on = GeminiService.GetRouteDescriptionPrompt("DATA", webSearch: true);
        Assert.Equal(off.Replace(GeminiService.RouteSearchOffSentence, "<search>"),
                     on.Replace(GeminiService.RouteSearchOnSentence, "<search>"));
        Assert.EndsWith("DATA", on);
    }

    [Fact]
    public void The_taxi_section_stays_short()
    {
        string prompt = Prompt();
        Assert.Contains("do not list every exit", prompt);
        Assert.Contains("do not describe where the data came from beyond the preview phrase, the check line's opening and the " +
                        "OpenStreetMap and general-knowledge wording above", prompt);
        Assert.Contains("a representative stand (say it is typical, not assigned)", prompt);
        Assert.Contains("When a leg's route comes from OpenStreetMap, say so in a few words", prompt);
    }

    [Fact]
    public void An_OpenStreetMap_leg_is_never_called_the_scenery_route()
    {
        // "The expected route on the pilot's scenery" is false for an OSM-tier leg — a pilot could hear that AND
        // "taxi guidance cannot use it" about the same route.
        Assert.Contains("call it the expected route on OpenStreetMap's map rather than on the pilot's scenery", Prompt());
    }

    [Fact]
    public void A_runway_crossing_is_never_filtered_out_of_the_briefing()
    {
        // The old filter read as exhaustive and omitted the "no hold short point" and "leaves the runway on
        // taxiway X" notes, so an unheld crossing could be briefed as no crossing at all.
        string prompt = Prompt();
        Assert.Contains("Always give every runway the route crosses, including one a note says has no hold " +
                        "short point", prompt);
        Assert.Contains("when a note says the mapped route leaves the runway on another taxiway, say which", prompt);
        Assert.Contains("a stand the scenery marks as a fuel or other special stand", prompt);
    }

    [Fact]
    public void An_unavailable_leg_is_answered_from_general_knowledge_and_says_so()
    {
        string prompt = Prompt();
        Assert.Contains("saying it is general knowledge and not checked against the scenery", prompt);
        Assert.Contains("where the leg has a \"Taxiway names at\" list, name only taxiways from it", prompt);
        // Broader than a missing route: an unavailable taxi-in can still carry a computed exit and stand, and
        // "usual knowledge" makes no sense when the aircraft is already sitting at the runway entrance.
        Assert.Contains("still give whatever the block does give for that leg", prompt);
        Assert.Contains("When the reason is that the aircraft is already at the runway, give no route for that leg.", prompt);
        Assert.Contains("only a leg with no such list may name taxiways the block does not give", prompt);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteDescriptionPromptTests"`
Expected: build FAILS with `CS1739` ("does not have a parameter named 'webSearch'") and `CS0117` ("'GeminiService' does not contain a definition for 'RouteCheckFromMemory'", and the same for the other four constants). That compile failure is the red state.

- [ ] **Step 3a: Add the constants** — in `MSFSBlindAssist/Services/GeminiService.cs`, directly after the `RealWorldTaxiQuestion` constant (the line ending `"Please include the expected taxiways, hold short points, and any specific restrictions.";`), insert:

```csharp

    /// <summary>
    /// How each taxi leg's check line begins when the AI checked the scenery's route against its own knowledge of the airport
    /// — always so when the request has no web search (<see cref="GetRouteDescriptionPrompt"/>'s <c>webSearch</c>). Heard by
    /// the pilot, so it says what the check was made against (owner, 2026-09-27).
    /// </summary>
    internal const string RouteCheckFromMemory = "Real-world check, from memory rather than live charts:";

    /// <summary>How a check line begins only when a web search in that briefing found the airport's current charts.</summary>
    internal const string RouteCheckAgainstCharts = "Real-world check, against current charts:";

    /// <summary>
    /// How each taxi leg's optional suggestions paragraph begins: what the scenery cannot provide — preferred exits from the
    /// charts, size restrictions, usual routing — flagged so it is never mistaken for the scenery's route (owner, 2026-09-27).
    /// </summary>
    internal const string RouteSuggestionsOpening = "Real-world suggestions, not from your scenery:";

    /// <summary>Section 7's search sentence when the request carrying the prompt has no web search.</summary>
    internal const string RouteSearchOffSentence =
        $"Web search is off for this briefing, so every check line begins \"{RouteCheckFromMemory}\".";

    /// <summary>Section 7's search sentence when the request carrying the prompt has web search.</summary>
    internal const string RouteSearchOnSentence =
        "Web search is on for this briefing: you may look up each airport's current airport diagram and chart notes, " +
        "and a check line says current charts only when that search found them.";
```

- [ ] **Step 3b: Change the signature** — replace

```csharp
    /// <summary>
    /// Generates the prompt for route description.
    /// </summary>
    internal static string GetRouteDescriptionPrompt(string flightData)
    {
        return $@"You are writing a flight briefing
```

with

```csharp
    /// <summary>
    /// Generates the prompt for route description.
    /// </summary>
    /// <param name="webSearch">Whether the request carrying this prompt has web search (Gemini's grounding, Claude's
    /// web_search tool). It chooses section 7's search sentence and nothing else, so a taxi leg's check line can never
    /// claim current charts the AI could not have looked at.</param>
    internal static string GetRouteDescriptionPrompt(string flightData, bool webSearch)
    {
        string searchSentence = webSearch ? RouteSearchOnSentence : RouteSearchOffSentence;
        return $@"You are writing a flight briefing
```

(Only the lines shown change; the rest of that first prompt line stays as it is.)

- [ ] **Step 3c: Rewrite section 7** — inside the same verbatim string, replace every line from `7. TAXI OUT AND TAXI IN` down to and including the line that begins `   When a leg's note says SayIntentions assigned a different runway from the flight plan, say so here,` (the blank line after it and `IMPORTANT GUIDELINES:` stay) with exactly:

```text
7. TAXI OUT AND TAXI IN
   The flight plan data ends with a TAXI ROUTES block worked out from the pilot's own simulator scenery.
   Write this section as two legs, the taxi out at the departure airport and then the taxi in at the arrival airport, each in up to three parts in this order: the route paragraph, the check line and the suggestions paragraph, each part on its own line.
   For each leg, answer the question below from that leg's lines of the TAXI ROUTES block, taking the bracketed items (the airport, the runway, the stand or terminal, and the aircraft type) from them (the runway there may be the one SayIntentions assigned rather than the flight plan's):
      ""{RealWorldTaxiQuestion}""
   That question is an instruction to you, not text for the pilot: write only your answer, and never write the question itself into the briefing, as shown here or with the items filled in.
   For the taxi out, the route runs from the stand to the departure runway; for the taxi in, it runs from the landing runway, via the exit, to the stand.
   The route paragraph is one short paragraph per leg, in the voice of real-world operations, and apart from the general-knowledge route described below, nothing in it comes from your own knowledge.
   The route comes from the block only: the stand, the taxiways in order with the turn at each change of taxiway and into the stand wherever the block gives one, every hold-short point and the runway it protects, and for the arrival which side to leave the runway (left or right), the exit taxiway and its distance from the threshold, the next exit if that one is missed, every runway crossed, and the gate.
   Use ONLY the taxiway, exit and stand names given in the block, and repeat distances, sides and turn directions exactly as given; where the block gives no turn for a taxiway, give none.
   Give the total taxi distance for each leg the block gives one for, and never estimate one.
   At the end of each route paragraph, say in a short phrase that this is the expected route on the pilot's scenery and that SayIntentions or ATC will give the actual taxi clearance.
   Always give every runway the route crosses, including one a note says has no hold short point, and when a note says the mapped route leaves the runway on another taxiway, say which.
   Mention any other note from the block only when it changes what the pilot does or hears, such as a runway SayIntentions assigned that differs from the flight plan, a representative stand (say it is typical, not assigned), a SayIntentions gate the scenery lists under another name or at a different position, a stand the scenery marks as a fuel or other special stand, or a taxiway width or stand size note.
   When a leg's route comes from OpenStreetMap, say so in a few words, and call it the expected route on OpenStreetMap's map rather than on the pilot's scenery; taxi guidance cannot use it.
   If the block says a leg is unavailable, say so in a few words, and still give whatever the block does give for that leg, such as the exit with its side and distance, and the stand.
   When the reason is that the aircraft is already at the runway, give no route for that leg.
   Otherwise you may give that leg's usual route from your own knowledge, saying it is general knowledge and not checked against the scenery; where the leg has a ""Taxiway names at"" list, name only taxiways from it, and only a leg with no such list may name taxiways the block does not give.
   The check line is one sentence after each route paragraph that checks the block's route, exit and stand for that leg against the real airport as you know it.
   Begin it with ""{RouteCheckFromMemory}"", or with ""{RouteCheckAgainstCharts}"" only when a web search in this briefing found that airport's current airport diagram or chart notes.
   {searchSentence}
   When they agree, say so in a few words; when something differs, name what differs instead; when you do not know the airport well enough to check it, say so, and never claim an agreement you cannot support.
   Leave the check line out for a leg the block gives no route, exit or stand for.
   The suggestions paragraph comes after the check line, only when you have something to add that the scenery cannot provide, and otherwise is left out; it begins ""{RouteSuggestionsOpening}"" and has at most three short sentences.
   It may give a preferred exit from the real airport's charts, restrictions that apply to this aircraft's size (from the block's Aircraft line, such as a wide-body kept off a taxiway, a wingspan limit or a full-length departure requirement), current operational information such as a NOTAM closing a taxiway on the route, and at most one sentence saying that controllers usually route differently there; never give a full alternative route.
   A suggested exit takes its side and distance from the block's exits list, and gets none when the list does not give them.
   When the block gives no size class for the aircraft, say which aircraft a size restriction applies to.
   Any taxiway, exit or stand you name in the check line or the suggestions must appear in that leg's lines, including its ""Taxiway names at"" list; when a point could only be made with a name that is not there, leave the point out.
   Keep it short: do not list every exit, and do not describe where the data came from beyond the preview phrase, the check line's opening and the OpenStreetMap and general-knowledge wording above.
   Give every distance in this section in the unit the block's ""Distance unit"" line names, and never mix units.
   When a leg's note says SayIntentions assigned a different runway from the flight plan, say so here, and also in the DEPARTURE AND SID or ARRIVAL AND STAR section, naming both runways.
```

This is text inside the existing C# `$@"…"` string: `""` renders as one `"`, and `{RealWorldTaxiQuestion}`, `{RouteCheckFromMemory}`, `{RouteCheckAgainstCharts}`, `{searchSentence}` and `{RouteSuggestionsOpening}` are interpolated. Keep the three-space indentation. There are no blank lines inside the section. Do not add `{` or `}` anywhere else in the section.

- [ ] **Step 3d: Gemini passes its own search flag** — in `GeminiService.DescribeRouteAsync`, replace

```csharp
        string prompt = GetRouteDescriptionPrompt(flightData);
        bool enableSearch = SettingsManager.Current.GeminiSearchGrounding;
```

with

```csharp
        bool enableSearch = SettingsManager.Current.GeminiSearchGrounding;
        // The prompt is told whether THIS request can search, so a taxi leg's check line never claims current charts.
        string prompt = GetRouteDescriptionPrompt(flightData, webSearch: enableSearch);
```

- [ ] **Step 3e: Claude passes its own flag, and `false` on its retry** — in `ClaudeService.DescribeRouteAsync`, replace the method body from `string prompt = GeminiService.GetRouteDescriptionPrompt(flightData);` through the end of the `catch` block so the whole method reads:

```csharp
    public async Task<string> DescribeRouteAsync(string flightData)
    {
        bool enableSearch = SettingsManager.Current.ClaudeWebSearch;
        try
        {
            // The prompt is told whether THIS request can search, so a taxi leg's check line never claims current charts.
            string prompt = GeminiService.GetRouteDescriptionPrompt(flightData, webSearch: enableSearch);
            return RouteBriefingText.RemoveEchoedTaxiQuestion(await SendTextRequestAsync(prompt, enableSearch));
        }
        catch (HttpRequestException ex) when (enableSearch &&
            ex.Message.Contains("web_search", StringComparison.OrdinalIgnoreCase))
        {
            // The selected model doesn't support the web_search tool (the API's 400 body names
            // the rejected tool type, so it always contains "web_search" — a broader match like
            // "tool" would swallow unrelated 400s). Degrade to an ungrounded briefing rather
            // than failing, but SAY SO up front: a blind pilot who asked for NOTAM grounding
            // must not silently receive an ungrounded briefing as if it were current. The retry
            // has no search, so its prompt must say so too, or the taxi section's check lines
            // could claim current charts the model never looked at.
            string briefing = RouteBriefingText.RemoveEchoedTaxiQuestion(
                await SendTextRequestAsync(GeminiService.GetRouteDescriptionPrompt(flightData, webSearch: false), false));
            return "Note: web search is not available for the selected Claude model, so this " +
                   "briefing is not grounded with current NOTAM or weather data.\n\n" + briefing;
        }
    }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteDescriptionPromptTests"`
Expected: `Passed!` with 26 tests, 0 failed.

- [ ] **Step 5: Confirm that nothing outside section 7 moved** — run `git diff -U0 -- MSFSBlindAssist/Services/GeminiService.cs`. Every changed hunk must fall in one of four places: `DescribeRouteAsync`, the new constants after `RealWorldTaxiQuestion`, the `GetRouteDescriptionPrompt` doc comment and its first two lines, or the lines of section 7. No hunk may touch sections 1 to 6, `IMPORTANT GUIDELINES` or `FLIGHT PLAN DATA:`.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: every test passes; the total is the pre-task total plus 10 (the prompt test class goes from 16 tests to 26; the controller measured the pre-task total before dispatch).

- [ ] **Step 7: Commit (only these three files)**

```bash
git add MSFSBlindAssist/Services/GeminiService.cs MSFSBlindAssist/Services/ClaudeService.cs tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs
git commit -F - <<'EOF'
feat(briefing): each taxi leg is checked against the real airport, with labelled suggestions

Section 7 now gives each leg the scenery's route paragraph (block facts only), one check
line that says what it was checked against ("from memory rather than live charts" unless a
web search found the charts), and an optional "Real-world suggestions, not from your
scenery:" paragraph: preferred exits from the charts, size restrictions, current
operational information, at most one routing sentence. The check line and suggestions
name only the scenery's taxiways, exits and stands and drop a point rather than invent a
name (owner, 2026-09-27). GetRouteDescriptionPrompt takes the request's web-search flag;
Claude's retry without the web_search tool passes false.

Co-Authored-By: <your model line>
EOF
```

---

### Task 2: Docs and the CLAUDE.md invariant describe the checked section

Runs in parallel with Task 1 (docs only; no build).

**Files:**
- Modify: `docs/gemini.md`, the paragraph at line 20 that begins "The EFB's Describe Route briefing ends with a **TAXI OUT AND TAXI IN** section."
- Modify: `CLAUDE.md`, the bullet at line 837 that begins "- The briefing's taxi section is ONE short part".

**Interfaces:**
- Consumes: from Task 1, by name only: `GeminiService.RouteCheckFromMemory`, `RouteCheckAgainstCharts`, `RouteSuggestionsOpening`, `RouteSearchOffSentence`, `RouteSearchOnSentence`, and `GetRouteDescriptionPrompt(flightData, webSearch)`.
- Produces: nothing code depends on.

- [ ] **Step 1: Replace the docs/gemini.md paragraph.** Replace the whole paragraph (it is ONE line) that begins `The EFB's Describe Route briefing ends with a **TAXI OUT AND TAXI IN** section.` and ends `invented taxiway names (KMEM "V1", KATL "V3"/"V4") and was reverted.` with this ONE line:

```markdown
The EFB's Describe Route briefing ends with a **TAXI OUT AND TAXI IN** section. The app computes the expected taxi-out (stand → departure runway) and taxi-in (landing exit → stand) routes from the pilot's own scenery data and appends a plain-text `TAXI ROUTES` block to the SimBrief flight data for that AI call only (`ElectronicFlightBagForm.BuildTaxiRoutesBlockAsync`; the stored `ExtractedFlightData` stays pure SimBrief). The prompt's section 7 gives each leg up to three parts, in order and each on its own line (owner, 2026-09-27). (1) The **route paragraph** answers the owner's question ("Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. Please include the expected taxiways, hold short points, and any specific restrictions.") from the block alone, in the voice of real-world operations. The names, order, turns, sides and distances come only from the block, and nothing comes from the AI's own knowledge except an unavailable leg's general-knowledge route. It ends with the preview phrase: this is the expected route on the pilot's scenery, and SayIntentions or ATC gives the actual clearance. That matters because the route is the shortest one, and controllers do not always route that way (KATL's V goes around the west end of 08R/26L). (2) One **check line** checks the leg's route, exit and stand against the real airport. It begins `GeminiService.RouteCheckFromMemory` ("Real-world check, from memory rather than live charts:"), or `RouteCheckAgainstCharts` ("Real-world check, against current charts:") only when a web search in that briefing found the airport's current diagram or chart notes. It says the two agree, names what differs, or says the AI does not know the airport well enough, and never claims an agreement it cannot support. It is left out only for a leg the block gives no route, exit or stand for. (3) An optional **suggestions paragraph** begins `RouteSuggestionsOpening` ("Real-world suggestions, not from your scenery:") and has at most three short sentences, only when the scenery cannot provide something. It may give a preferred exit from the charts (with its side and distance from the block's exits list), restrictions for the aircraft's size from the block's Aircraft line (a wide-body kept off a taxiway, a wingspan limit, a full-length departure requirement), current operational information such as a NOTAM closing a taxiway on the route, and at most one sentence that controllers usually route differently. It never gives a full alternative route. The check line and the suggestions may name only taxiways, exits and stands in that leg's lines, including its "Taxiway names at" list: every edge name on the graph the leg was planned on (`TaxiLegBriefing.AirportTaxiways`). A point that could only be made with another name is left out. That was the owner's choice: a scenery out of date against the real airport is then not called out by name, but no invented name gets through. `GetRouteDescriptionPrompt(flightData, webSearch)` is told whether the request carrying it has web search (`GeminiSearchGrounding` / `ClaudeWebSearch`, both off by default). The flag swaps exactly one sentence of section 7 (`RouteSearchOffSentence` / `RouteSearchOnSentence`), so a check line can never claim live charts the AI could not have looked at. When a Claude model rejects the web_search tool, the retry rebuilds the prompt with `webSearch: false`. The taxi out runs stand → runway and the taxi in runway → exit → stand; the owner's question names only the second direction, so the prompt says so. Every runway the route crosses is always given. That includes one where no hold short point could be placed, which the block's hold line reports as "none could be placed; see the notes" rather than "no runway crossings" (`TaxiLegBriefing.UnheldRunways`). Any other note is given only when it changes what the pilot does or hears. An OpenStreetMap leg is called the expected route on OpenStreetMap's map. An unavailable leg still gives what the block has for it (exit, side, stand), and gets no route when the aircraft is already at the runway. Otherwise it may be answered from general knowledge, said to be so, with a total distance only where the block gives one. That is the one place a name outside the scenery can appear (owner confirmed, 2026-09-27). History: until 2026-09-26 the section had two parts (the computed route, then a "Real-world practice" answer), which the owner found too long. A real-world-only version the same day invented taxiway names (KMEM "V1", KATL "V3"/"V4") and was reverted. Round 5 (2026-09-26) told the route in one paragraph per leg with the AI's own additions mixed in. Round 6 (2026-09-27) moved those additions into the labelled suggestions paragraph and added the check line.
```

- [ ] **Step 2: Replace the CLAUDE.md bullet.** Replace the whole bullet (ONE line) that begins `- The briefing's taxi section is ONE short part` with this ONE line:

```markdown
- The briefing's taxi section gives each leg up to three parts, in order and each on its own line (owner, 2026-09-27). First, the ROUTE PARAGRAPH, built on the scenery-computed route: names, order, turns, sides and distances only from the block, and nothing from the AI's own knowledge except an unavailable leg's general-knowledge route. Never two versions (owner, 2026-09-26: the two-section form was too long, and the real-world-only form invented taxiway names, KMEM "V1" and KATL "V3"/"V4"). Second, one CHECK LINE that checks the leg against the real airport and says what it checked against: `GeminiService.RouteCheckFromMemory` unless a web search in that briefing found the airport's charts (`RouteCheckAgainstCharts`). It never claims an agreement it cannot support. Third, an optional SUGGESTIONS paragraph (`RouteSuggestionsOpening`, "Real-world suggestions, not from your scenery:"), at most three short sentences: preferred exits from the charts, restrictions for the aircraft's size, current operational information, and at most one sentence that controllers usually route differently. Never a full alternative route. `GetRouteDescriptionPrompt` must be told whether ITS OWN request has web search (`webSearch`; Claude's retry without the web_search tool passes false). The flag swaps exactly one sentence, because a check line claiming live charts it never had is a false assurance. Each route paragraph says it is the expected route on the pilot's scenery and that SayIntentions or ATC gives the actual clearance: the planner's route is the SHORTEST one, and controllers route by flows, one-way taxiways and end-around taxiways. Every runway the route crosses is always given, including one where no hold short point could be placed. The prompt's note filter must never become an exhaustive list that can silence it, and the block's hold line never says "No runway crossings" over such a crossing (`TaxiLegBriefing.UnheldRunways`). The check line and the suggestions may name only taxiways, exits and stands in that leg's lines, including its "Taxiway names at" list (`TaxiLegBriefing.AirportTaxiways`, every edge name on the graph the leg was planned on), and drop a point rather than name anything else. Aircraft classification for it is SimBrief-only (never the loaded aircraft or `WING SPAN`). Navdata taxiway widths are advisory notes, never a routing constraint: the commonest navdata width, 82 ft = 24.99 m on 62 % of fs2024 taxiway rows and 65 % of fs2020's, is judged at the block's 0.1 m precision, so it is never "below" code F's 25 m. → [gemini.md](docs/gemini.md)
```

- [ ] **Step 3: Verify the edits**

Run: `grep -c "ONE short part" CLAUDE.md; grep -c "gives each leg up to three parts" CLAUDE.md; grep -c "Round 6 (2026-09-27)" docs/gemini.md; grep -c "one sentence per leg" CLAUDE.md docs/gemini.md`
Expected: `0`, `1`, `1`, then `CLAUDE.md:0` and `docs/gemini.md:0`. Then run `git diff --stat`. Only `CLAUDE.md` and `docs/gemini.md` should appear among YOUR changes: Task 1's three files may also show, because it runs at the same time; leave them alone.

- [ ] **Step 4: Commit (only these two files)**

```bash
git add CLAUDE.md docs/gemini.md
git commit -F - <<'EOF'
docs(briefing): the taxi section's check line and real-world suggestions

docs/gemini.md and the CLAUDE.md invariant now describe each leg's three parts: the
scenery's route paragraph, one check line that says what it was checked against, and the
optional "Real-world suggestions, not from your scenery:" paragraph, plus the prompt's
web-search flag and the rule that the check line and suggestions name only the scenery's
taxiways, exits and stands (owner, 2026-09-27).

Co-Authored-By: <your model line>
EOF
```

---

### Task 3: Whole-round verification and review (controller, after Tasks 1 and 2)

**Files:** none changed unless the review finds something.

- [ ] **Step 1: Build the solution the way the app runs**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded.` with 0 errors. The worktree has no untracked navdatareader source; that affects only the app's output folder, not the build.

- [ ] **Step 2: Run the full suite**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass; the total is the pre-round total plus 10.

- [ ] **Step 3: Scope check against the round's base**

Run: `git diff --stat c47ecb3e..HEAD`
Expected, exactly seven paths: the spec and this plan (both under `docs/superpowers/`), `CLAUDE.md`, `docs/gemini.md`, `MSFSBlindAssist/Services/ClaudeService.cs`, `MSFSBlindAssist/Services/GeminiService.cs` and `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs`.

- [ ] **Step 4: Render and read the new prompt once.** Run a scratch C# snippet, or temporarily call `GetRouteDescriptionPrompt("DATA", true)` from a test, then delete it. Read section 7 end to end: it must match the spec §4 text exactly, with the openings and the search sentence filled in, and no `{` or `}` left over.

- [ ] **Step 5: Code review** of `c47ecb3e..HEAD` with superpowers:requesting-code-review. Fix every real finding test-first, then re-run Steps 1 and 2.

- [ ] **Step 6: Hand-off (ask the owner first).**
  1. Fast-forward `feature/route-briefing-taxi-routes` to this branch from the owner's main folder (`git merge --ff-only claude/route-briefing-taxi-refine-2963c1`), only after the owner says yes. That folder has the feature branch checked out.
  2. Refresh the owner's one test build `D:\Claude\oasis1701\builds\MSFSBA-route-briefing-pr160-debug\`:
     - Create a detached throw-away worktree at this branch and `git fetch fork` first.
     - `git merge fork/feature/first-officer` there only, then `dotnet build MSFSBlindAssist.sln -c Debug` and the full tests.
     - Run `robocopy <bin\x64\Debug\net10.0-windows> <builds folder> /MIR /XD navdatareader /XF SimConnect.dll` from PowerShell; exit codes below 8 mean success.
     - Run `dotnet build-server shutdown`, then remove the throw-away worktree.
     - Never merge PR #160 into the branch.
  3. Update the project memory file `route-briefing-taxi-routes-in-progress.md` with round 6.
