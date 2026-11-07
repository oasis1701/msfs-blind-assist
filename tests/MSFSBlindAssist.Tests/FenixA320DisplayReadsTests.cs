using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which instrument camera view each Fenix A320 display read uses, MEASURED on the live aircraft
/// (2026-10-22, MSFS 2024, Fenix v3 CFM A320, 1920 x 1080) by writing each index and capturing the
/// frame — never read off cameras.cfg.
///
/// Fenix v3 restored the "Main Panel (Left)" camera v2 lacked, so the live index is the file
/// position again and v2's 7/8 are now 8/9. Instrument view 8 (index 7) frames the captain's PFD,
/// ND and standby whole. Instrument view 9 (index 8), the centre, frames the E/WD, the SD and the
/// standby whole but clips BOTH NDs. Instrument view 10 (index 9) frames the first officer's PFD
/// and ND whole.
/// </summary>
public class FenixA320DisplayReadsTests
{
    [Fact]
    public void FiveReads_OnePerDisplayKey()
    {
        var actions = FenixA320DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(5, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD", 9)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDFenix, "ND", 7)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperECAM, "E/WD", 8)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerECAM, "SD", 8)]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.StandbyFenix, "Standby instruments", 8)]
    public void EachRead_HasItsPromptNameAndView(
        HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(AiDisplayRead.TryGet(FenixA320DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
        Assert.Equal(view, read.InstrumentViewIndex);
    }

    [Fact]
    public void TheViewConstants_AreTheMeasuredIndices()
    {
        Assert.Equal(7, FenixA320DisplayReads.CaptainPanelView);
        Assert.Equal(8, FenixA320DisplayReads.CenterPanelView);
        Assert.Equal(9, FenixA320DisplayReads.FirstOfficerPanelView);
    }

    [Fact]
    public void OnlyThePfdAndNdLeaveTheCentreView_EachOnItsOwnSide()
    {
        // v3 lists the captain's view (index 7); Alt+N reads the captain's ND there (owner,
        // 2026-10-22), because the centre view clips both NDs on v3. Alt+P keeps the first
        // officer's PFD (2026-10-21): the two PFDs differ only in baro and side-specific FD/AP
        // annunciation.
        var elsewhere = FenixA320DisplayReads.All
            .Where(r => r.InstrumentViewIndex != FenixA320DisplayReads.CenterPanelView)
            .ToDictionary(r => r.Action, r => r.InstrumentViewIndex);

        Assert.Equal(2, elsewhere.Count);
        Assert.Equal(FenixA320DisplayReads.FirstOfficerPanelView, elsewhere[HotkeyAction.ReadDisplayPFD]);
        Assert.Equal(FenixA320DisplayReads.CaptainPanelView, elsewhere[HotkeyAction.ReadDisplayND]);
    }

    [Fact]
    public void TheNdPromptNamesTheRightHandDisplay()
    {
        // The captain's view frames the captain's PFD on the left and ND on the right. The shared
        // DisplayType.ND prompt says only "ONLY describe the Navigation Display", so the Fenix has
        // its own, naming the ND as the right-hand display: "left-hand" would send the AI to the PFD.
        string nd = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.NDFenix);

        Assert.Contains("right-hand", nd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("captain", nd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("primary flight display", nd, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("left-hand", nd, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("two navigation displays", nd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheStandbyPrompt_HandlesRoundGaugesAndADigitalIsis()
    {
        // Fenix ships both: this airframe has three round gauges (airspeed, altimeter with a
        // Kollsman baro window, attitude), other variants have a single digital ISIS. One prompt
        // has to identify which is fitted and report it, the way the PMDG 737's lower-DU prompt
        // names which of its two pages it found.
        string standby = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.StandbyFenix);

        Assert.Contains("round", standby, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ISIS", standby);
        Assert.Contains("baro", standby, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheStandbyPrompt_IsNotTheOneTheHorizonSim787Uses()
    {
        // DisplayType.ISIS is shared with the HS787, which has a real digital ISFD. The Fenix
        // needed its own rather than an edit in place, or the 787 would inherit wording about
        // round gauges it does not have.
        Assert.NotEqual(
            GeminiService.GetPromptForDisplay(GeminiService.DisplayType.ISIS),
            GeminiService.GetPromptForDisplay(GeminiService.DisplayType.StandbyFenix));

        Assert.True(AiDisplayRead.TryGet(HS787DisplayReads.All, HotkeyAction.ReadDisplayISIS, out var hs));
        Assert.Equal(GeminiService.DisplayType.ISIS, hs.DisplayType);
    }

    [Fact]
    public void AnUnrelatedAction_IsNotARead()
    {
        Assert.False(AiDisplayRead.TryGet(FenixA320DisplayReads.All, HotkeyAction.ReadFlaps, out _));
    }
}
