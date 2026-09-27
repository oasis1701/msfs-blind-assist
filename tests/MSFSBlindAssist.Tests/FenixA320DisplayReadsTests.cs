using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which instrument camera view each Fenix A320 display read uses, MEASURED on the live aircraft
/// (2026-09-21, MSFS 2024 1.8.16.0, FenixA320 IAE WF) by writing each index and capturing the
/// frame — never read off cameras.cfg, which on this aircraft is wrong twice over.
///
/// Instrument view 8 (index 7) frames the captain's ND, the standby instruments, the E/WD, the SD
/// and the first officer's ND, with BOTH PFDs clipped to slivers at the frame edges. Instrument
/// view 9 (index 8) frames the first officer's ND and PFD large, with the ECAM clipped at the
/// left edge.
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
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD", 8)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDFenix, "ND", 7)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperECAM, "E/WD", 7)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerECAM, "SD", 7)]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.StandbyFenix, "Standby instruments", 7)]
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
        Assert.Equal(7, FenixA320DisplayReads.CenterPanelView);
        Assert.Equal(8, FenixA320DisplayReads.FirstOfficerPanelView);
    }

    [Fact]
    public void ThePfdIsTheOnlyReadThatLeavesTheCentreView()
    {
        // The captain's own main-panel camera is defined in cameras.cfg ("Main Panel (Left)") but
        // is NOT in the live instrument list, so no frame holds the captain's PFD. The first
        // officer's is the only one framed, and the two PFDs differ only in baro and side-specific
        // FD/AP annunciation.
        var elsewhere = FenixA320DisplayReads.All
            .Where(r => r.InstrumentViewIndex != FenixA320DisplayReads.CenterPanelView)
            .ToArray();

        Assert.Single(elsewhere);
        Assert.Equal(HotkeyAction.ReadDisplayPFD, elsewhere[0].Action);
    }

    [Fact]
    public void TheNdPromptNamesWhichOfTheTwoToRead()
    {
        // The centre view frames BOTH NDs. The shared DisplayType.ND prompt says only "ONLY
        // describe the Navigation Display", which is ambiguous with two of them side by side, so
        // the Fenix has its own prompt naming the captain's — the left-hand one, and the one whose
        // range and mode the pilot sets on EFIS1.
        string nd = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.NDFenix);

        Assert.Contains("two navigation displays", nd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left", nd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("captain", nd, StringComparison.OrdinalIgnoreCase);
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
