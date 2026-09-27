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
        Assert.DoesNotContain("but only where it concerns a taxiway, runway or stand the block names", prompt);
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
                        "only when a web search in this briefing found and read that airport's current airport diagram or chart " +
                        "notes.", prompt);
    }

    [Fact]
    public void The_check_line_never_claims_an_agreement_or_a_difference_it_cannot_support()
        => Assert.Contains("When they agree, say so in a few words; when something differs, name what differs instead; when you " +
                           "do not know the airport well enough to check it, say so; never claim an agreement or a difference you " +
                           "cannot support.", Prompt());

    [Fact]
    public void The_check_line_is_left_out_only_when_the_block_gives_nothing_to_check()
        => Assert.Contains("Leave the check line out for a leg the block gives no route, exit or stand for.", Prompt());

    [Fact]
    public void Suggestions_are_a_labelled_paragraph_only_when_there_is_something_to_add()
        => Assert.Contains("The suggestions paragraph comes after the check line (or after the route paragraph when there is " +
                           "none), only when you have something to add that the scenery cannot provide, and otherwise is left " +
                           $"out; it begins \"{GeminiService.RouteSuggestionsOpening}\" and has at most three short sentences.",
                           Prompt());

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
        Assert.Contains("When you leave a point out of a check line, do not call that leg an agreement: say that not everything " +
                        "could be checked, without naming what.", prompt);
        Assert.DoesNotContain("Any taxiway you name must appear", prompt);
    }

    [Fact]
    public void The_search_sentences_are_exact()
    {
        Assert.Equal("Web search is off for this briefing, so every check line begins " +
                     "\"Real-world check, from memory rather than live charts:\".", GeminiService.RouteSearchOffSentence);
        Assert.Equal("Web search is on for this briefing: do any lookups before you start writing, and you may look up each " +
                     "airport's current airport diagram and chart notes; a check line says current charts only when that search " +
                     "found and read them.",
                     GeminiService.RouteSearchOnSentence);
    }

    [Fact]
    public void The_search_sentence_follows_the_check_line_opening_rule()
    {
        // The "Begin it with" rule and the search sentence must read as one unit: nothing about looking things up should
        // sit apart from the rule it qualifies.
        string off = GeminiService.GetRouteDescriptionPrompt("DATA", webSearch: false).Replace("\r\n", "\n");
        string on = GeminiService.GetRouteDescriptionPrompt("DATA", webSearch: true).Replace("\r\n", "\n");
        Assert.Contains("chart notes.\n   " + GeminiService.RouteSearchOffSentence, off);
        Assert.Contains("chart notes.\n   " + GeminiService.RouteSearchOnSentence, on);
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
        Assert.Contains("do not describe where the data came from beyond the wording this section asks for", prompt);
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

    [Fact]
    public void A_general_knowledge_route_is_never_called_the_scenery_route()
        // Owner fix, 2026-09-27: the "expected route on the pilot's scenery" phrase only fits a route the block
        // actually gives; a general-knowledge route (or a leg with no route at all) must never carry it.
        => Assert.Contains("The scenery phrase belongs only to a route the block gives: end a general-knowledge route by " +
                           "saying only that SayIntentions or ATC will give the actual taxi clearance, never that it is the " +
                           "expected route on the pilot's scenery, and give no scenery phrase for a leg with no route.", Prompt());
}
