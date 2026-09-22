using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which instrument camera view each iFly 737 MAX8 display read uses, measured on the live
/// aircraft (2026-09-18, MSFS 2024 1.8.16.0): instrument view 1 (index 0) frames the captain's
/// PFD, ND and engine indications large; instrument view 2 (index 1) frames the whole main panel
/// with the standby instrument dead centre.
///
/// The standby instrument takes the full-panel view although it is legible in both: in the
/// captain-panel view its glass ends about 3.6% from the right frame edge at 16:9, so a narrower
/// window would clip it, while in the full-panel view it depends on no frame edge.
/// </summary>
public class IFly737DisplayReadsTests
{
    [Fact]
    public void FourReads_OnePerDisplayKey()
    {
        var actions = IFly737DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(4, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.Contains(HotkeyAction.ReadDisplayPFD, actions);
        Assert.Contains(HotkeyAction.ReadDisplayND, actions);
        Assert.Contains(HotkeyAction.ReadDisplayUpperECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayISIS, actions);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFDiFly, "PFD", 0)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDiFly, "ND", 0)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICASiFly, "EICAS", 0)]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFDiFly, "ISFD", 1)]
    public void EachRead_HasItsPromptNameAndView(HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(AiDisplayRead.TryGet(IFly737DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
        Assert.Equal(view, read.InstrumentViewIndex);
    }

    [Fact]
    public void TheViewConstants_AreTheMeasuredIndices()
    {
        Assert.Equal(0, IFly737DisplayReads.CaptainPanelView);
        Assert.Equal(1, IFly737DisplayReads.FullPanelView);
    }

    [Fact]
    public void ThereIsNoLowerSystemDisplayRead()
    {
        // The MAX has four landscape display units and no lower system display, so Alt+S stays
        // unhandled and falls through to the base definition. Measured in the full-panel capture,
        // 2026-09-18. Do not "fix" this by adding a row.
        Assert.False(AiDisplayRead.TryGet(IFly737DisplayReads.All, HotkeyAction.ReadDisplayLowerECAM, out _));
    }

    [Fact]
    public void AnUnrelatedAction_IsNotARead()
    {
        Assert.False(AiDisplayRead.TryGet(IFly737DisplayReads.All, HotkeyAction.ReadFlaps, out _));
    }
}
