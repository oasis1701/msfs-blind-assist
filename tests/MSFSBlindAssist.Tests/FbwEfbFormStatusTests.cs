using System.Globalization;
using MSFSBlindAssist.Forms.FBWA380;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins what the shared tablet window (FbwEfbForm: flyPad, 757, PMDG, MD-11 and in-sim tablets) says
/// while it is not connected: F6 and the status line, the page's waiting line, and when the "keeps
/// refusing" reason is spoken. Until 2026-11-22 the status line said "the A380X is loaded" in every
/// window, and nothing said that another program held the sim tablet's view.
/// </summary>
public class FbwEfbFormStatusTests
{
    // The hint MainForm passes to the in-sim tablet's window, so these tests pin what that window says.
    private const string InSimHint = FbwEfbForm.SimTabletNotConnectedHint;
    private const string Refused = "The simulator keeps closing the connection; another program may be using it.";

    [Fact]
    public void The_general_sentence()
    {
        Assert.Equal("flyPad not connected. Make sure the aircraft is loaded and the tablet is powered on.",
            FbwEfbForm.NotConnectedText("flyPad", FbwEfbForm.DefaultNotConnectedHint, refused: false));
    }

    [Fact]
    public void The_in_sim_sentence()
    {
        Assert.Equal("In-sim tablet not connected. Make sure MSFS 2024 is running and a flight is loaded.",
            FbwEfbForm.NotConnectedText("In-sim tablet", InSimHint, refused: false));
    }

    [Fact]
    public void A_refusal_replaces_the_hint()
    {
        Assert.Equal("In-sim tablet not connected. " + Refused,
            FbwEfbForm.NotConnectedText("In-sim tablet", InSimHint, refused: true));
        Assert.Equal(Refused, FbwEfbForm.RefusedReason);
    }

    [Fact]
    public void No_sentence_names_the_A380X()
    {
        Assert.DoesNotContain("A380X", FbwEfbForm.NotConnectedText("EFB", FbwEfbForm.DefaultNotConnectedHint, false));
        Assert.DoesNotContain("A380X", FbwEfbForm.NotConnectedText("In-sim tablet", InSimHint, false));
        Assert.DoesNotContain("A380X", FbwEfbForm.NotConnectedText("tablet", FbwEfbForm.DefaultNotConnectedHint, true));
    }

    [Fact]
    public void The_waiting_line()
    {
        // The plain line is the page's own initial text (PageHtml: "Waiting for the {{NOUN}}…").
        Assert.Equal("Waiting for the In-sim tablet…", FbwEfbForm.WaitingLine("In-sim tablet", contentLoaded: false, refused: false));
        Assert.Equal("Waiting for the In-sim tablet. " + Refused, FbwEfbForm.WaitingLine("In-sim tablet", contentLoaded: false, refused: true));
        Assert.Null(FbwEfbForm.WaitingLine("In-sim tablet", contentLoaded: true, refused: false));
        Assert.Null(FbwEfbForm.WaitingLine("In-sim tablet", contentLoaded: true, refused: true));
    }

    [Fact]
    public void A_refusal_that_starts_while_open_is_spoken()
    {
        Assert.Equal(FbwEfbForm.RefusalCue.Speak,
            FbwEfbForm.RefusalCueFor(refused: true, announced: false, visible: true, wasVisible: true, contentLoaded: false, pageShowsReason: false));
        Assert.Equal(FbwEfbForm.RefusalCue.Speak,
            FbwEfbForm.RefusalCueFor(refused: true, announced: false, visible: true, wasVisible: true, contentLoaded: true, pageShowsReason: false));
    }

    [Fact]
    public void A_refusal_is_not_repeated_nor_spoken_while_hidden_or_after_recovery()
    {
        Assert.Equal(FbwEfbForm.RefusalCue.None,
            FbwEfbForm.RefusalCueFor(refused: true, announced: true, visible: true, wasVisible: true, contentLoaded: false, pageShowsReason: true));
        Assert.Equal(FbwEfbForm.RefusalCue.None,
            FbwEfbForm.RefusalCueFor(refused: true, announced: false, visible: false, wasVisible: false, contentLoaded: true, pageShowsReason: false));
        Assert.Equal(FbwEfbForm.RefusalCue.None,
            FbwEfbForm.RefusalCueFor(refused: false, announced: false, visible: true, wasVisible: true, contentLoaded: false, pageShowsReason: false));
    }

    [Fact]
    public void Opening_onto_a_refusal_before_content_loaded_leaves_it_to_the_page()
    {
        // The waiting line already says it, and the screen reader reads it on opening (CORE-7).
        Assert.Equal(FbwEfbForm.RefusalCue.PageSaysIt,
            FbwEfbForm.RefusalCueFor(refused: true, announced: false, visible: true, wasVisible: false, contentLoaded: false, pageShowsReason: true));
    }

    [Fact]
    public void Opening_onto_a_refusal_the_page_does_not_show_is_spoken()
    {
        // Review of PR 364: list mode (no page line), a failed push, or a refusal first seen on the tick
        // after the window opened: the screen reader read no reason, so the window speaks it.
        Assert.Equal(FbwEfbForm.RefusalCue.Speak,
            FbwEfbForm.RefusalCueFor(refused: true, announced: false, visible: true, wasVisible: false, contentLoaded: false, pageShowsReason: false));
    }

    [Fact]
    public void Opening_onto_a_refusal_after_content_loaded_is_spoken()
    {
        // Review of the branch: the page then shows the old tablet as if live, so only speech says why.
        Assert.Equal(FbwEfbForm.RefusalCue.Speak,
            FbwEfbForm.RefusalCueFor(refused: true, announced: false, visible: true, wasVisible: false, contentLoaded: true, pageShowsReason: false));
    }

    [Fact]
    public void The_sentences_under_a_comma_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("flyPad not connected. Make sure the aircraft is loaded and the tablet is powered on.",
                FbwEfbForm.NotConnectedText("flyPad", FbwEfbForm.DefaultNotConnectedHint, refused: false));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
