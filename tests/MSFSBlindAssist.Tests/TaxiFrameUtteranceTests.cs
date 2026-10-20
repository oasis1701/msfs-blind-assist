using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every interrupting line taxi guidance speaks in one position frame is spoken as ONE utterance
/// (TaxiGuidanceManager.ComposeFrameUtterance). Spoken one by one each cut the previous off, so
/// "In 30 metres, slight right." / "Slight right now." / "Slow for turn." left the pilot hearing
/// only "Slow for turn." (VirtualPilot 2026-09-25).
/// </summary>
public class TaxiFrameUtteranceTests
{
    [Fact]
    public void NothingSaid_IsNull() =>
        Assert.Null(TaxiGuidanceManager.ComposeFrameUtterance(new List<string>()));

    [Fact]
    public void OneLine_IsUnchanged() =>
        Assert.Equal("Taxiway B.", TaxiGuidanceManager.ComposeFrameUtterance(new[] { "Taxiway B." }));

    [Fact]
    public void AdvanceNoticeInTheSameFrameAsTheTurnNow_IsDropped() =>
        Assert.Equal("Slight right now. Slow for turn.",
            TaxiGuidanceManager.ComposeFrameUtterance(new[] { "In 30 metres, slight right.", "Slight right now.", "Slow for turn." }));

    [Fact]
    public void RolloutTurnAheadInTheSameFrameAsTurnNow_IsDropped() =>
        Assert.Equal("Turn left now, taxiway Y3.",
            TaxiGuidanceManager.ComposeFrameUtterance(new[] { "Left turn ahead, taxiway Y3.", "Turn left now, taxiway Y3." }));

    [Fact]
    public void RolloutTurnAheadWithoutATurnNow_IsKept() =>
        Assert.Equal("Left turn ahead, taxiway Y3.",
            TaxiGuidanceManager.ComposeFrameUtterance(new[] { "Left turn ahead, taxiway Y3." }));

    [Fact]
    public void AdvanceNoticeWithoutATurnNow_IsKept() =>
        Assert.Equal("In 30 metres, slight right. Slow for turn.",
            TaxiGuidanceManager.ComposeFrameUtterance(new[] { "In 30 metres, slight right.", "Slow for turn." }));

    [Fact]
    public void ContinueNotice_IsNotDroppedByATurnNow() =>
        // "In 40 metres, continue onto taxiway A." is about a different junction than a turn.
        Assert.Contains("In 40 metres, continue onto taxiway A.",
            TaxiGuidanceManager.ComposeFrameUtterance(new[] { "In 40 metres, continue onto taxiway A.", "Left now." }));

    [Fact]
    public void SafetyLines_GoFirst() =>
        Assert.Equal("Warning: approaching runway 27L at A3, off route. Slow for turn.",
            TaxiGuidanceManager.ComposeFrameUtterance(new[] { "Slow for turn.", "Warning: approaching runway 27L at A3, off route." }));

    [Fact]
    public void Duplicates_AreSpokenOnce() =>
        Assert.Equal("Taxiway B.", TaxiGuidanceManager.ComposeFrameUtterance(new[] { "Taxiway B.", "Taxiway B." }));

    [Fact]
    public void MissingFullStop_IsAdded() =>
        Assert.Equal("Continuing. Taxiway A11A. Crossing runway 16L at A11A.",
            TaxiGuidanceManager.ComposeFrameUtterance(new[] { "Continuing. Taxiway A11A. ", "Crossing runway 16L at A11A" }));
}
