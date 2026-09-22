using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which instrument camera view each PMDG 777 display read uses, MEASURED on the live aircraft
/// (2026-09-22, MSFS 2024 1.8.16.0, PMDG 777F) by writing each index and capturing the frame.
///
/// Instrument view 8 (index 7) frames the captain's PFD and ND, both large, with the ISFD at the
/// right-hand edge. Instrument view 2 (index 1) frames the whole forward panel: the ISFD well
/// inside the frame, the upper EICAS, the LOWER display, both CDUs and the first officer's
/// displays. The standby read uses view 2 rather than view 8 for exactly that reason — at the
/// edge of view 8 it depends on the window's aspect ratio, in view 2 it does not.
/// </summary>
public class Pmdg777DisplayReadsTests
{
    [Fact]
    public void FiveReads_OnePerDisplayKey()
    {
        var actions = Pmdg777DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(5, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD777, "PFD", 7)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND777, "ND", 7)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS, "EICAS", 1)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerDisplay777, "Lower display", 1)]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFD, "ISFD", 1)]
    public void EachRead_HasItsPromptNameAndView(
        HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(AiDisplayRead.TryGet(Pmdg777DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
        Assert.Equal(view, read.InstrumentViewIndex);
    }

    [Fact]
    public void TheViewConstants_AreTheMeasuredIndices()
    {
        Assert.Equal(7, Pmdg777DisplayReads.CaptainPanelView);
        Assert.Equal(1, Pmdg777DisplayReads.ForwardPanelView);
    }

    [Fact]
    public void TheStandbyDoesNotUseTheCaptainPanelView()
    {
        // It IS in that frame, but hard against the right edge, so a narrower window would clip
        // it. The forward-panel view holds it well inside. Same reasoning as the PMDG 737 and the
        // iFly, both of which moved their standby read off the captain-panel view for this.
        Assert.True(AiDisplayRead.TryGet(Pmdg777DisplayReads.All, HotkeyAction.ReadDisplayISIS, out var isfd));

        Assert.Equal(Pmdg777DisplayReads.ForwardPanelView, isfd.InstrumentViewIndex);
    }

    [Fact]
    public void ThreeReadsShareTheForwardPanelView()
    {
        // The EICAS, the lower display and the ISFD are all in that one frame, so their prompts
        // have to exclude each other by name.
        var shared = Pmdg777DisplayReads.All
            .Where(r => r.InstrumentViewIndex == Pmdg777DisplayReads.ForwardPanelView)
            .Select(r => r.Action)
            .ToArray();

        Assert.Equal(3, shared.Length);
        Assert.Contains(HotkeyAction.ReadDisplayUpperECAM, shared);
        Assert.Contains(HotkeyAction.ReadDisplayLowerECAM, shared);
        Assert.Contains(HotkeyAction.ReadDisplayISIS, shared);
    }

    [Fact]
    public void TheLowerDisplayPrompt_NamesWhichPageItFound()
    {
        // The 777's lower display is selectable: secondary engine indications, or one of the
        // synoptics (STATUS, ELEC, HYD, FUEL, AIR, DOOR, GEAR, FCTL), or a navigation display.
        // The prompt identifies which is present before reporting it, the way the 737's lower-DU
        // and the MD-11's SD prompts do.
        string lower = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.LowerDisplay777);

        Assert.Contains("secondary engine", lower, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("synoptic", lower, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("N2", lower);
        Assert.Contains("vibration", lower, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheLowerDisplayPrompt_DoesNotExcludeTheValuesItAsksFor()
    {
        // The 737's lower-DU prompt shipped listing fuel flow among the things to IGNORE while
        // asking for it four lines later, which lets a model legitimately drop it. The upper
        // EICAS is in this frame too, so this prompt excludes it by name — but must not name any
        // value the lower display actually carries.
        string lower = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.LowerDisplay777);

        var ignoreClause = lower.Split('\n').Single(line => line.Contains("Ignore", StringComparison.Ordinal));
        Assert.DoesNotContain("N2", ignoreClause);
        Assert.DoesNotContain("oil", ignoreClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vibration", ignoreClause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnrelatedAction_IsNotARead()
    {
        Assert.False(AiDisplayRead.TryGet(Pmdg777DisplayReads.All, HotkeyAction.ReadFlaps, out _));
    }
}
