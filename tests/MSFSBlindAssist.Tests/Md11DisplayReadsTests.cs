using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which instrument camera view each MD-11 display read uses, measured on the live aircraft
/// (2026-09-08): view 1 (index 0) frames the captain's PFD, ND and EAD; view 3 (index 2) the ND,
/// EAD and SD; view 4 (index 3) the forward pedestal with the standby instrument.
/// </summary>
public class Md11DisplayReadsTests
{
    [Fact]
    public void FiveReads_OnePerDisplayKey()
    {
        var actions = Md11DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(5, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.Contains(HotkeyAction.ReadDisplayPFD, actions);
        Assert.Contains(HotkeyAction.ReadDisplayND, actions);
        Assert.Contains(HotkeyAction.ReadDisplayUpperECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayLowerECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayISIS, actions);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFDMd11, "PFD", 0)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDMd11, "ND", 0)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EADMd11, "EAD", 0)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.SDMd11, "SD", 2)]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFDMd11, "Standby instrument", 3)]
    public void EachRead_HasItsPromptNameAndView(HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(Md11DisplayReads.TryGet(action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
        Assert.Equal(view, read.InstrumentViewIndex);
    }

    [Fact]
    public void TheViewConstants_AreTheMeasuredIndices()
    {
        Assert.Equal(0, Md11DisplayReads.CaptainPanelView);
        Assert.Equal(2, Md11DisplayReads.CenterPanelView);
        Assert.Equal(3, Md11DisplayReads.ForwardPedestalView);
    }

    [Fact]
    public void AnUnrelatedAction_IsNotARead()
    {
        Assert.False(Md11DisplayReads.TryGet(HotkeyAction.ReadFlaps, out _));
    }
}
