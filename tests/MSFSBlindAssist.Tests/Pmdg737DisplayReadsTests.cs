using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which instrument camera view each PMDG 737-800 display read uses, measured on the live
/// aircraft (2026-09-20, MSFS 2024 1.8.16.0): instrument view 8 (index 7) frames the captain's
/// PFD and ND large, and instrument view 2 (index 1) frames the ISFD, the upper engine display
/// and the lower system display.
///
/// The two camera titles are misleading and the indices must not be "corrected" from them: the
/// camera titled "PFD" also frames the ND, and the one titled "EICAS" also frames the ISFD. The
/// standby read deliberately does NOT use the captain-panel view, where the ISFD is clipped by
/// the right frame edge.
/// </summary>
public class Pmdg737DisplayReadsTests
{
    [Fact]
    public void FourReads_OnePerDisplayKey()
    {
        var actions = Pmdg737DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(4, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.Contains(HotkeyAction.ReadDisplayPFD, actions);
        Assert.Contains(HotkeyAction.ReadDisplayND, actions);
        Assert.Contains(HotkeyAction.ReadDisplayUpperECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayISIS, actions);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD737, "PFD", 7)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND737, "ND", 7)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS737, "EICAS", 1)]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFD737, "ISFD", 1)]
    public void EachRead_HasItsPromptNameAndView(HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(AiDisplayRead.TryGet(Pmdg737DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
        Assert.Equal(view, read.InstrumentViewIndex);
    }

    [Fact]
    public void TheViewConstants_AreTheMeasuredIndices()
    {
        Assert.Equal(7, Pmdg737DisplayReads.CaptainPanelView);
        Assert.Equal(1, Pmdg737DisplayReads.CenterPanelView);
    }

    [Fact]
    public void ThereIsNoLowerSystemDisplayRead()
    {
        // Alt+S stays out of scope, matching the PMDG 777. PMDG737Definition answers it with a
        // bare false rather than deferring to the base definition — a deliberate difference from
        // the iFly, and one the dispatch above the switch must not swallow.
        Assert.False(AiDisplayRead.TryGet(Pmdg737DisplayReads.All, HotkeyAction.ReadDisplayLowerECAM, out _));
    }

    [Fact]
    public void AnUnrelatedAction_IsNotARead()
    {
        Assert.False(AiDisplayRead.TryGet(Pmdg737DisplayReads.All, HotkeyAction.ReadFlaps, out _));
    }
}
