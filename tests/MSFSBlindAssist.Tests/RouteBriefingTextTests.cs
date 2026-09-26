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
                "Provide the step-by-step taxi route at [ICAO Code] from [Runway] to [Terminal/Gate] in a [Aircraft Type]. " +
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
