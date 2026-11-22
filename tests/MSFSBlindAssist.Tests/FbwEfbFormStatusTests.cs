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
    private const string InSimHint = "Make sure MSFS 2024 is running and a flight is loaded.";
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
    public void A_refusal_speaks_once_while_visible()
    {
        Assert.True(FbwEfbForm.ShouldAnnounceRefusal(refused: true, wasRefused: false, visible: true));
        Assert.False(FbwEfbForm.ShouldAnnounceRefusal(refused: true, wasRefused: true, visible: true));    // not repeated
        Assert.False(FbwEfbForm.ShouldAnnounceRefusal(refused: true, wasRefused: false, visible: false));  // hidden: the page carries it
        Assert.False(FbwEfbForm.ShouldAnnounceRefusal(refused: false, wasRefused: true, visible: true));   // recovered
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
