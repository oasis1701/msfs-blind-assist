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
        Assert.Contains("Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. " +
                        "Please include the expected taxiways, hold short points, and any specific restrictions.", prompt);
        Assert.DoesNotContain("Real-world practice", prompt);
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
        // The long-unnamed-stretch rule leaves some taxiways with no turn word; this line stops the AI inventing one.
        Assert.Contains("for a taxiway, give none", prompt);
    }

    [Fact]
    public void Prompt_asks_for_every_taxi_distance_in_the_block_s_unit()
    {
        // Live KMEM→KATL: "2.2 kilometers" and "6,025 feet" in one taxi section. The block now states one unit.
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("Give every distance in this section in the unit the block's \"Distance unit\" line names", prompt);
    }

    [Fact]
    public void Prompt_asks_for_a_SayIntentions_runway_difference_to_be_named()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("SayIntentions assigned a different runway from the flight plan", prompt);
        Assert.Contains("DEPARTURE AND SID or ARRIVAL AND STAR section", prompt);
    }

    [Fact]
    public void The_real_world_route_uses_the_block_s_runway()
    {
        // A real-world route to SimBrief's 18R beside a computed route to SayIntentions' 36L is the confusion the runway
        // choice removes.
        Assert.Contains("from that leg's lines of the TAXI ROUTES block", GeminiService.GetRouteDescriptionPrompt("x"));
    }

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
}
