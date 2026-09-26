# Route Briefing Follow-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The EFB route briefing never shows the AI's own question, takes SayIntentions' gate (file or parking service) and runways when SayIntentions is flying this flight, and gives a turn direction at every taxiway change of the computed routes.

**Architecture:** Four pure helpers carry the logic and the tests — `RouteBriefingText` (echo removal), `SayIntentionsArrivalGate` + `BriefingRunwayChoice` (SayIntentions inputs), `BriefingTurns` (turn geometry) — and one integration task threads them through the models, planner, renderer, EFB form and MainForm wiring. The AI prompt (shared by Gemini and Claude) changes in one place.

**Tech Stack:** .NET 10 / C# 13, Windows Forms, xUnit (`tests/MSFSBlindAssist.Tests`).

**Spec:** [docs/superpowers/specs/2026-09-26-route-briefing-echo-si-runways-turns-design.md](../specs/2026-09-26-route-briefing-echo-si-runways-turns-design.md)

## Global Constraints

- Build ONLY with `dotnet build MSFSBlindAssist.sln -c Debug` (or a project with `-p:Platform=x64`); never the bare `.csproj`.
- Test command: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` (add `--filter "FullyQualifiedName~<Class>"` for one class).
- Every `RegexOptions.IgnoreCase` also carries `RegexOptions.CultureInvariant`.
- Numbers written to logs or to the TAXI ROUTES block use `CultureInfo.InvariantCulture`.
- All logging through `MSFSBlindAssist.Utils.Logging.Log` / an existing `LogChannel`; never a raw file write.
- Exact pilot-facing strings (copy verbatim):
  - `SayIntentions has assigned this runway too`
  - `runway {si} is the runway SayIntentions assigned; the flight plan names {plan}`
  - `runway {si} is the runway SayIntentions assigned; the flight plan names no runway`
  - `SayIntentions' parking service named {label}, but its position is not at {icao}; using a representative stand instead`
  - turn words: `straight ahead`, `slight left`, `slight right`, `left`, `right`, `sharp left`, `sharp right`
  - block: `{turn} onto {taxiway}` and `then {turn} into the stand`
- Do not change Taxi Assist, `TaxiGuidanceManager`, the SayIntentions window, or the SimBrief data sent to the AI.
- No changelog fragment (this is iteration on the unmerged route-briefing branch).
- Commit messages end with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. NEVER `git push`.
- `docs/superpowers/` is gitignored; add files there with `git add -f`.

## Execution waves

- **Wave 1 (parallel, each in its own isolated worktree):** Task 1, Task 2, Task 3 — they touch disjoint files.
- **Wave 2 (after Wave 1 is merged):** Task 4 and Task 5 in parallel (Task 5 touches docs only).
- **Wave 3:** full build + full test suite + final review.

---

### Task 1: The question is asked, never shown (prompt + safety net)

**Files:**
- Create: `MSFSBlindAssist/Services/RouteBriefingText.cs`
- Modify: `MSFSBlindAssist/Services/GeminiService.cs` (`DescribeRouteAsync` ~line 660; `GetRouteDescriptionPrompt` ~lines 900–983)
- Modify: `MSFSBlindAssist/Services/ClaudeService.cs` (`DescribeRouteAsync` ~lines 91–111)
- Create: `tests/MSFSBlindAssist.Tests/RouteBriefingTextTests.cs`
- Modify: `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs`

**Interfaces:**
- Produces: `internal const string GeminiService.RealWorldTaxiQuestion`; `public static class RouteBriefingText` with `public const string QuestionOpening`, `SecondSentenceOpening`, `QuestionEnding` and `public static string RemoveEchoedTaxiQuestion(string text)`.
- Consumes: nothing from other tasks.

- [ ] **Step 1: Write the failing safety-net tests**

Create `tests/MSFSBlindAssist.Tests/RouteBriefingTextTests.cs`:

```csharp
// tests/MSFSBlindAssist.Tests/RouteBriefingTextTests.cs
using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteBriefingTextTests
{
    // The live KMEM→KATL briefing (Gemini gemini-pro-latest, 2026-09-26), taxi section, as the pilot received it: the
    // prompt's own question, blanks filled in, opened both "Real-world practice" parts.
    private const string LiveTaxiSection =
        "TAXI OUT AND TAXI IN\n" +
        "\n" +
        "The computed route for taxi out at KMEM begins from stand 12 and proceeds to runway 18R. The taxiways in order are J, N, M, and M8, covering a distance of 2.1 kilometers. You will hold short of runway 18R on taxiway M8 before entering.\n" +
        "\n" +
        "Real-world practice\n" +
        "Provide the step-by-step taxi route at KMEM from stand 12 to runway 18R in a FENIX A320. Please include the expected taxiways, hold short points, and any specific restrictions.\n" +
        "\n" +
        "In real-world operations, assuming stand 12 is located at the main terminal area, you would typically push back and join taxiway S or C to transition outward.\n" +
        "\n" +
        "The computed route for taxi in at KATL begins after landing on runway 08L.\n" +
        "\n" +
        "Real-world practice\n" +
        "Provide the step-by-step taxi route at KATL from runway 08L to representative stand C 22 in a FENIX A320. Please include the expected taxiways, hold short points, and any specific restrictions.\n" +
        "\n" +
        "In real-world operations, after landing on runway 08L and exiting to the right at B11, you will immediately hold short of the parallel runway 08R.";

    private const string LiveTaxiSectionCleaned =
        "TAXI OUT AND TAXI IN\n" +
        "\n" +
        "The computed route for taxi out at KMEM begins from stand 12 and proceeds to runway 18R. The taxiways in order are J, N, M, and M8, covering a distance of 2.1 kilometers. You will hold short of runway 18R on taxiway M8 before entering.\n" +
        "\n" +
        "Real-world practice\n" +
        "\n" +
        "In real-world operations, assuming stand 12 is located at the main terminal area, you would typically push back and join taxiway S or C to transition outward.\n" +
        "\n" +
        "The computed route for taxi in at KATL begins after landing on runway 08L.\n" +
        "\n" +
        "Real-world practice\n" +
        "\n" +
        "In real-world operations, after landing on runway 08L and exiting to the right at B11, you will immediately hold short of the parallel runway 08R.";

    [Fact]
    public void The_live_KMEM_KATL_echoes_are_removed_and_nothing_else_changes()
        => Assert.Equal(LiveTaxiSectionCleaned, RouteBriefingText.RemoveEchoedTaxiQuestion(LiveTaxiSection));

    [Fact]
    public void A_reply_without_the_question_is_returned_unchanged()
    {
        string text = "FLIGHT OVERVIEW\n\nThe computed taxi route to runway 18R is 2.1 km.\nPlease include your callsign.\n";
        Assert.Same(text, RouteBriefingText.RemoveEchoedTaxiQuestion(text));
    }

    [Fact]
    public void The_question_on_the_heading_line_is_removed_and_the_answer_kept()
        => Assert.Equal("Real-world practice: In real-world operations, you would push back onto S.",
            RouteBriefingText.RemoveEchoedTaxiQuestion(
                "Real-world practice: Provide the step-by-step taxi route at KMEM from stand 12 to runway 18R in a FENIX A320. " +
                "Please include the expected taxiways, hold short points, and any specific restrictions. " +
                "In real-world operations, you would push back onto S."));

    [Theory]
    [InlineData("Question: \"Provide the step-by-step taxi route at KATL from runway 08L to gate C22 in an A320. Please include the expected taxiways, hold short points, and any specific restrictions.\"")]
    [InlineData("“Provide the step-by-step taxi route at KATL from runway 08L to gate C22 in an A320. Please include the expected taxiways, hold short points, and any specific restrictions.”")]
    [InlineData("Prompt: Provide the step-by-step taxi route at KATL from runway 08L to gate C22 in an A320. Please include the expected taxiways, hold short points, and any specific restrictions.")]
    public void A_quoted_or_labelled_question_is_removed_with_its_line(string echo)
        => Assert.Equal("Real-world practice\nAnswer.",
            RouteBriefingText.RemoveEchoedTaxiQuestion("Real-world practice\n" + echo + "\nAnswer."));

    [Fact]
    public void A_question_split_over_two_lines_is_removed()
        => Assert.Equal("Real-world practice\nAnswer.",
            RouteBriefingText.RemoveEchoedTaxiQuestion(
                "Real-world practice\n" +
                "Provide the step-by-step taxi route at KMEM from stand 12 to runway 18R in a FENIX A320.\n" +
                "Please include the expected taxiways, hold short points, and any specific restrictions.\n" +
                "Answer."));

    [Fact]
    public void Only_the_first_sentence_echoed_is_removed_and_the_answer_after_it_kept()
        => Assert.Equal("In real-world operations, you push back onto S.",
            RouteBriefingText.RemoveEchoedTaxiQuestion(
                "Provide the step-by-step taxi route at KMEM from stand 12 to runway 18R in a FENIX A320. " +
                "In real-world operations, you push back onto S."));

    [Fact]
    public void The_literal_template_is_removed()
        => Assert.Equal("Real-world practice\nAnswer.",
            RouteBriefingText.RemoveEchoedTaxiQuestion(
                "Real-world practice\n" +
                "Provide the step-by-step taxi route at [ICAO] from [runway] to [terminal/gate] in a [aircraft type]. " +
                "Please include the expected taxiways, hold short points, and any specific restrictions.\n" +
                "Answer."));

    [Fact]
    public void Crlf_line_ends_are_kept()
        => Assert.Equal("Real-world practice\r\n\r\nAnswer.\r\n",
            RouteBriefingText.RemoveEchoedTaxiQuestion(
                "Real-world practice\r\n" +
                "Provide the step-by-step taxi route at KMEM from stand 12 to runway 18R in a FENIX A320. " +
                "Please include the expected taxiways, hold short points, and any specific restrictions.\r\n" +
                "\r\n" +
                "Answer.\r\n"));

    [Fact]
    public void Case_is_ignored()
        => Assert.Equal("Answer.",
            RouteBriefingText.RemoveEchoedTaxiQuestion(
                "PROVIDE THE STEP-BY-STEP TAXI ROUTE AT KMEM FROM STAND 12 TO RUNWAY 18R IN A FENIX A320. " +
                "PLEASE INCLUDE THE EXPECTED TAXIWAYS, HOLD SHORT POINTS, AND ANY SPECIFIC RESTRICTIONS.\nAnswer."));

    [Fact]
    public void Turkish_culture_does_not_stop_the_match()
    {
        // Under tr-TR an IgnoreCase pattern's "i" no longer matches "I" unless the regex is CultureInvariant.
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("Answer.",
                RouteBriefingText.RemoveEchoedTaxiQuestion(
                    "PROVIDE THE STEP-BY-STEP TAXI ROUTE AT KMEM FROM STAND 12 TO RUNWAY 18R IN A FENIX A320. " +
                    "PLEASE INCLUDE THE EXPECTED TAXIWAYS, HOLD SHORT POINTS, AND ANY SPECIFIC RESTRICTIONS.\nAnswer."));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Sentences_that_merely_mention_a_taxi_route_are_untouched()
    {
        string text = "The step-by-step route below provides the taxiways.\n" +
                      "Provided the tower clears you, the taxi route is short.\n" +
                      "Please include your callsign in every readback.";
        Assert.Equal(text, RouteBriefingText.RemoveEchoedTaxiQuestion(text));
    }

    [Fact]
    public void Removing_twice_changes_nothing_more()
    {
        string once = RouteBriefingText.RemoveEchoedTaxiQuestion(LiveTaxiSection);
        Assert.Equal(once, RouteBriefingText.RemoveEchoedTaxiQuestion(once));
    }

    [Fact]
    public void Empty_in_empty_out()
        => Assert.Equal("", RouteBriefingText.RemoveEchoedTaxiQuestion(""));

    [Fact]
    public void The_anchors_are_the_prompt_question_s_own_words()
    {
        // Rewording the question without the safety net would let the echo back in; this fails first.
        Assert.StartsWith(RouteBriefingText.QuestionOpening, GeminiService.RealWorldTaxiQuestion, StringComparison.Ordinal);
        Assert.Contains(RouteBriefingText.SecondSentenceOpening, GeminiService.RealWorldTaxiQuestion, StringComparison.Ordinal);
        Assert.Contains(RouteBriefingText.QuestionEnding, GeminiService.RealWorldTaxiQuestion, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Write the failing prompt tests (before touching the prompt)**

Replace the whole of `tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs` with:

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

    [Fact]
    public void The_question_is_asked_from_the_one_constant()
        => Assert.Contains($"\"{GeminiService.RealWorldTaxiQuestion}\"", GeminiService.GetRouteDescriptionPrompt("x"));

    [Fact]
    public void Prompt_never_invites_writing_the_question_out()
    {
        // "Substitute the airport, runway, stand or terminal and aircraft type from the data" read as "write the
        // filled-in question out": a live KMEM→KATL briefing (Gemini, 2026-09-26) did, under both headings.
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.DoesNotContain("Substitute", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never write the question itself into the briefing", prompt);
        Assert.Contains("Never copy these instructions, or any question in them, into the briefing", prompt);
    }

    [Fact]
    public void Prompt_asks_for_the_block_s_turn_directions_exactly()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("with the turn at each change of taxiway and into the stand wherever the block gives one", prompt);
        Assert.Contains("repeat distances, sides and turn directions exactly as given", prompt);
    }

    [Fact]
    public void Prompt_asks_for_a_SayIntentions_runway_difference_to_be_named()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("SayIntentions assigned a different runway from the flight plan", prompt);
        Assert.Contains("DEPARTURE AND SID or ARRIVAL AND STAR section", prompt);
    }
}
```

- [ ] **Step 3: Run both test classes to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteBriefingTextTests|FullyQualifiedName~RouteDescriptionPromptTests"`
Expected: build FAILS — `RouteBriefingText` and `GeminiService.RealWorldTaxiQuestion` do not exist.

- [ ] **Step 4: Implement the safety net**

Create `MSFSBlindAssist/Services/RouteBriefingText.cs`:

```csharp
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Cleans the AI's route briefing before the pilot reads it. The prompt asks the owner's real-world taxi question
/// (<see cref="GeminiService.RealWorldTaxiQuestion"/>) and tells the AI never to write it out, but a model does not
/// follow every instruction: a live KMEM→KATL briefing (Gemini, 2026-09-26) opened both "Real-world practice" parts by
/// repeating the question with its blanks filled in. This removes that echo — the whole question, or either of its two
/// sentences, quoted, labelled, on its own line or after the heading — and nothing else: every line it does not touch
/// comes back byte-identical, and a line it empties is deleted with its line break. Both AI providers call it on their
/// route briefing.
/// </summary>
public static class RouteBriefingText
{
    /// <summary>The question's first words, before its first blank. A test pins that the question starts with them.</summary>
    public const string QuestionOpening = "Provide the step-by-step taxi route";

    /// <summary>The first words of the question's second sentence.</summary>
    public const string SecondSentenceOpening = "Please include the expected taxiways";

    /// <summary>The question's last words.</summary>
    public const string QuestionEnding = "any specific restrictions";

    // IgnoreCase always with CultureInvariant: under tr-TR the pattern's "i" would otherwise stop matching an "I".
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Label = @"(?:(?:Question|Prompt)\s*:\s*)?";
    private const string Quote = "[\"“”]?";

    // 1. The whole question, wherever it is on the line, with the spaces after it.
    private static readonly Regex WholeQuestion = new(
        Label + Quote + Regex.Escape(QuestionOpening) + @"[^\r\n]*?" + Regex.Escape(QuestionEnding) +
        @"\.?" + Quote + @"[ \t]*", Options);

    // 2. Its first sentence alone: to the first period followed by whitespace or the end of the line, else to the end.
    private static readonly Regex FirstSentence = new(
        Label + Quote + Regex.Escape(QuestionOpening) + @"[^\r\n]*?(?:\." + Quote + @"(?=\s|$)|$)[ \t]*", Options);

    // 3. Its second sentence alone.
    private static readonly Regex SecondSentence = new(
        Quote + Regex.Escape(SecondSentenceOpening) + @"[^\r\n]*?" + Regex.Escape(QuestionEnding) +
        @"\.?" + Quote + @"[ \t]*", Options);

    /// <summary>The briefing with every echo of the question removed; the same instance when there is none.</summary>
    public static string RemoveEchoedTaxiQuestion(string text)
    {
        if (string.IsNullOrEmpty(text) ||
            (text.IndexOf(QuestionOpening, StringComparison.OrdinalIgnoreCase) < 0 &&
             text.IndexOf(SecondSentenceOpening, StringComparison.OrdinalIgnoreCase) < 0))
            return text;

        var lines = text.Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            bool carriageReturn = line.EndsWith('\r');
            string content = carriageReturn ? line[..^1] : line;
            string cleaned = SecondSentence.Replace(FirstSentence.Replace(WholeQuestion.Replace(content, ""), ""), "");
            if (string.Equals(cleaned, content, StringComparison.Ordinal))
            {
                kept.Add(line);
                continue;
            }
            if (cleaned.Trim().Length == 0) continue;   // emptied by the removal: drop the line and its break
            kept.Add(carriageReturn ? cleaned + "\r" : cleaned);
        }
        return string.Join('\n', kept);
    }
}
```

- [ ] **Step 5: Add the question constant and reword the prompt**

In `MSFSBlindAssist/Services/GeminiService.cs`, directly ABOVE the existing summary `/// Generates the prompt for route description.` (around line 900), insert:

```csharp
    /// <summary>
    /// The owner's real-world taxi question, asked for each leg under "Real-world practice". An INSTRUCTION to the AI,
    /// never text for the briefing: the prompt forbids writing it out, and <see cref="RouteBriefingText"/> removes it
    /// if it comes back anyway (live KMEM→KATL, 2026-09-26).
    /// </summary>
    internal const string RealWorldTaxiQuestion =
        "Provide the step-by-step taxi route at [ICAO] from [runway] to [terminal/gate] in a [aircraft type]. " +
        "Please include the expected taxiways, hold short points, and any specific restrictions.";

```

In the same file, inside `GetRouteDescriptionPrompt`, replace this exact block (section 7):

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
   b) Then, under the heading ""Real-world practice"", answer this from your own knowledge of the
      airport: ""Provide the step-by-step taxi route at [ICAO] from [runway] to [terminal/gate] in a [aircraft type]. Please include the expected taxiways, hold short points, and any specific restrictions.""
      Substitute the airport, runway, stand or terminal and aircraft type from the data. Do it for
      the departure (from the stand to the runway) and for the arrival (from the runway, via the
      expected exit, to the terminal or gate). Mention wingspan or aircraft-type restrictions on
      taxiways and stands where you know of them. Where your route differs from the computed one,
      say so and say which is which; never present your own route as the computed one. When the
      block says no ground data exists for an airport, this real-world route is the answer for
      that leg and should be given in full.
```

with:

```
7. TAXI OUT AND TAXI IN
   The flight plan data ends with a TAXI ROUTES block computed by the pilot's own simulator
   scenery. For each of the two legs (taxi out at the departure airport, taxi in at the arrival
   airport) do two things, in this order:
   a) Describe the computed route in prose: the stand it starts from (say plainly when the block
      calls it a representative stand rather than an assignment), the taxiways in order
      with the turn at each change of taxiway and into the stand wherever the block gives one,
      every hold-short point and which runway it protects, and for the arrival which side to leave
      the runway (left or right), the exit taxiway and its distance from the threshold, and the
      fallback exit if that one is missed.
      Use ONLY the taxiway, exit and stand names given in the block for this part, and
      repeat distances, sides and turn directions exactly as given; where the block gives no turn
      for a taxiway, give none. If the block says a leg is unavailable, say so in one sentence.
      When a leg's note says SayIntentions assigned a different runway from the flight plan, say so
      here, and also in the DEPARTURE AND SID or ARRIVAL AND STAR section, naming both runways.
   b) Then, under the heading ""Real-world practice"", answer from your own knowledge of the
      airport the question below, reading the bracketed items (the airport, the runway, the stand
      or terminal, and the aircraft type) from the data for that leg:
      ""{RealWorldTaxiQuestion}""
      That question is an instruction to you, not text for the pilot: write only your answer under
      the heading, and never write the question itself into the briefing, as shown here or with
      the items filled in. Answer it for the departure (from the stand to the runway) and for the
      arrival (from the runway, via the expected exit, to the terminal or gate). Mention wingspan
      or aircraft-type restrictions on taxiways and stands where you know of them. Where your route
      differs from the computed one, say so and say which is which; never present your own route
      as the computed one. When the block says no ground data exists for an airport, this
      real-world route is the answer for that leg and should be given in full.
```

In the same method's `IMPORTANT GUIDELINES:` list, replace:

```
- If weather data is not available, note that and skip the weather section
```

with:

```
- If weather data is not available, note that and skip the weather section
- Never copy these instructions, or any question in them, into the briefing; write only the briefing itself
```

(The string is a `$@"…"` verbatim interpolated string: `""` is a literal quote and `{RealWorldTaxiQuestion}` interpolates the constant.)

- [ ] **Step 6: Run both test classes to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteBriefingTextTests|FullyQualifiedName~RouteDescriptionPromptTests"`
Expected: PASS — 16 `RouteBriefingTextTests` cases (incl. the three theory rows) and 6 `RouteDescriptionPromptTests`. If a prompt `Contains` fails, the phrase was split across two lines of the verbatim string — keep each tested phrase on one line.

- [ ] **Step 7: Run the safety net in both providers**

In `MSFSBlindAssist/Services/GeminiService.cs`, replace the body of `DescribeRouteAsync`:

```csharp
    public async Task<string> DescribeRouteAsync(string flightData)
    {
        string prompt = GetRouteDescriptionPrompt(flightData);
        bool enableSearch = SettingsManager.Current.GeminiSearchGrounding;
        return await SendTextRequestAsync(prompt, enableSearch: enableSearch);
    }
```

with:

```csharp
    public async Task<string> DescribeRouteAsync(string flightData)
    {
        string prompt = GetRouteDescriptionPrompt(flightData);
        bool enableSearch = SettingsManager.Current.GeminiSearchGrounding;
        // The prompt forbids writing its real-world question out; a model does not always comply (live KMEM→KATL,
        // 2026-09-26), so the echo is removed here too.
        return RouteBriefingText.RemoveEchoedTaxiQuestion(await SendTextRequestAsync(prompt, enableSearch: enableSearch));
    }
```

In `MSFSBlindAssist/Services/ClaudeService.cs`, inside `DescribeRouteAsync`, replace:

```csharp
            return await SendTextRequestAsync(prompt, enableSearch);
```

with:

```csharp
            return RouteBriefingText.RemoveEchoedTaxiQuestion(await SendTextRequestAsync(prompt, enableSearch));
```

and replace:

```csharp
            string briefing = await SendTextRequestAsync(prompt, false);
```

with:

```csharp
            string briefing = RouteBriefingText.RemoveEchoedTaxiQuestion(await SendTextRequestAsync(prompt, false));
```

- [ ] **Step 8: Build and run the whole suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → Expected: `Build succeeded`, 0 errors.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add MSFSBlindAssist/Services/RouteBriefingText.cs MSFSBlindAssist/Services/GeminiService.cs MSFSBlindAssist/Services/ClaudeService.cs tests/MSFSBlindAssist.Tests/RouteBriefingTextTests.cs tests/MSFSBlindAssist.Tests/RouteDescriptionPromptTests.cs
git commit -m "fix(briefing): ask the real-world taxi question, never show it" -m "The prompt told the AI to substitute the data into the owner's question, and a live KMEM-KATL Gemini briefing wrote the filled-in question out under both Real-world practice headings. The prompt now forbids it, and both providers remove any echo that comes back." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: SayIntentions inputs — the parking-service gate, the runway choice, the parking log

**Files:**
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs:15-17`
- Modify: `MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs:1-6` (usings) and `:488-508` (`SayIntentionsArrivalGate`)
- Create: `MSFSBlindAssist/Navigation/Briefing/BriefingRunwayChoice.cs`
- Modify: `MSFSBlindAssist/Services/SayIntentions/SayIntentionsService.cs` (usings; `FetchParkingAsync` ~line 616)
- Modify: `tests/MSFSBlindAssist.Tests/BriefingStandPickerTests.cs` (append inside the class, after the last `SayIntentionsArrivalGate.From` test)
- Create: `tests/MSFSBlindAssist.Tests/BriefingRunwayChoiceTests.cs`
- Create: `tests/MSFSBlindAssist.Tests/SayIntentionsParkingLogTests.cs`

**Interfaces:**
- Produces:
  - `public enum SayIntentionsGateSource { FlightFile, ParkingService }` (namespace `MSFSBlindAssist.Navigation.Briefing`)
  - `public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position, SayIntentionsGateSource Source = SayIntentionsGateSource.FlightFile)`
  - `SayIntentionsArrivalGate.IsThisFlight([NotNullWhen(true)] SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao) : bool`
  - `SayIntentionsArrivalGate.From(SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao)` (unchanged signature)
  - `SayIntentionsArrivalGate.From(SayIntentionsFlightContext? ctx, SayIntentionsParking? parking, string departureIcao, string arrivalIcao) : SayIntentionsGateHint?`
  - `SayIntentionsArrivalGate.FromStatus(SayIntentionsStatusResult? status, string departureIcao, string arrivalIcao) : SayIntentionsGateHint?`
  - `public readonly record struct BriefingRunway(string Runway, string? Note)`
  - `BriefingRunwayChoice.AgreesNote` const and `BriefingRunwayChoice.Choose(string? planRunway, string? siRunway, bool siIsThisFlight) : BriefingRunway`
  - `internal static string SayIntentionsService.DescribeParking(SayIntentionsParking parking)`
- Consumes: `TaxiBriefingPlanner.RunwayIdsMatch(string?, string?)` (existing, internal).

- [ ] **Step 1: Write the failing gate tests**

In `tests/MSFSBlindAssist.Tests/BriefingStandPickerTests.cs`, after the test `The_assigned_gate_label_is_trimmed` (the last method of the class), add:

```csharp

    // ── SayIntentionsArrivalGate: the parking-service fallback ─────────────────────────────

    private static SayIntentionsParking Parking(string? name, double? lat = null, double? lon = null) =>
        new() { Name = name, Latitude = lat, Longitude = lon };

    [Fact]
    public void The_flight_file_gate_wins_over_the_parking_service()
    {
        var ctx = Ctx(true, "KMEM", "KATL", "Terminal 1 Gate 17");
        ctx.AssignedGatePosition = new GeoPoint(33.64, -84.43);
        var hint = SayIntentionsArrivalGate.From(ctx, Parking("B 12", 35.04, -89.98), "KMEM", "KATL")!;

        Assert.Equal("Terminal 1 Gate 17", hint.Label);
        Assert.Equal(33.64, hint.Position!.Value.Latitude);
        Assert.Equal(SayIntentionsGateSource.FlightFile, hint.Source);
    }

    [Fact]
    public void With_no_gate_in_the_file_the_parking_service_gate_is_used_with_its_own_position()
    {
        // Live KMEM→KATL (2026-09-26): flight.json's assigned_gate was empty while SayIntentions showed a concourse B
        // arrival gate, served only by getParking — which MSFS Blind Assist's SayIntentions window already fell back to.
        var hint = SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", ""), Parking(" B 12 ", 33.6407, -84.4277), "KMEM", "KATL")!;

        Assert.Equal("B 12", hint.Label);
        Assert.Equal(new GeoPoint(33.6407, -84.4277), hint.Position!.Value);
        Assert.Equal(SayIntentionsGateSource.ParkingService, hint.Source);
    }

    [Fact]
    public void A_parking_service_gate_never_takes_the_file_s_position()
    {
        var ctx = Ctx(true, "KMEM", "KATL", null);
        ctx.AssignedGatePosition = new GeoPoint(33.64, -84.43);   // a stray coordinate with no gate name beside it
        Assert.Null(SayIntentionsArrivalGate.From(ctx, Parking("B 12"), "KMEM", "KATL")!.Position);
    }

    [Fact]
    public void A_parking_service_position_at_zero_zero_is_no_position()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 0, 0), "KMEM", "KATL")!.Position);

    [Fact]
    public void A_parking_service_position_missing_a_coordinate_is_no_position()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 33.64, null), "KMEM", "KATL")!.Position);

    [Fact]
    public void No_gate_anywhere_means_no_hint()
    {
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), null, "KMEM", "KATL"));
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", " "), Parking("  "), "KMEM", "KATL"));
    }

    [Fact]
    public void Another_flight_s_parking_gate_is_ignored()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KDFW", null), Parking("B 12"), "KMEM", "KATL"));

    [Fact]
    public void The_status_overload_reads_the_file_and_the_parking_service()
    {
        var status = new SayIntentionsStatusResult(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 33.6407, -84.4277), null);
        Assert.Equal("B 12", SayIntentionsArrivalGate.FromStatus(status, "KMEM", "KATL")!.Label);
        Assert.Null(SayIntentionsArrivalGate.FromStatus(null, "KMEM", "KATL"));
    }

    [Fact]
    public void This_flight_means_the_file_exists_and_both_airports_match()
    {
        Assert.True(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, "kmem ", "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(false, "KMEM", "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, "KMEM", "KDFW", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, null, "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(null, "KMEM", "KATL"));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BriefingStandPickerTests"`
Expected: build FAILS — `SayIntentionsGateSource`, the 4-argument `From`, `FromStatus` and `IsThisFlight` do not exist.

- [ ] **Step 3: Implement the gate source and the fallback**

In `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs`, replace:

```csharp
/// <summary>SayIntentions' ARRIVAL gate — the label it published and, when it did, the stand's position.
/// Only ever built by <see cref="SayIntentionsArrivalGate"/>, which checks the flight matches this OFP.</summary>
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position);
```

with:

```csharp
/// <summary>Where SayIntentions published the arrival gate: its flight file's <c>assigned_gate</c>, or — when the file
/// has none yet — its SAPI parking service (<c>getParking</c>), the fallback MSFS Blind Assist's SayIntentions window
/// and Taxi Assist's import already use. SAPI does not say whether that service means the arrival gate or the
/// aircraft's current parking, so the planner refuses a parking-service gate whose position is not at the arrival
/// airport.</summary>
public enum SayIntentionsGateSource { FlightFile, ParkingService }

/// <summary>SayIntentions' ARRIVAL gate — the label it published and, when it did, the stand's position, both from the
/// same <see cref="Source"/>. Only ever built by <see cref="SayIntentionsArrivalGate"/>, which checks the flight matches
/// this OFP.</summary>
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position,
    SayIntentionsGateSource Source = SayIntentionsGateSource.FlightFile);
```

In `MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs`, add `using System.Diagnostics.CodeAnalysis;` as the first `using` line, then replace the whole `SayIntentionsArrivalGate` class and its summary (from `/// <summary>` "Turns a flight.json snapshot into the briefing's arrival-gate hint" to the class's closing brace) with:

```csharp
/// <summary>
/// Turns SayIntentions' state into the briefing's arrival-gate hint, or null. SayIntentions assigns an ARRIVAL gate
/// only, so the hint is offered only when SayIntentions' flight is THIS OFP's flight (<see cref="IsThisFlight"/>) and a
/// gate is set. The gate is the flight file's <c>assigned_gate</c>, with the file's position; when the file has none,
/// the parking service's answer with ITS position — the order MSFS Blind Assist's SayIntentions window uses
/// (<c>SayIntentionsService.GetAssignedStatusAsync</c>), so the two never disagree. A name is never paired with the
/// other source's position. No timestamp is consulted (a stale file for the same city pair is accepted and labelled
/// as SayIntentions' assignment).
/// </summary>
public static class SayIntentionsArrivalGate
{
    /// <summary>SayIntentions is flying this flight: its file exists and its origin and destination are the flight
    /// plan's. The briefing's one test before it takes anything from SayIntentions — the gate and the runways alike.</summary>
    public static bool IsThisFlight([NotNullWhen(true)] SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao) =>
        ctx != null && ctx.FlightJsonExists &&
        IcaoEquals(ctx.Origin, departureIcao) && IcaoEquals(ctx.Destination, arrivalIcao);

    /// <summary>The flight file's gate only.</summary>
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao) =>
        From(ctx, null, departureIcao, arrivalIcao);

    /// <summary>The flight file's gate, else the parking service's.</summary>
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, SayIntentionsParking? parking,
                                              string departureIcao, string arrivalIcao)
    {
        if (!IsThisFlight(ctx, departureIcao, arrivalIcao)) return null;
        if (!string.IsNullOrWhiteSpace(ctx.AssignedGate))
            return new SayIntentionsGateHint(ctx.AssignedGate.Trim(), ctx.AssignedGatePosition);
        if (parking == null || string.IsNullOrWhiteSpace(parking.Name)) return null;
        // (0, 0) is what two absent numbers look like once read as zero — the flight-file reader refuses it too.
        GeoPoint? position = parking.Latitude is double lat && parking.Longitude is double lon && (lat != 0 || lon != 0)
            ? new GeoPoint(lat, lon)
            : null;
        return new SayIntentionsGateHint(parking.Name.Trim(), position, SayIntentionsGateSource.ParkingService);
    }

    /// <summary>What <c>SayIntentionsService.GetAssignedStatusAsync</c> returned: the flight file and, when the file had
    /// no gate, the parking service.</summary>
    public static SayIntentionsGateHint? FromStatus(SayIntentionsStatusResult? status, string departureIcao, string arrivalIcao) =>
        status == null ? null : From(status.Context, status.Parking, departureIcao, arrivalIcao);

    private static bool IcaoEquals(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run the gate tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BriefingStandPickerTests"`
Expected: PASS — the new tests and every existing `SayIntentionsArrivalGate.From` test.

- [ ] **Step 5: Write the failing runway-choice tests**

Create `tests/MSFSBlindAssist.Tests/BriefingRunwayChoiceTests.cs`:

```csharp
// tests/MSFSBlindAssist.Tests/BriefingRunwayChoiceTests.cs
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

public class BriefingRunwayChoiceTests
{
    [Fact]
    public void SayIntentions_runway_wins_and_the_note_names_both()
    {
        // Live KMEM (2026-09-26): SayIntentions assigned 36L; SimBrief planned 18R, a runway it was not using.
        var r = BriefingRunwayChoice.Choose("18R", "36L", siIsThisFlight: true);
        Assert.Equal("36L", r.Runway);
        Assert.Equal("runway 36L is the runway SayIntentions assigned; the flight plan names 18R", r.Note);
    }

    [Fact]
    public void The_same_runway_keeps_the_flight_plan_spelling_and_says_SayIntentions_agrees()
    {
        var r = BriefingRunwayChoice.Choose("08L", "8L", siIsThisFlight: true);
        Assert.Equal("08L", r.Runway);
        Assert.Equal("SayIntentions has assigned this runway too", r.Note);
        Assert.Equal(BriefingRunwayChoice.AgreesNote, r.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void With_no_SayIntentions_runway_the_flight_plan_runway_stands_with_no_note(string? si)
    {
        var r = BriefingRunwayChoice.Choose("18R", si, siIsThisFlight: true);
        Assert.Equal("18R", r.Runway);
        Assert.Null(r.Note);
    }

    [Fact]
    public void Another_flight_s_runway_is_ignored()
    {
        var r = BriefingRunwayChoice.Choose("18R", "36L", siIsThisFlight: false);
        Assert.Equal("18R", r.Runway);
        Assert.Null(r.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void With_no_flight_plan_runway_SayIntentions_supplies_it(string? plan)
    {
        var r = BriefingRunwayChoice.Choose(plan, "36L", siIsThisFlight: true);
        Assert.Equal("36L", r.Runway);
        Assert.Equal("runway 36L is the runway SayIntentions assigned; the flight plan names no runway", r.Note);
    }

    [Fact]
    public void Nothing_anywhere_is_an_empty_runway_with_no_note()
    {
        var r = BriefingRunwayChoice.Choose(null, null, siIsThisFlight: false);
        Assert.Equal("", r.Runway);
        Assert.Null(r.Note);
    }
}
```

- [ ] **Step 6: Run them to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BriefingRunwayChoiceTests"`
Expected: build FAILS — `BriefingRunwayChoice` does not exist.

- [ ] **Step 7: Implement the runway choice**

Create `MSFSBlindAssist/Navigation/Briefing/BriefingRunwayChoice.cs`:

```csharp
// MSFSBlindAssist/Navigation/Briefing/BriefingRunwayChoice.cs
namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>The runway one briefing leg is planned to, and the note saying where it came from (null: the flight plan's
/// runway, with SayIntentions not consulted).</summary>
public readonly record struct BriefingRunway(string Runway, string? Note);

/// <summary>
/// Which runway a taxi leg is briefed to. When SayIntentions is flying this flight and has assigned a runway
/// (<c>current_flight.flight_plan_departing_runway</c> / <c>flight_plan_arriving_runway</c>, SAPI's "assigned departure /
/// arrival runway"), that runway wins — it is the one its controllers will clear the pilot to — and the leg says where
/// it came from. Live KMEM→KATL (2026-09-26): SayIntentions assigned 36L while SimBrief planned 18R, a runway
/// SayIntentions was not using, and the briefing routed the taxi-out to 18R. Otherwise the flight plan's runway
/// (SimBrief's <c>plan_rwy</c>, or the pilot's own EFB pick) stands, as before.
/// </summary>
public static class BriefingRunwayChoice
{
    public const string AgreesNote = "SayIntentions has assigned this runway too";

    public static BriefingRunway Choose(string? planRunway, string? siRunway, bool siIsThisFlight)
    {
        string plan = planRunway?.Trim() ?? "";
        string si = siRunway?.Trim() ?? "";
        if (!siIsThisFlight || si.Length == 0) return new BriefingRunway(plan, null);
        if (plan.Length == 0)
            return new BriefingRunway(si, $"runway {si} is the runway SayIntentions assigned; the flight plan names no runway");
        return TaxiBriefingPlanner.RunwayIdsMatch(plan, si)
            ? new BriefingRunway(plan, AgreesNote)
            : new BriefingRunway(si, $"runway {si} is the runway SayIntentions assigned; the flight plan names {plan}");
    }
}
```

- [ ] **Step 8: Run them to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BriefingRunwayChoiceTests"`
Expected: PASS (9 tests incl. theory rows).

- [ ] **Step 9: Write the failing parking-log tests**

Create `tests/MSFSBlindAssist.Tests/SayIntentionsParkingLogTests.cs`:

```csharp
// tests/MSFSBlindAssist.Tests/SayIntentionsParkingLogTests.cs
using System.Globalization;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsParkingLogTests
{
    [Fact]
    public void The_parking_service_answer_is_described_by_its_name_and_position()
        => Assert.Equal("name='B 12' lat=33.6407 lon=-84.4277 heading=180",
            SayIntentionsService.DescribeParking(new SayIntentionsParking { Name = "B 12", Latitude = 33.6407, Longitude = -84.4277, Heading = 180 }));

    [Fact]
    public void A_missing_number_reads_as_a_dash()
        => Assert.Equal("name='B 12' lat=- lon=- heading=-",
            SayIntentionsService.DescribeParking(new SayIntentionsParking { Name = "B 12" }));

    [Fact]
    public void Numbers_are_invariant_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("name='B 12' lat=33.6407 lon=-84.4277 heading=180.5",
                SayIntentionsService.DescribeParking(new SayIntentionsParking { Name = "B 12", Latitude = 33.6407, Longitude = -84.4277, Heading = 180.5 }));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
```

- [ ] **Step 10: Run them to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SayIntentionsParkingLogTests"`
Expected: build FAILS — `SayIntentionsService.DescribeParking` does not exist.

- [ ] **Step 11: Log what the parking service returned**

In `MSFSBlindAssist/Services/SayIntentions/SayIntentionsService.cs`, add `using System.Globalization;` to the using block. In `FetchParkingAsync`, replace:

```csharp
                    if (parking == null || string.IsNullOrWhiteSpace(parking.Name))
                    {
                        parking = null;
                        error = "No SayIntentions parking assignment found for the active flight.";
                    }
```

with:

```csharp
                    if (parking == null || string.IsNullOrWhiteSpace(parking.Name))
                    {
                        parking = null;
                        error = "No SayIntentions parking assignment found for the active flight.";
                    }
                    else
                    {
                        _log.Debug($"getParking: {DescribeParking(parking)}");
                    }
```

and add these two members directly after the `FetchParkingAsync` method:

```csharp
    /// <summary>What the parking service returned, for sayintentions.log — its name and position, nothing personal.
    /// Before, only the request was logged, so a briefing that missed the gate (live KMEM→KATL, 2026-09-26) could not
    /// be traced to what the service had said.</summary>
    internal static string DescribeParking(SayIntentionsParking parking) =>
        $"name='{parking.Name}' lat={LogNumber(parking.Latitude)} lon={LogNumber(parking.Longitude)} heading={LogNumber(parking.Heading)}";

    private static string LogNumber(double? value) =>
        value is double v ? v.ToString("0.######", CultureInfo.InvariantCulture) : "-";
```

- [ ] **Step 12: Run them to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SayIntentionsParkingLogTests"`
Expected: PASS (3 tests).

- [ ] **Step 13: Build and run the whole suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → Expected: `Build succeeded`, 0 errors.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → Expected: all tests pass.

- [ ] **Step 14: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs MSFSBlindAssist/Navigation/Briefing/BriefingRunwayChoice.cs MSFSBlindAssist/Services/SayIntentions/SayIntentionsService.cs tests/MSFSBlindAssist.Tests/BriefingStandPickerTests.cs tests/MSFSBlindAssist.Tests/BriefingRunwayChoiceTests.cs tests/MSFSBlindAssist.Tests/SayIntentionsParkingLogTests.cs
git commit -m "feat(briefing): SayIntentions' parking-service gate and assigned runways as briefing inputs" -m "The arrival-gate hint falls back to SayIntentions' getParking answer, with its own position, when flight.json has no gate yet; BriefingRunwayChoice prefers SayIntentions' assigned runway for this flight and says where it came from; what getParking returned is now logged." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Turn directions (`BriefingTurns`)

**Files:**
- Create: `MSFSBlindAssist/Navigation/Briefing/BriefingTurns.cs`
- Create: `tests/MSFSBlindAssist.Tests/BriefingTurnsTests.cs`

**Interfaces:**
- Produces: `public static class BriefingTurns` with `public const double StretchMetres = 60.0`, `public static string Words(double signedDegrees)`, `public static IReadOnlyList<string?> TaxiwayTurns(IReadOnlyList<TaxiRouteSegment>? segments)`, `public static string? StandTurn(IReadOnlyList<TaxiRouteSegment>? segments)`.
- Consumes: `TaxiRouteSegment` (existing: `TaxiwayName`, `DistanceMeters`, `BearingDegrees` — the direction of travel; TaxiGraph stores each edge both ways with the reversed bearing).

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/BriefingTurnsTests.cs`:

```csharp
// tests/MSFSBlindAssist.Tests/BriefingTurnsTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

public class BriefingTurnsTests
{
    /// <summary>A route from <paramref name="start"/> through each leg's end point; the leg's name is the taxiway of the
    /// segment that ends there ("" = unnamed). Metres east/north of the fixture base.</summary>
    private static List<TaxiRouteSegment> Route((double E, double N) start, params (string Name, double E, double N)[] legs)
    {
        var segments = new List<TaxiRouteSegment>();
        var from = new TaxiNode { NodeId = 0, Latitude = Lat(start.N), Longitude = Lon(start.E) };
        for (int i = 0; i < legs.Length; i++)
        {
            var to = new TaxiNode { NodeId = i + 1, Latitude = Lat(legs[i].N), Longitude = Lon(legs[i].E) };
            segments.Add(new TaxiRouteSegment
            {
                FromNode = from, ToNode = to, TaxiwayName = legs[i].Name,
                DistanceMeters = TaxiGraph.FastDistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude),
                BearingDegrees = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, to.Latitude, to.Longitude),
            });
            from = to;
        }
        return segments;
    }

    [Fact]
    public void A_right_angle_left_onto_the_next_taxiway()
        => Assert.Equal(new string?[] { null, "left" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", -300, 300))));

    [Fact]
    public void Small_bends_that_add_up_to_a_right_angle_are_one_right_turn()
    {
        // Navdata splits real turns into small bends: here 30° + 30° + 30° over 45 m. The junction where the name changes
        // bends only 30° ("slight right" by itself); the stretch sums all three.
        var route = Route((0, 0), ("A", 0, 300), ("B", 7.5, 312.99), ("B", 20.49, 320.49), ("B", 35.49, 320.49), ("B", 235.49, 320.49));
        Assert.Equal(new string?[] { null, "right" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void A_gentle_bend_is_slight()
        => Assert.Equal(new string?[] { null, "slight right" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", 150, 559.81))));

    [Fact]
    public void Two_turns_close_together_do_not_blend()
    {
        // B is 40 m long: each change reaches only halfway into it, so right-then-left is not read as straight ahead.
        var route = Route((0, 0), ("A", 0, 300), ("B", 40, 300), ("C", 40, 600));
        Assert.Equal(new string?[] { null, "right", "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void A_straight_continuation_says_straight_ahead()
        => Assert.Equal(new string?[] { null, "straight ahead" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", 0, 600))));

    [Fact]
    public void An_unnamed_connector_between_two_taxiways_is_part_of_the_turn()
        => Assert.Equal(new string?[] { null, "right" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("", 30, 300), ("B", 330, 300))));

    [Fact]
    public void A_turn_back_on_itself_is_sharp()
        => Assert.Equal(new string?[] { null, "sharp right" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", 150, 40.19))));

    [Fact]
    public void The_first_taxiway_never_has_a_turn()
        => Assert.Equal(new string?[] { null },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("", 0, 50), ("A", 300, 50))));

    [Fact]
    public void A_sub_metre_segment_is_skipped()
    {
        // A 0.3 m stub pointing east between north-bound A and west-bound B is a point, not a direction.
        var route = Route((0, 0), ("A", 0, 300), ("B", 0.3, 300), ("B", -300, 300));
        Assert.Equal(new string?[] { null, "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void Turns_align_with_the_taxiway_list_on_a_route_that_returns_to_a_taxiway()
    {
        var route = Route((0, 0), ("A", 0, 300), ("B", 300, 300), ("A", 300, 600));
        Assert.Equal(new[] { "A", "B", "A" }, RouteTaxiwaySequence.DistinctConsecutive(route));
        Assert.Equal(new string?[] { null, "right", "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void An_unnamed_gap_inside_one_taxiway_does_not_split_it()
    {
        var route = Route((0, 0), ("A", 0, 300), ("", 0, 310), ("A", 0, 600), ("B", -300, 600));
        Assert.Equal(new[] { "A", "B" }, RouteTaxiwaySequence.DistinctConsecutive(route));
        Assert.Equal(new string?[] { null, "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void The_turn_into_the_stand_is_measured_from_the_last_taxiway()
    {
        Assert.Equal("right", BriefingTurns.StandTurn(Route((300, 0), ("A", 0, 0), ("", 0, 150))));     // west, then north
        Assert.Equal("left", BriefingTurns.StandTurn(Route((0, 0), ("A", 300, 0), ("", 300, 150))));    // east, then north
    }

    [Fact]
    public void No_stand_turn_when_the_route_ends_on_a_taxiway()
        => Assert.Null(BriefingTurns.StandTurn(Route((0, 0), ("A", 0, 300), ("B", -300, 300))));

    [Fact]
    public void A_straight_run_into_the_stand_gives_no_stand_turn()
        => Assert.Null(BriefingTurns.StandTurn(Route((0, 0), ("A", 0, 300), ("", 0, 400))));

    [Fact]
    public void No_route_means_no_turns()
    {
        Assert.Empty(BriefingTurns.TaxiwayTurns(null));
        Assert.Empty(BriefingTurns.TaxiwayTurns(new List<TaxiRouteSegment>()));
        Assert.Empty(BriefingTurns.TaxiwayTurns(Route((0, 0), ("", 0, 300))));
        Assert.Null(BriefingTurns.StandTurn(null));
        Assert.Null(BriefingTurns.StandTurn(Route((0, 0), ("", 0, 300))));
    }

    [Theory]
    [InlineData(0, "straight ahead")]
    [InlineData(19.9, "straight ahead")]
    [InlineData(-19.9, "straight ahead")]
    [InlineData(20, "slight right")]
    [InlineData(-59.9, "slight left")]
    [InlineData(60, "right")]
    [InlineData(-60, "left")]
    [InlineData(119.9, "right")]
    [InlineData(120, "sharp right")]
    [InlineData(-179, "sharp left")]
    public void Words_follow_live_guidance_s_lines(double degrees, string words)
        => Assert.Equal(words, BriefingTurns.Words(degrees));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BriefingTurnsTests"`
Expected: build FAILS — `BriefingTurns` does not exist.

- [ ] **Step 3: Implement `BriefingTurns`**

Create `MSFSBlindAssist/Navigation/Briefing/BriefingTurns.cs`:

```csharp
// MSFSBlindAssist/Navigation/Briefing/BriefingTurns.cs
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which way a briefed route turns where it changes taxiway, and where it turns into the stand, in a controller's
/// plain words ("left", "slight right", "sharp left", "straight ahead").
///
/// <para>A turn is measured over a STRETCH of route around the change, never at one junction: navdata splits a real 90°
/// turn into several small bends (the reason live guidance's "Straighten." cue cannot trust one junction either), so the
/// sum of the bearing changes over the stretch is the turn. The stretch reaches <see cref="StretchMetres"/> into each
/// taxiway but never past its middle — the other half belongs to the neighbouring change — and takes in any unnamed
/// connector between the two.</para>
///
/// <para>Runs are exactly <see cref="RouteTaxiwaySequence.DistinctConsecutive"/>'s groups, so the turns line up with the
/// taxiway names. The 20° and 60° lines are <see cref="TaxiRouter.GetTurnDirection"/>'s, so a direction always agrees
/// with what live taxi guidance calls out; the 120° line is TaxiRouter's documented split between a normal and a sharp
/// turn. Live guidance adds "sharp" and the angle from 60° up; a briefing keeps plain words.</para>
///
/// <para>No direction is given for joining the first taxiway: after pushback the aircraft's heading is not known, and on
/// the taxi-in the landing exit's side is already briefed.</para>
/// </summary>
public static class BriefingTurns
{
    /// <summary>How far into each taxiway a change's stretch reaches, at most.</summary>
    public const double StretchMetres = 60.0;

    /// <summary>A segment shorter than this is a point with no direction (GuidanceGeometry's rule).</summary>
    private const double DegenerateMetres = 1.0;

    private const double StraightBelowDeg = 20.0;
    private const double SlightBelowDeg = 60.0;
    private const double SharpFromDeg = 120.0;

    /// <summary>The words for a signed turn, right positive.</summary>
    public static string Words(double signedDegrees)
    {
        double magnitude = Math.Abs(signedDegrees);
        if (magnitude < StraightBelowDeg) return "straight ahead";
        string side = signedDegrees < 0 ? "left" : "right";
        if (magnitude < SlightBelowDeg) return $"slight {side}";
        return magnitude < SharpFromDeg ? side : $"sharp {side}";
    }

    /// <summary>One entry per name of <see cref="RouteTaxiwaySequence.DistinctConsecutive"/>, in order: null for the first
    /// taxiway, then the turn onto each later one — null where it cannot be measured.</summary>
    public static IReadOnlyList<string?> TaxiwayTurns(IReadOnlyList<TaxiRouteSegment>? segments)
    {
        var turns = new List<string?>();
        if (segments == null) return turns;
        var runs = Runs(segments);
        for (int i = 0; i < runs.Count; i++)
        {
            if (i == 0)
            {
                turns.Add(null);
                continue;
            }
            double back = Math.Min(StretchMetres, runs[i - 1].Length / 2.0);
            double ahead = Math.Min(StretchMetres, runs[i].Length / 2.0);
            turns.Add(TurnOver(segments, runs[i - 1].Last, back, runs[i].First, ahead) is double d ? Words(d) : null);
        }
        return turns;
    }

    /// <summary>The turn from the last named taxiway into the unnamed segments that end the route (the stand lead-in);
    /// null when the route ends on a named segment, when there is nothing to measure, or when it runs straight in.</summary>
    public static string? StandTurn(IReadOnlyList<TaxiRouteSegment>? segments)
    {
        if (segments == null) return null;
        var runs = Runs(segments);
        if (runs.Count == 0) return null;
        var last = runs[^1];
        int tailFirst = last.Last + 1;
        if (tailFirst >= segments.Count) return null;
        double back = Math.Min(StretchMetres, last.Length / 2.0);
        double ahead = Math.Min(StretchMetres, Length(segments, tailFirst, segments.Count - 1));
        if (TurnOver(segments, last.Last, back, tailFirst, ahead) is not double d) return null;
        return Math.Abs(d) < StraightBelowDeg ? null : Words(d);
    }

    /// <summary>One taxiway run: its first and last NAMED segment (unnamed segments between two named segments of the
    /// same name belong to it) and its length from the first to the last.</summary>
    private readonly record struct Run(int First, int Last, double Length);

    private static List<Run> Runs(IReadOnlyList<TaxiRouteSegment> segments)
    {
        var runs = new List<Run>();
        string? name = null;
        int first = -1, last = -1;
        for (int i = 0; i < segments.Count; i++)
        {
            string current = segments[i].TaxiwayName;
            if (string.IsNullOrEmpty(current)) continue;
            if (name != null && current.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                last = i;
                continue;
            }
            if (name != null) runs.Add(new Run(first, last, Length(segments, first, last)));
            name = current;
            first = last = i;
        }
        if (name != null) runs.Add(new Run(first, last, Length(segments, first, last)));
        return runs;
    }

    /// <summary>The signed sum of the bearing changes over the stretch from <paramref name="back"/> metres before the
    /// end of segment <paramref name="incomingLast"/> to <paramref name="ahead"/> metres after the start of segment
    /// <paramref name="outgoingFirst"/>; null when fewer than two segments with a direction lie in it.</summary>
    private static double? TurnOver(IReadOnlyList<TaxiRouteSegment> s, int incomingLast, double back, int outgoingFirst, double ahead)
    {
        int start = incomingLast;
        double walked = s[start].DistanceMeters;
        while (walked < back && start > 0) walked += s[--start].DistanceMeters;
        int end = outgoingFirst;
        walked = s[end].DistanceMeters;
        while (walked < ahead && end < s.Count - 1) walked += s[++end].DistanceMeters;

        double? previous = null;
        double sum = 0;
        bool compared = false;
        for (int i = start; i <= end; i++)
        {
            if (s[i].DistanceMeters < DegenerateMetres) continue;
            if (previous is double p)
            {
                sum += Normalize(s[i].BearingDegrees - p);
                compared = true;
            }
            previous = s[i].BearingDegrees;
        }
        return compared ? sum : null;
    }

    private static double Length(IReadOnlyList<TaxiRouteSegment> s, int first, int last)
    {
        double metres = 0;
        for (int i = first; i <= last; i++) metres += s[i].DistanceMeters;
        return metres;
    }

    /// <summary>A bearing change folded into (-180, 180], right positive.</summary>
    private static double Normalize(double degrees)
    {
        degrees %= 360.0;
        if (degrees > 180.0) degrees -= 360.0;
        else if (degrees <= -180.0) degrees += 360.0;
        return degrees;
    }
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BriefingTurnsTests"`
Expected: PASS (all facts and theory rows).

- [ ] **Step 5: Build and run the whole suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → Expected: `Build succeeded`, 0 errors.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/BriefingTurns.cs tests/MSFSBlindAssist.Tests/BriefingTurnsTests.cs
git commit -m "feat(briefing): measure the turn at each taxiway change and into the stand" -m "Summed over a stretch reaching up to 60 m into each taxiway but never past its middle, because navdata splits real turns into small bends; words on live guidance's 20/60 degree lines plus TaxiRouter's 120 degree sharp split." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Integration — models, planner, renderer, EFB and MainForm wiring

**Depends on:** Tasks 1–3 merged.

**Files:**
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs` (`TaxiBriefingRequest`, `TaxiLegBriefing`, `RouteBriefingDependencies`)
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs` (constants; `PlanTaxiOut`; `PlanTaxiIn`)
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs` (`RenderTaxiOut`, `RenderTaxiIn`, `JoinNames` → `RouteText`)
- Modify: `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.Async.cs` (`Summarise`)
- Modify: `MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs` (usings; `BuildTaxiRoutesBlockAsync` ~lines 923–945; new `ReadSayIntentionsAsync`)
- Modify: `MSFSBlindAssist/MainForm.Dialogs.cs:~628`
- Modify: `tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs`
- Modify: `tests/MSFSBlindAssist.Tests/TaxiBriefingRendererTests.cs`

**Interfaces:**
- Consumes: `BriefingTurns.TaxiwayTurns/StandTurn` (Task 3); `SayIntentionsGateSource`, `SayIntentionsArrivalGate.IsThisFlight/FromStatus` (Task 2); `BriefingRunwayChoice.Choose` → `BriefingRunway(Runway, Note)` (Task 2).
- Produces: `TaxiBriefingRequest(..., string? OriginRunwayNote = null, string? DestinationRunwayNote = null)`; `TaxiLegBriefing.TaxiwayTurns : IReadOnlyList<string?>`, `TaxiLegBriefing.StandTurn : string?`; `TaxiBriefingPlanner.ParkingServiceMaxAirportDistanceMetres`; `RouteBriefingDependencies.SayIntentions : Func<Task<SayIntentionsStatusResult>>`.

- [ ] **Step 1: Write the failing planner tests**

In `tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs`, add `using MSFSBlindAssist.Services.SayIntentions;` to the using block, and add inside the class (after the taxi-in tests, before any private helper methods):

```csharp

    // ── turn directions ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_taxi_out_gives_the_turn_onto_each_taxiway_after_the_first()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(Md11F, airline: "UPS"), Airport());
        Assert.Equal(new[] { "A", "E1" }, leg.Taxiways);
        Assert.Equal(new string?[] { null, "left" }, leg.TaxiwayTurns);   // west along A, then south down E1
        Assert.Null(leg.StandTurn);
    }

    [Fact]
    public void The_taxi_in_gives_the_turn_into_the_stand()
    {
        var toG1 = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("Terminal 1 Gate G1", null)), Airport());
        Assert.Equal(new[] { "A" }, toG1.Taxiways);
        Assert.Equal(new string?[] { null }, toG1.TaxiwayTurns);
        Assert.Equal("right", toG1.StandTurn);    // west along A, then north into G 1

        var toC1 = TaxiBriefingPlanner.PlanTaxiIn(Request(Md11F, airline: "UPS"), Airport());
        Assert.Equal("left", toC1.StandTurn);     // east along A, then north into C 1
    }

    // ── SayIntentions' parking-service gate ─────────────────────────────────────────────────

    [Fact]
    public void A_parking_service_gate_at_the_arrival_airport_is_briefed_as_SayIntentions_gate()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", new GeoPoint(Lat(250), Lon(300)), SayIntentionsGateSource.ParkingService);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());
        Assert.Equal("SayIntentions assigned gate G 1", leg.EndpointDescription);
    }

    [Fact]
    public void A_parking_service_gate_not_at_the_arrival_airport_is_not_briefed()
    {
        // SAPI does not say whether getParking means the arrival gate or the current parking; KMEM has a concourse B too.
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", new GeoPoint(Lat(500_000), Lon(300)), SayIntentionsGateSource.ParkingService);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.Contains("SayIntentions' parking service named Terminal 1 Gate G1, but its position is not at TEST; using a representative stand instead", leg.Notes);
    }

    [Fact]
    public void A_parking_service_gate_with_no_position_is_briefed_by_name()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", null, SayIntentionsGateSource.ParkingService);
        Assert.Equal("SayIntentions assigned gate G 1", TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport()).EndpointDescription);
    }

    [Fact]
    public void A_flight_file_gate_is_never_refused_for_its_position()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", new GeoPoint(Lat(500_000), Lon(300)));
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());
        Assert.Equal("SayIntentions assigned gate G 1", leg.EndpointDescription);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("parking service", StringComparison.Ordinal));
    }

    // ── runway notes ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_runway_note_leads_the_taxi_out_notes()
    {
        const string note = "runway 09 is the runway SayIntentions assigned; the flight plan names 27";
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738) with { OriginRunwayNote = note }, Airport());
        Assert.Null(leg.Unavailable);
        Assert.Equal(note, leg.Notes[0]);
    }

    [Fact]
    public void The_runway_note_leads_the_taxi_in_notes()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738) with { DestinationRunwayNote = BriefingRunwayChoice.AgreesNote }, Airport());
        Assert.Null(leg.Unavailable);
        Assert.Equal(BriefingRunwayChoice.AgreesNote, leg.Notes[0]);
    }

    [Fact]
    public void An_unavailable_leg_keeps_its_runway_note()
    {
        const string note = "runway 04 is the runway SayIntentions assigned; the flight plan names 09";
        var outLeg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "04") with { OriginRunwayNote = note }, Airport());
        var inLeg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "04") with { DestinationRunwayNote = note }, Airport());

        Assert.NotNull(outLeg.Unavailable);
        Assert.Contains(note, outLeg.Notes);
        Assert.NotNull(inLeg.Unavailable);
        Assert.Contains(note, inLeg.Notes);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TaxiBriefingPlannerTests"`
Expected: build FAILS — `TaxiwayTurns`, `StandTurn`, `OriginRunwayNote`, `DestinationRunwayNote` do not exist.

- [ ] **Step 3: Extend the models**

In `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs`, replace:

```csharp
public sealed record TaxiBriefingRequest(
    string OriginIcao, string OriginRunway, string DestinationIcao, string DestinationRunway,
    AircraftProfile Aircraft, string? AirlineIcao, OwnPosition? Own, SayIntentionsGateHint? ArrivalGate);
```

with:

```csharp
/// <param name="OriginRunwayNote">Where the departure runway came from (<see cref="BriefingRunwayChoice"/>); the leg's
/// first note when set.</param>
/// <param name="DestinationRunwayNote">Where the arrival runway came from; the leg's first note when set.</param>
public sealed record TaxiBriefingRequest(
    string OriginIcao, string OriginRunway, string DestinationIcao, string DestinationRunway,
    AircraftProfile Aircraft, string? AirlineIcao, OwnPosition? Own, SayIntentionsGateHint? ArrivalGate,
    string? OriginRunwayNote = null, string? DestinationRunwayNote = null);
```

In the same file, in `TaxiLegBriefing`, directly after the line `public IReadOnlyList<string> Taxiways { get; init; } = Array.Empty<string>();` insert:

```csharp
    /// <summary>The turn onto each of <see cref="Taxiways"/>, aligned with it (<see cref="BriefingTurns.TaxiwayTurns"/>):
    /// null for the first taxiway and wherever no turn could be measured.</summary>
    public IReadOnlyList<string?> TaxiwayTurns { get; init; } = Array.Empty<string?>();
    /// <summary>Taxi-in only: the turn from the last taxiway into the stand (<see cref="BriefingTurns.StandTurn"/>), or null.</summary>
    public string? StandTurn { get; init; }
```

In the same file, replace:

```csharp
/// <summary>What the EFB needs from MainForm to compute the taxi section: a provider GETTER (the instance is
/// swapped on a database switch), the gate source and the SayIntentions file reader. Null in tests.</summary>
public sealed record RouteBriefingDependencies(
    Func<IAirportDataProvider?> Provider,
    Func<GateDataSource?> GateSource,
    Func<Task<SayIntentionsFlightContext>> SayIntentions);
```

with:

```csharp
/// <summary>What the EFB needs from MainForm to compute the taxi section: a provider GETTER (the instance is
/// swapped on a database switch), the gate source and SayIntentions' status — the flight file, and its parking service
/// when the file has no gate (<c>SayIntentionsService.GetAssignedStatusAsync</c>, the SayIntentions window's own call).
/// Null in tests.</summary>
public sealed record RouteBriefingDependencies(
    Func<IAirportDataProvider?> Provider,
    Func<GateDataSource?> GateSource,
    Func<Task<SayIntentionsStatusResult>> SayIntentions);
```

- [ ] **Step 4: Thread notes, turns and the parking-service check through the planner**

In `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs`, after the constant `StandNodeMaxDistanceMetres` add:

```csharp
    /// <summary>A SayIntentions parking-service gate is briefed only when its position lies within this distance of the
    /// arrival airport's reference point — the same "at this airport" line as <see cref="OwnPositionMaxAirportDistanceMetres"/>.</summary>
    public const double ParkingServiceMaxAirportDistanceMetres = OwnPositionMaxAirportDistanceMetres;
```

In `PlanTaxiOut`, replace:

```csharp
        string icao = r.OriginIcao;
        var notes = new List<string>();
        if (g.Note != null) notes.Add(g.Note);
```

with:

```csharp
        string icao = r.OriginIcao;
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.OriginRunwayNote)) notes.Add(r.OriginRunwayNote);
        if (g.Note != null) notes.Add(g.Note);
```

and in its final `return new TaxiLegBriefing { … }`, replace:

```csharp
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments),
            DistanceMetres = route.TotalDistanceMeters, HoldShorts = holds,
```

with:

```csharp
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments),
            TaxiwayTurns = BriefingTurns.TaxiwayTurns(route.Segments),
            DistanceMetres = route.TotalDistanceMeters, HoldShorts = holds,
```

In `PlanTaxiIn`, replace:

```csharp
        string icao = r.DestinationIcao;
        var notes = new List<string>();
        if (g.Note != null) notes.Add(g.Note);
```

with:

```csharp
        string icao = r.DestinationIcao;
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.DestinationRunwayNote)) notes.Add(r.DestinationRunwayNote);
        if (g.Note != null) notes.Add(g.Note);
```

then replace:

```csharp
        var stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, r.ArrivalGate, s => StandNode(g.Graph, s) != null);
```

with:

```csharp
        // SAPI does not say whether its parking service means the arrival gate or the aircraft's current parking, and an
        // origin can share a concourse letter with the destination (KMEM and KATL both have a B): a parking-service gate
        // whose own position is not at this airport is not briefed as SayIntentions' (live KMEM→KATL, 2026-09-26).
        var arrivalGate = r.ArrivalGate;
        if (arrivalGate is { Source: SayIntentionsGateSource.ParkingService, Position: GeoPoint pin } && g.Airport != null &&
            TaxiGraph.FastDistanceMeters(pin.Latitude, pin.Longitude, g.Airport.Latitude, g.Airport.Longitude) > ParkingServiceMaxAirportDistanceMetres)
        {
            notes.Add($"SayIntentions' parking service named {arrivalGate.Label}, but its position is not at {icao}; using a representative stand instead");
            arrivalGate = null;
        }

        var stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, arrivalGate, s => StandNode(g.Graph, s) != null);
```

and in its final `return new TaxiLegBriefing { … }`, replace:

```csharp
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(way.Route.Segments),
            DistanceMetres = way.Route.TotalDistanceMeters, HoldShorts = way.Holds, Exit = choice, VacatingExits = vacating,
```

with:

```csharp
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(way.Route.Segments),
            TaxiwayTurns = BriefingTurns.TaxiwayTurns(way.Route.Segments),
            StandTurn = BriefingTurns.StandTurn(way.Route.Segments),
            DistanceMetres = way.Route.TotalDistanceMeters, HoldShorts = way.Holds, Exit = choice, VacatingExits = vacating,
```

- [ ] **Step 5: Run the planner tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TaxiBriefingPlannerTests"`
Expected: PASS — the new tests and every existing one.

- [ ] **Step 6: Write the failing renderer tests**

In `tests/MSFSBlindAssist.Tests/TaxiBriefingRendererTests.cs`, in `FullBriefing()`, replace:

```csharp
            Taxiways = new[] { "N", "M", "A", "B" }, DistanceMetres = 2400,
```

with:

```csharp
            Taxiways = new[] { "N", "M", "A", "B" }, TaxiwayTurns = new string?[] { null, "left", "right", "slight right" },
            DistanceMetres = 2400,
```

and replace:

```csharp
            Taxiways = new[] { "AA", "E", "C" }, DistanceMetres = 3100,
```

with:

```csharp
            Taxiways = new[] { "AA", "E", "C" }, TaxiwayTurns = new string?[] { null, "left", "right" }, StandTurn = "right",
            DistanceMetres = 3100,
```

In `Full_two_leg_block`'s expected text, replace:

```csharp
            "  Taxiways: N, M, A, B (2.4 km)\n" +
```

with:

```csharp
            "  Taxiways: N, left onto M, right onto A, slight right onto B (2.4 km)\n" +
```

and replace:

```csharp
            "  Taxiways from the exit: AA, E, C (3.1 km)\n" +
```

with:

```csharp
            "  Taxiways from the exit: AA, left onto E, right onto C, then right into the stand (3.1 km)\n" +
```

Then add inside the class:

```csharp

    [Fact]
    public void A_leg_without_turns_names_its_taxiways_alone()
    {
        var b = FullBriefing();
        var bare = b with { TaxiOut = new TaxiLegBriefing
        {
            Icao = "KMEM", Runway = "36L", Tier = BriefingTier.Navdata, EndpointDescription = "current position",
            Taxiways = new[] { "N", "M" }, DistanceMetres = 900,
        } };
        Assert.Contains("  Taxiways: N, M (900 m)\n", TaxiBriefingRenderer.Render(bare));
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
        Assert.Contains("  Taxiways: N, straight ahead onto M (900 m)\n", TaxiBriefingRenderer.Render(straight));
    }
```

- [ ] **Step 7: Run them to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TaxiBriefingRendererTests"`
Expected: FAIL — `Full_two_leg_block` and `A_straight_change_reads_straight_ahead_onto` (the renderer still prints bare names).

- [ ] **Step 8: Render the turns**

In `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs`, replace:

```csharp
            lines.Add($"  Taxiways: {JoinNames(leg.Taxiways)} ({FormatDistance(leg.DistanceMetres)})");
```

with:

```csharp
            lines.Add($"  Taxiways: {RouteText(leg)} ({FormatDistance(leg.DistanceMetres)})");
```

replace:

```csharp
            lines.Add($"  Taxiways from the exit: {JoinNames(leg.Taxiways)} ({FormatDistance(leg.DistanceMetres)})");
```

with:

```csharp
            lines.Add($"  Taxiways from the exit: {RouteText(leg)} ({FormatDistance(leg.DistanceMetres)})");
```

and replace:

```csharp
    private static string JoinNames(IReadOnlyList<string> names) => names.Count == 0 ? Unnamed : string.Join(", ", names);
```

with:

```csharp
    /// <summary>The taxiways in order with the turn onto each where one was measured ("N, left onto M, …") and, on the
    /// taxi-in, the turn into the stand ("…, then right into the stand"). A taxiway with no turn is named alone; a
    /// wholly unnamed route reads <see cref="Unnamed"/>.</summary>
    private static string RouteText(TaxiLegBriefing leg)
    {
        if (leg.Taxiways.Count == 0) return Unnamed;
        var parts = new List<string>(leg.Taxiways.Count + 1);
        for (int i = 0; i < leg.Taxiways.Count; i++)
        {
            string? turn = i < leg.TaxiwayTurns.Count ? leg.TaxiwayTurns[i] : null;
            parts.Add(turn == null ? leg.Taxiways[i] : $"{turn} onto {leg.Taxiways[i]}");
        }
        if (leg.StandTurn != null) parts.Add($"then {leg.StandTurn} into the stand");
        return string.Join(", ", parts);
    }
```

In `MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.Async.cs`, in `Summarise`, replace:

```csharp
            : $"{head} endpoint=\"{leg.EndpointDescription}\" taxiways=[{string.Join(",", leg.Taxiways)}] " +
```

with:

```csharp
            : $"{head} endpoint=\"{leg.EndpointDescription}\" taxiways=[{string.Join(",", leg.Taxiways)}] " +
              $"turns=[{string.Join(",", leg.TaxiwayTurns.Select(t => t ?? "-"))}] standTurn=\"{leg.StandTurn ?? "-"}\" " +
```

- [ ] **Step 9: Run the renderer tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TaxiBriefingRendererTests"`
Expected: PASS — every test, the OSM test's unchanged `"  Taxiways: A (640 m)\n"` included.

- [ ] **Step 10: Wire the EFB and MainForm**

In `MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs`, add `using MSFSBlindAssist.Services.SayIntentions;` after `using MSFSBlindAssist.Services;`. Replace the whole `BuildTaxiRoutesBlockAsync` method:

```csharp
    private async Task<string> BuildTaxiRoutesBlockAsync(FlightPlan plan)
    {
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
```

with:

```csharp
    private async Task<string> BuildTaxiRoutesBlockAsync(FlightPlan plan)
    {
        var aircraft = AircraftSizeClass.Resolve(plan.AircraftTypeIcao, plan.AircraftName, plan.AircraftMaxPassengers);
        // SayIntentions is read while the aircraft position is: a web call to its parking service can take seconds.
        var siTask = ReadSayIntentionsAsync();
        string outRunway = plan.DepartureRunway, inRunway = plan.ArrivalRunway;
        try
        {
            var provider = _briefingDependencies?.Provider();
            var gateSource = _briefingDependencies?.GateSource();
            var own = await ReadOwnPositionAsync();
            var si = await siTask;

            // SayIntentions' runways and gate count only for THIS flight; its runway wins over the flight plan's and the
            // leg says so (BriefingRunwayChoice).
            bool siThisFlight = SayIntentionsArrivalGate.IsThisFlight(si?.Context, plan.DepartureICAO, plan.ArrivalICAO);
            var outChoice = BriefingRunwayChoice.Choose(plan.DepartureRunway, si?.Context.DepartureRunway, siThisFlight);
            var inChoice = BriefingRunwayChoice.Choose(plan.ArrivalRunway, si?.Context.ArrivalRunway, siThisFlight);
            (outRunway, inRunway) = (outChoice.Runway, inChoice.Runway);
            var siGate = SayIntentionsArrivalGate.FromStatus(si, plan.DepartureICAO, plan.ArrivalICAO);

            var request = new TaxiBriefingRequest(plan.DepartureICAO, outRunway, plan.ArrivalICAO, inRunway,
                                                  aircraft, plan.AirlineIcao, own, siGate, outChoice.Note, inChoice.Note);
            var briefing = await TaxiBriefingPlanner.PlanAsync(request, provider, gateSource, TaxiBriefingPlanner.DefaultBudget);
            return TaxiBriefingRenderer.Render(briefing);
        }
        catch (Exception ex)
        {
            Log.Warn("taxi_briefing", $"taxi routes block failed: {ex}");
            return TaxiBriefingRenderer.Render(TaxiBriefing.Unavailable(aircraft, plan.DepartureICAO, outRunway,
                plan.ArrivalICAO, inRunway, $"taxi route could not be computed ({ex.Message})"));
        }
    }

    /// <summary>SayIntentions' status — the flight file and, when it has no gate, the parking service — or null when
    /// SayIntentions is not wired in or the read fails. A SayIntentions failure costs the briefing SayIntentions' data
    /// only, never the taxi routes.</summary>
    private async Task<SayIntentionsStatusResult?> ReadSayIntentionsAsync()
    {
        if (_briefingDependencies == null) return null;
        try
        {
            return await _briefingDependencies.SayIntentions();
        }
        catch (Exception ex)
        {
            Log.Warn("taxi_briefing", $"SayIntentions read failed; briefing without it: {ex.Message}");
            return null;
        }
    }
```

In `MSFSBlindAssist/MainForm.Dialogs.cs`, replace:

```csharp
                    () => sayIntentionsService.ReadFlightContextAsync()));
```

with:

```csharp
                    () => sayIntentionsService.GetAssignedStatusAsync()));
```

- [ ] **Step 11: Build and run the whole suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → Expected: `Build succeeded`, 0 errors.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → Expected: all tests pass.

- [ ] **Step 12: Commit**

```bash
git add MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs MSFSBlindAssist/Navigation/Briefing/TaxiBriefingRenderer.cs MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.Async.cs MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs MSFSBlindAssist/MainForm.Dialogs.cs tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs tests/MSFSBlindAssist.Tests/TaxiBriefingRendererTests.cs
git commit -m "feat(briefing): SayIntentions runways and parking-service gate, and turn directions, in the taxi routes" -m "The EFB reads SayIntentions through GetAssignedStatusAsync, plans each leg to SayIntentions' assigned runway when it is flying this flight and notes where the runway came from, refuses a parking-service gate not at the arrival airport, and the block gives the turn at each taxiway change and into the stand." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Docs and invariants

**Depends on:** nothing at run time (names are fixed by this plan); may run in parallel with Task 4.

**Files:**
- Modify: `docs/gemini.md` ("Taxi routes in the route briefing": after the "Taxiway widths are advisory only" bullet; the "Diagnostics:" paragraph)
- Modify: `docs/sayintentions.md` (new subsection immediately before `### Second capture: KBOS, on the ground, no flight plan`)
- Modify: `docs/superpowers/specs/2026-09-25-route-briefing-taxi-routes-design.md` (§5.9 and §5.10 headings)
- Modify: `CLAUDE.md` (end of `### Gemini AI (→ [gemini.md](docs/gemini.md))`, before `## Quick Reference`)

- [ ] **Step 1: docs/gemini.md — four bullets**

Insert after the bullet that begins `- **Taxiway widths are advisory only**` (and before `- Limits:`):

```markdown
- **Runways** (`BriefingRunwayChoice`): each leg is planned to the flight plan's runway (SimBrief's `plan_rwy`, or the pilot's own pick on the EFB's Departure/Arrival tab) unless SayIntentions is flying THIS flight (`SayIntentionsArrivalGate.IsThisFlight`: its flight file exists and its origin and destination are the flight plan's) and has assigned one (`current_flight.flight_plan_departing_runway` / `flight_plan_arriving_runway`, SAPI's "assigned departure / arrival runway"). Then SayIntentions' runway wins — its controllers will clear the pilot to it — and the leg's first note says where the runway came from: "runway 36L is the runway SayIntentions assigned; the flight plan names 18R", or "SayIntentions has assigned this runway too" when they agree (`RunwayIdsMatch`, so 8L = 08L). The prompt has the AI name a difference in its departure or arrival narrative as well. Live KMEM→KATL (2026-09-26): SayIntentions assigned 36L while SimBrief planned 18R, a runway SayIntentions was not using, and the briefing had routed the taxi-out to 18R. It is a snapshot: SayIntentions can change runways after the briefing is made.
- **SayIntentions' gate source**: the EFB reads SayIntentions through `SayIntentionsService.GetAssignedStatusAsync` — the call MSFS Blind Assist's SayIntentions window makes — so the gate is the flight file's `assigned_gate` with the file's position or, when the file has none yet, the SAPI parking service's answer (`getParking`) with ITS OWN position (`SayIntentionsGateSource.ParkingService`); a name is never paired with the other source's position. Live KMEM→KATL: the file's `assigned_gate` was empty while SayIntentions showed a concourse B arrival gate, and the briefing, which read only the file, used a representative stand. SAPI does not say whether `getParking` means the arrival gate or the current parking (and KMEM has a concourse B too), so a parking-service gate whose position is more than `TaxiBriefingPlanner.ParkingServiceMaxAirportDistanceMetres` (5 km) from the arrival airport's reference point is not briefed — the leg says "SayIntentions' parking service named …, but its position is not at …; using a representative stand instead". A parking-service gate with no position is used by name, as Taxi Assist's import uses it; a flight-file gate is never subject to that check. A SayIntentions failure costs the briefing SayIntentions' data only; the taxi legs still come.
- **Turn directions** (`BriefingTurns`): each taxiway after the first carries the turn onto it ("N, left onto M, right onto A, slight right onto B"), and the taxi-in ends with the turn into the stand ("then right into the stand"). A turn is the sum of the bearing changes over a stretch reaching up to 60 m (`StretchMetres`) into each taxiway but never past its middle, through any unnamed connector — never one junction's angle, because navdata splits real turns into small bends, and never past the middle, so two close turns do not blend. Words: under 20° "straight ahead", under 60° "slight", under 120° plain, then "sharp"; the 20° and 60° lines are `TaxiRouter.GetTurnDirection`'s, so the direction agrees with live guidance's callouts (which add "sharp" and the angle from 60° up). No direction is given for joining the first taxiway (the heading after pushback is unknown; on the taxi-in the exit's side is already given) or onto the departure runway (live guidance leaves that hop to the lineup tone).
- **The real-world question is asked, never shown**: `GeminiService.RealWorldTaxiQuestion` is an instruction to the AI. The prompt tells it to write only its answer and never the question — the old wording, "Substitute the airport, runway, stand or terminal and aircraft type from the data", read as "write the filled-in question out", and a live KMEM→KATL Gemini briefing did, under both headings — and both providers run `RouteBriefingText.RemoveEchoedTaxiQuestion` on the reply. It removes the question or either of its sentences (quoted, labelled, after the heading, or split over two lines) and deletes a line it empties; every other line comes back byte-identical.
```

Replace the paragraph that begins `Diagnostics: \`debug.log\`, category \`taxi_briefing\`` with:

```markdown
Diagnostics: `debug.log`, category `taxi_briefing` — one Info-level summary line per leg regardless of outcome (an ordinary "no exit"/"no stand" leg, and a caller cancellation, log at Info too, exactly like a fully computed route), carrying `turns=[…]` and `standTurn="…"` beside `taxiways=[…]`; Warn is reserved for a genuine timeout, an unexpected exception during computation, or a failed SayIntentions read (the briefing then goes on without SayIntentions). What SayIntentions' parking service returned is in `sayintentions.log` (`getParking: name='…' lat=… lon=… heading=…`).
```

- [ ] **Step 2: docs/sayintentions.md — the origin capture**

Insert immediately before the line `### Second capture: KBOS, on the ground, no flight plan`:

```markdown
### Third capture: KMEM at the origin — the arrival gate only through getParking

(2026-09-26, parked at KMEM before a KMEM→KATL flight.) `current_flight.assigned_gate`, `assigned_gate_lat` and
`assigned_gate_lon` were all empty strings while SayIntentions itself showed an arrival gate on concourse B, and
`getParking` answered with a named parking — as it has on every one of the 67 calls in the log, including every call
made at an origin before any gate reached the file. `flight_plan_departing_runway` was 36L (SimBrief's plan said 18R)
and `flight_plan_arriving_runway` 8L; `departure_wx.active_runways_departing` read "36L,36R,27". So at the origin the
file does carry both assigned runways and does not carry the arrival gate: anything that wants the gate before the
arrival must fall back to `getParking`, as `GetAssignedStatusAsync` does. The route briefing does since 2026-09-26
(docs/gemini.md, "Taxi routes in the route briefing"). What `getParking` returned is now logged at Debug
(`getParking: name='…' lat=… lon=… heading=…`); before, only the request was, and nothing could say which stand it had
named. SAPI's documentation does not say whether `getParking` is the arrival gate or the current parking, so the
briefing refuses a parking-service gate whose position is not at the arrival airport.

```

- [ ] **Step 3: The 2026-09-25 spec — point at the amendment**

In `docs/superpowers/specs/2026-09-25-route-briefing-taxi-routes-design.md`, directly under the heading line `### 5.9 EFB wiring (\`ElectronicFlightBagForm\`)` insert:

```markdown
> Amended 2026-09-26: SayIntentions is read through `GetAssignedStatusAsync` (the parking-service fallback) and supplies
> the runways when it is flying this flight — see
> [2026-09-26-route-briefing-echo-si-runways-turns-design.md](2026-09-26-route-briefing-echo-si-runways-turns-design.md).
```

and directly under the heading line `### 5.10 Prompt (\`GeminiService.GetRouteDescriptionPrompt\`) and Gemini truncation` insert:

```markdown
> Amended 2026-09-26: the real-world question is asked, never shown; the prompt gains turn directions and the
> SayIntentions-runway instruction — see
> [2026-09-26-route-briefing-echo-si-runways-turns-design.md](2026-09-26-route-briefing-echo-si-runways-turns-design.md).
```

- [ ] **Step 4: CLAUDE.md — two invariants**

At the end of the `### Gemini AI (→ [gemini.md](docs/gemini.md))` bullet list (immediately before the blank line that precedes `## Quick Reference`), add:

```markdown
- The owner's real-world taxi question (`GeminiService.RealWorldTaxiQuestion`) is an INSTRUCTION to the AI, never output: the prompt must never again tell the AI to "substitute" the data into it (a live KMEM→KATL Gemini briefing wrote the filled-in question out under both "Real-world practice" headings, 2026-09-26), and `RouteBriefingText.RemoveEchoedTaxiQuestion` must stay in BOTH providers' `DescribeRouteAsync`. It removes only the question (whole, either sentence, quoted, labelled, split over two lines) and a line it empties; its anchors are pinned against the constant, so rewording the question fails the tests until the anchors follow. → [gemini.md](docs/gemini.md)
- The route briefing takes SayIntentions' data only for THIS flight (`SayIntentionsArrivalGate.IsThisFlight`: flight file exists, origin and destination match the flight plan). Its assigned runways (`flight_plan_departing_runway` / `flight_plan_arriving_runway`) win over the flight plan's, and the leg always says so ("…the flight plan names 18R" / "SayIntentions has assigned this runway too"). Its gate comes through `GetAssignedStatusAsync` — the SayIntentions window's own call — so the file's `assigned_gate`, else the `getParking` answer, each with its OWN position; a parking-service gate whose position is more than 5 km from the arrival airport is never briefed as SayIntentions' (SAPI does not say what `getParking` means, and an origin can share a concourse letter with the destination). The computed routes' turns come from `BriefingTurns` — a stretch sum, never one junction's angle — with no direction into the first taxiway or onto the departure runway. → [gemini.md](docs/gemini.md)
```

- [ ] **Step 5: Check the docs build nothing and read well**

Run: `git diff --stat` → Expected: only the four doc files changed.
Proofread each inserted block once in the file for broken Markdown (unclosed backticks, list indentation).

- [ ] **Step 6: Commit**

```bash
git add docs/gemini.md docs/sayintentions.md CLAUDE.md
git add -f docs/superpowers/specs/2026-09-25-route-briefing-taxi-routes-design.md
git commit -m "docs(briefing): SayIntentions runways and parking-service gate, turn directions, asked-never-shown question" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Wave 3: Final verification

- [ ] Run `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`, 0 errors; confirm `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` LastWriteTime is now.
- [ ] Run `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all pass; note the count.
- [ ] Final code review of the whole branch diff against the spec (superpowers:requesting-code-review).
- [ ] Owner's in-sim check (one scenario, from the spec §6): this KMEM→KATL flight with SayIntentions running, press Describe Route; expect no question text, taxi-out to 36L with the note naming 18R, the taxi-in to SayIntentions' concourse B gate labelled as its assignment, 36L in the departure narrative, turn directions on both computed routes, and a `getParking:` line in `sayintentions.log`.
