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
    public void FiveReads_OnePerDisplayKey()
    {
        var actions = Pmdg737DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(5, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.Contains(HotkeyAction.ReadDisplayPFD, actions);
        Assert.Contains(HotkeyAction.ReadDisplayND, actions);
        Assert.Contains(HotkeyAction.ReadDisplayUpperECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayLowerECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayISIS, actions);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD737, "PFD", 7)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND737, "ND", 7)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS737, "EICAS", 1)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerDU737, "Lower display", 1)]
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
    public void BothEngineDisplays_ShareTheOneCameraView()
    {
        // They are two halves of one frame, so Alt+E and Alt+S never move the camera between them.
        Assert.True(AiDisplayRead.TryGet(Pmdg737DisplayReads.All, HotkeyAction.ReadDisplayUpperECAM, out var upper));
        Assert.True(AiDisplayRead.TryGet(Pmdg737DisplayReads.All, HotkeyAction.ReadDisplayLowerECAM, out var lower));

        Assert.Equal(upper.InstrumentViewIndex, lower.InstrumentViewIndex);
        Assert.NotEqual(upper.DisplayType, lower.DisplayType);
    }

    [Fact]
    public void TheLowerDisplayPrompt_SeparatesItFromTheUpperOne()
    {
        // The two units share one frame, so each prompt excludes the other's numbers by name.
        string lower = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.LowerDU737);

        Assert.Contains("N2", lower);
        Assert.Contains("vibration", lower, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Upper Engine Display", lower);
        // The lower unit is selectable, so the prompt must say which of the two it found.
        Assert.Contains("Navigation display", lower);
    }

    [Fact]
    public void TheLowerDisplayPrompt_AsksForFuelFlow()
    {
        // Measured in the simulator 2026-09-20: the lower unit shows FF (2.17 / 2.16) alongside
        // N2, oil and vibration, and the upper unit shows its own FF at the same time. The first
        // draft of this prompt told the model that fuel flow "belongs to the Upper Engine Display,
        // not this one" and to skip it here, which would have dropped a value that is on screen.
        string lower = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.LowerDU737);

        Assert.Contains("fuel flow", lower, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("N1, EGT and fuel flow belong to the Upper Engine Display", lower);
    }

    [Fact]
    public void AnUnrelatedAction_IsNotARead()
    {
        Assert.False(AiDisplayRead.TryGet(Pmdg737DisplayReads.All, HotkeyAction.ReadFlaps, out _));
    }
}
