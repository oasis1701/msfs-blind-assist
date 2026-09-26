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
        Assert.Contains("Aim for 600 to 900 words", prompt);
        Assert.DoesNotContain("Aim for 300 to 500 words", prompt);
        Assert.EndsWith("FLIGHT DATA HERE", prompt);
    }

    [Fact]
    public void The_taxi_section_asks_for_the_typical_real_world_flow_only()
    {
        // Owner, 2026-09-26: the scenery-computed route is gone; the taxi section is the real-world flow alone.
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("from that leg's lines of the TAXI PLANNING block", prompt);
        Assert.Contains("typical real-world taxi flow", prompt);
        Assert.DoesNotContain("TAXI ROUTES", prompt);
        Assert.DoesNotContain("computed route", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Use ONLY the taxiway, exit and stand names given in the block", prompt);
        Assert.DoesNotContain("Real-world practice", prompt);
    }

    [Fact]
    public void The_arrival_leg_asks_for_the_usual_exit_and_side()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("which side the aircraft usually leaves the runway and the exit taxiway usually used", prompt);
    }

    [Fact]
    public void A_missing_stand_or_gate_is_presented_as_typical_not_assigned()
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt("x");
        Assert.Contains("When the block gives no stand or gate", prompt);
        Assert.Contains("say that it is typical, not assigned", prompt);
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
    public void Prompt_asks_for_every_taxi_distance_in_the_block_s_unit()
    {
        // Live KMEM→KATL: "2.2 kilometers" and "6,025 feet" in one taxi section. The block states one unit.
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
}
